using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Calculations;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Wallet;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Orchestrates the full <c>POST /transactions</c> happy path (T45 — 07 §7.2,
/// 03 §2.2). All side effects happen inside a single
/// <see cref="DbContext.SaveChangesAsync"/> so the entity insert,
/// <c>OutboxMessages</c> row and <c>User.PayoutAddressChangedAt</c>
/// snapshot land atomically.
/// </summary>
public sealed class TransactionCreationService : ITransactionCreationService
{
    /// <summary>
    /// Documented default <c>accept_timeout_minutes</c> when the SystemSetting
    /// row is unconfigured (02 §3, §16.2). The platform never ships with a
    /// missing row in production (settings bootstrap fails fast at startup),
    /// so this is a defensive fallback only.
    /// </summary>
    public const int DefaultAcceptTimeoutMinutes = 60;

    private const string SteamId64Prefix = "76561";
    private const int SteamId64Length = 17;

    private readonly AppDbContext _db;
    private readonly ITransactionEligibilityService _eligibility;
    private readonly ITransactionLimitsProvider _limits;
    private readonly ISteamInventoryReader _inventory;
    private readonly IFraudPreCheckService _fraudPreCheck;
    private readonly ITransactionFraudFlagWriter _flagWriter;
    private readonly ITrc20AddressValidator _addressValidator;
    private readonly IWalletSanctionsCheck _sanctions;
    private readonly IInvitationCodeGenerator _inviteCodes;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;

    public TransactionCreationService(
        AppDbContext db,
        ITransactionEligibilityService eligibility,
        ITransactionLimitsProvider limits,
        ISteamInventoryReader inventory,
        IFraudPreCheckService fraudPreCheck,
        ITransactionFraudFlagWriter flagWriter,
        ITrc20AddressValidator addressValidator,
        IWalletSanctionsCheck sanctions,
        IInvitationCodeGenerator inviteCodes,
        IOutboxService outbox,
        TimeProvider clock)
    {
        _db = db;
        _eligibility = eligibility;
        _limits = limits;
        _inventory = inventory;
        _fraudPreCheck = fraudPreCheck;
        _flagWriter = flagWriter;
        _addressValidator = addressValidator;
        _sanctions = sanctions;
        _inviteCodes = inviteCodes;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<CreateTransactionOutcome> CreateAsync(
        Guid sellerId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: cheap, in-memory request validation ----------
        if (!Enum.IsDefined(request.Stablecoin))
            return Validation("Unsupported stablecoin.");
        if (!Enum.IsDefined(request.BuyerIdentificationMethod))
            return Validation("Unsupported buyer identification method.");
        if (string.IsNullOrWhiteSpace(request.ItemAssetId))
            return Validation("itemAssetId is required.");
        if (string.IsNullOrWhiteSpace(request.SellerWalletAddress))
            return Validation("sellerWalletAddress is required.");

        if (!TryParsePositiveDecimal(request.Price, out var price))
            return Validation("price must be a positive decimal with up to 2 fractional digits.");

        // ---------- Stage 2: eligibility re-check ----------
        var eligibility = await _eligibility.GetAsync(sellerId, cancellationToken);
        if (!eligibility.Eligible)
        {
            var reason = eligibility.Reasons?.FirstOrDefault() ?? TransactionErrorCodes.AccountFlagged;
            return new CreateTransactionOutcome(
                MapEligibilityReason(reason),
                Body: null,
                ErrorCode: reason,
                ErrorMessage: $"Eligibility check failed: {reason}.");
        }

        // ---------- Stage 3: limits-driven validation ----------
        var limits = await _limits.GetAsync(cancellationToken);

        if (limits.MinTransactionAmount.HasValue && price < limits.MinTransactionAmount.Value)
            return Failure(CreateTransactionStatus.PriceOutOfRange, TransactionErrorCodes.PriceOutOfRange,
                $"Price {price} below configured minimum {limits.MinTransactionAmount.Value}.");
        if (limits.MaxTransactionAmount.HasValue && price > limits.MaxTransactionAmount.Value)
            return Failure(CreateTransactionStatus.PriceOutOfRange, TransactionErrorCodes.PriceOutOfRange,
                $"Price {price} above configured maximum {limits.MaxTransactionAmount.Value}.");

        var paymentTimeoutMinutes = request.PaymentTimeoutHours * 60;
        if (limits.PaymentTimeoutMinMinutes.HasValue && paymentTimeoutMinutes < limits.PaymentTimeoutMinMinutes.Value)
            return Failure(CreateTransactionStatus.TimeoutOutOfRange, TransactionErrorCodes.TimeoutOutOfRange,
                $"paymentTimeoutHours {request.PaymentTimeoutHours} below configured minimum.");
        if (limits.PaymentTimeoutMaxMinutes.HasValue && paymentTimeoutMinutes > limits.PaymentTimeoutMaxMinutes.Value)
            return Failure(CreateTransactionStatus.TimeoutOutOfRange, TransactionErrorCodes.TimeoutOutOfRange,
                $"paymentTimeoutHours {request.PaymentTimeoutHours} above configured maximum.");

        if (request.BuyerIdentificationMethod == BuyerIdentificationMethod.OPEN_LINK && !limits.OpenLinkEnabled)
            return Failure(CreateTransactionStatus.OpenLinkDisabled, TransactionErrorCodes.OpenLinkDisabled,
                "Open-link buyer identification is currently disabled (02 §6.2).");

        if (request.BuyerIdentificationMethod == BuyerIdentificationMethod.STEAM_ID
            && !IsSteamId64(request.BuyerSteamId))
            return Validation("buyerSteamId is required and must be a 17-digit Steam ID 64.");

        // ---------- Stage 4: seller wallet pipeline (02 §12.3) ----------
        if (!_addressValidator.IsValid(request.SellerWalletAddress))
            return Failure(CreateTransactionStatus.InvalidWallet, TransactionErrorCodes.InvalidWalletAddress,
                "sellerWalletAddress fails TRC-20 validation (02 §12.3).");

        var sanctions = await _sanctions.EvaluateAsync(request.SellerWalletAddress, cancellationToken);
        if (sanctions.IsMatch)
            return Failure(CreateTransactionStatus.SanctionsMatch, TransactionErrorCodes.SanctionsMatch,
                $"sellerWalletAddress matched sanctions list '{sanctions.MatchedList}'.");

        // ---------- Stage 5: seller lookup + Steam inventory ----------
        var seller = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == sellerId && !u.IsDeleted && !u.IsDeactivated, cancellationToken);
        if (seller is null)
            return Failure(CreateTransactionStatus.SellerNotFound, TransactionErrorCodes.AccountFlagged,
                "Seller not found.");

        var inventoryItem = await _inventory.TryGetItemAsync(
            seller.SteamId, request.ItemAssetId, cancellationToken);
        if (inventoryItem is null)
            return Failure(CreateTransactionStatus.ItemNotInInventory, TransactionErrorCodes.ItemNotInInventory,
                "Item is not in the seller's Steam inventory.");
        if (!inventoryItem.IsTradeable)
            return Failure(CreateTransactionStatus.ItemNotTradeable, TransactionErrorCodes.ItemNotTradeable,
                "Item has an active trade lock (02 §9).");

        // ---------- Stage 6: buyer resolution (Steam ID lookup) ----------
        Guid? buyerId = null;
        if (request.BuyerIdentificationMethod == BuyerIdentificationMethod.STEAM_ID)
        {
            // 02 §6.1 — buyer may be platform-registered (notification flow)
            // or unregistered (invite-link flow). Both cases are valid; we
            // only resolve the User.Id when present so that downstream
            // notification fan-out can include them.
            buyerId = await _db.Set<User>()
                .AsNoTracking()
                .Where(u => u.SteamId == request.BuyerSteamId && !u.IsDeleted && !u.IsDeactivated)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // ---------- Stage 7: commission + fraud pre-check ----------
        // T52: formulae centralised in FinancialCalculator (02 §5, 06 §8.3,
        // 09 §14.4). Snapshot semantics (09 §9.5) — the rate is captured at
        // creation, so a later admin change to commission_rate never
        // re-prices an in-flight transaction.
        var commissionRate = limits.CommissionRate ?? TransactionParamsService.DefaultCommissionRate;
        var commissionAmount = FinancialCalculator.CalculateCommission(price, commissionRate);
        var totalAmount = FinancialCalculator.CalculateTotal(price, commissionAmount);

        var fraud = await _fraudPreCheck.EvaluateAsync(
            inventoryItem.ClassId,
            inventoryItem.InstanceId,
            request.Stablecoin,
            price,
            cancellationToken);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var status = fraud.ShouldFlag ? TransactionStatus.FLAGGED : TransactionStatus.CREATED;

        // ---------- Stage 8: build entity ----------
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerIdentificationMethod = request.BuyerIdentificationMethod,
            TargetBuyerSteamId = request.BuyerIdentificationMethod == BuyerIdentificationMethod.STEAM_ID
                ? request.BuyerSteamId
                : null,
            // CK_Transactions_BuyerMethod_*: STEAM_ID ⇒ InviteToken NULL,
            // OPEN_LINK ⇒ InviteToken NOT NULL. Unregistered Steam-ID buyers
            // still get a shareable link, but it's the public transaction
            // path (/transactions/:id) — the matching is done by Steam ID
            // lookup once the buyer authenticates.
            InviteToken = request.BuyerIdentificationMethod == BuyerIdentificationMethod.OPEN_LINK
                ? _inviteCodes.Generate()
                : null,
            ItemAssetId = inventoryItem.AssetId,
            ItemClassId = inventoryItem.ClassId,
            ItemInstanceId = inventoryItem.InstanceId,
            ItemName = inventoryItem.Name,
            ItemIconUrl = inventoryItem.IconUrl,
            ItemExterior = inventoryItem.Exterior,
            ItemType = inventoryItem.Type,
            ItemInspectLink = inventoryItem.InspectLink,
            StablecoinType = request.Stablecoin,
            Price = price,
            CommissionRate = commissionRate,
            CommissionAmount = commissionAmount,
            TotalAmount = totalAmount,
            MarketPriceAtCreation = fraud.MarketPrice,
            SellerPayoutAddress = request.SellerWalletAddress,
            PaymentTimeoutMinutes = paymentTimeoutMinutes,
            // CREATED state requires AcceptDeadline NOT NULL (06 §3.5);
            // FLAGGED keeps every deadline NULL until admin approval (03 §7).
            AcceptDeadline = status == TransactionStatus.CREATED
                ? nowUtc + TimeSpan.FromMinutes(limits.AcceptTimeoutMinutes ?? DefaultAcceptTimeoutMinutes)
                : null,
        };

        _db.Set<Transaction>().Add(transaction);

        // ---------- Stage 9: pre-create fraud flag row (T54) ----------
        // When the pre-check decided FLAGGED, persist the matching FraudFlag
        // row in the same SaveChanges so an admin can never observe a
        // FLAGGED transaction without a flag row to review (02 §14.0,
        // 06 §3.12 invariant). The flag detail JSON shape is the
        // PRICE_DEVIATION payload from 07 §9.3 — input price + market
        // price + deviation percentage rounded to 4 decimal places to keep
        // the on-disk representation deterministic.
        if (status == TransactionStatus.FLAGGED)
        {
            var details = JsonSerializer.Serialize(new
            {
                inputPrice = price,
                marketPrice = fraud.MarketPrice ?? 0m,
                deviationPercent = fraud.DeviationRatio.HasValue
                    ? Math.Round(fraud.DeviationRatio.Value * 100m, 4)
                    : 0m,
            });
            await _flagWriter.StagePreCreateFlagAsync(
                userId: sellerId,
                transactionId: transaction.Id,
                type: FraudFlagType.PRICE_DEVIATION,
                details: details,
                cancellationToken);
        }

        // ---------- Stage 10: outbox publish (T62/T78–T80 consume) ----------
        await _outbox.PublishAsync(
            new TransactionCreatedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                SellerId: sellerId,
                BuyerId: buyerId,
                ItemName: transaction.ItemName,
                Price: price,
                Stablecoin: request.Stablecoin,
                OccurredAt: nowUtc),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // ---------- Stage 11: response ----------
        var response = new CreateTransactionResponse(
            Id: transaction.Id,
            Status: transaction.Status,
            InviteUrl: BuildInviteUrl(transaction),
            CreatedAt: transaction.CreatedAt,
            FlagReason: status == TransactionStatus.FLAGGED ? "PRICE_DEVIATION" : null);

        return new CreateTransactionOutcome(
            CreateTransactionStatus.Created,
            Body: response,
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static string BuildInviteUrl(Transaction transaction)
    {
        // 07 §7.2 sample: "/transactions/<id>". Frontend resolves the absolute
        // origin; the backend sticks to a relative path so Skinora stays
        // host-agnostic across review/staging/prod.
        return transaction.InviteToken is null
            ? $"/transactions/{transaction.Id:D}"
            : $"/invite/{transaction.InviteToken}";
    }

    private static bool IsSteamId64(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (candidate.Length != SteamId64Length) return false;
        if (!candidate.StartsWith(SteamId64Prefix, StringComparison.Ordinal)) return false;
        for (var i = 0; i < candidate.Length; i++)
        {
            if (!char.IsDigit(candidate[i])) return false;
        }
        return true;
    }

    private static bool TryParsePositiveDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return false;
        if (parsed <= 0) return false;
        if (decimal.Round(parsed, 2) != parsed) return false; // 07 §7.2 — 2-decimal contract
        value = parsed;
        return true;
    }

    private static CreateTransactionStatus MapEligibilityReason(string reason) => reason switch
    {
        TransactionErrorCodes.MobileAuthenticatorRequired => CreateTransactionStatus.EligibilityFailed,
        TransactionErrorCodes.AccountFlagged => CreateTransactionStatus.EligibilityFailed,
        TransactionErrorCodes.CancelCooldownActive => CreateTransactionStatus.EligibilityFailed,
        TransactionErrorCodes.ConcurrentLimitReached => CreateTransactionStatus.EligibilityFailed,
        TransactionErrorCodes.NewAccountLimitReached => CreateTransactionStatus.EligibilityFailed,
        TransactionErrorCodes.PayoutAddressCooldownActive => CreateTransactionStatus.PayoutAddressCooldownActive,
        TransactionErrorCodes.SellerWalletAddressMissing => CreateTransactionStatus.SellerWalletAddressMissing,
        _ => CreateTransactionStatus.EligibilityFailed,
    };

    private static CreateTransactionOutcome Validation(string message) =>
        Failure(CreateTransactionStatus.ValidationFailed, TransactionErrorCodes.ValidationError, message);

    private static CreateTransactionOutcome Failure(
        CreateTransactionStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
