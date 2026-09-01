using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Calculations;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
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
    private readonly ISteamInventoryCacheInvalidator _inventoryCacheInvalidator;
    private readonly IPaymentAddressAllocator _paymentAddressAllocator;
    private readonly ILogger<TransactionCreationService> _logger;
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
        ISteamInventoryCacheInvalidator inventoryCacheInvalidator,
        IPaymentAddressAllocator paymentAddressAllocator,
        ILogger<TransactionCreationService> logger,
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
        _inventoryCacheInvalidator = inventoryCacheInvalidator;
        _paymentAddressAllocator = paymentAddressAllocator;
        _logger = logger;
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

        // ---------- Stage 5: seller lookup + Steam inventory ----------
        // T105a: a suspended seller cannot start a transaction (02 §14.0
        // fund-flow restriction) — treated as not-eligible at the guard
        // (defense-in-depth; the frontend restricted session is the primary gate).
        var seller = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == sellerId && !u.IsDeleted && !u.IsDeactivated && !u.IsSuspended,
                cancellationToken);
        if (seller is null)
            return Failure(CreateTransactionStatus.SellerNotFound, TransactionErrorCodes.AccountFlagged,
                "Seller not found.");

        // ---------- Stage 5b: seller payout address pipeline (02 §12.3) ----------
        // The address is read from the PROFILE, never from the request body.
        // Both controls 02 §12.3 assigns to this value — Steam re-authentication
        // and the `wallet.payout_address_cooldown_hours` window — live on the
        // profile write path (U3 `PUT /users/me/wallet/seller`). While the body
        // could name any address, a seller whose profile address was set more
        // than the cooldown ago could redirect every payout to a fresh address
        // without re-authenticating and without arming the cooldown, so neither
        // control protected the value that actually gets paid
        // (`SellerPayoutQueueJob` sends to `Transaction.SellerPayoutAddress`).
        // Reading the profile here makes the write path the single gate.
        //
        // Runs AFTER the seller lookup on purpose: the address now comes from
        // `seller`, so it cannot be validated before that entity is loaded.
        //
        // The two checks below are NOT redundant with `WalletAddressService`,
        // which validates on write:
        //   - format: rows can reach the column without passing that service
        //     (migrations, seeds, e2e `db.ts` writes `DefaultPayoutAddress` in
        //     raw SQL), so this stays defense-in-depth against a malformed row;
        //   - sanctions: the list GROWS after an address is stored. The write
        //     path can only screen against the list as it was that day; this is
        //     the only point that screens against the list as it is TODAY, so
        //     removing it would let an address sanctioned after it was saved go
        //     on opening transactions.
        var payoutAddress = seller.DefaultPayoutAddress;
        if (string.IsNullOrWhiteSpace(payoutAddress))
            return Failure(CreateTransactionStatus.SellerWalletAddressMissing,
                TransactionErrorCodes.SellerWalletAddressMissing,
                "Seller profile has no payout address (02 §12.3).");

        if (!_addressValidator.IsValid(payoutAddress))
            return Failure(CreateTransactionStatus.InvalidWallet, TransactionErrorCodes.InvalidWalletAddress,
                "Seller profile payout address fails TRC-20 validation (02 §12.3).");

        var sanctions = await _sanctions.EvaluateAsync(payoutAddress, cancellationToken);
        if (sanctions.IsMatch)
            return Failure(CreateTransactionStatus.SanctionsMatch, TransactionErrorCodes.SanctionsMatch,
                $"Seller profile payout address matched sanctions list '{sanctions.MatchedList}'.");

        // ---------- Stage 5a: one open transaction per item (02 §2.3) ----------
        // T128 — the rule is a money-safety rule, not a tidiness one: delivery
        // evidence is measured as a class-count delta (02 §9.2), so two live
        // transactions over the same asset let an arriving item be attributed
        // to the wrong one and pay the wrong seller. 06 §5.1 states the same
        // invariant as UQ_Transactions_SellerId_ItemAssetId_Active; without
        // this gate the seller met it as a 500.
        //
        // Placed between the seller lookup and the Steam read deliberately.
        // Before the lookup it would answer "already listed" to a suspended or
        // deleted seller, who must hear SellerNotFound; after the read it would
        // spend a rate-limited Steam round-trip on a request that cannot
        // succeed. The compared value is the requested asset id — the same key
        // the index is built on.
        var openListingId = await FindOpenListingAsync(
            sellerId, request.ItemAssetId, cancellationToken);
        if (openListingId is not null)
        {
            _logger.LogInformation(
                "Create rejected for seller {SellerId}: asset {ItemAssetId} is already committed to open transaction {TransactionId} (02 §2.3).",
                sellerId, request.ItemAssetId, openListingId);
            return Failure(CreateTransactionStatus.ItemAlreadyListed, TransactionErrorCodes.ItemAlreadyListed,
                "The seller already has an open transaction for this item (02 §2.3).");
        }

        // T121 — 08 §2.3: the read is three-valued and the three answers are
        // not interchangeable. Only a Public read licenses the "item is not in
        // the inventory" verdict; a hidden profile or an unreachable Steam is
        // absence of information and gets its own code, so the seller is not
        // sent to look for an item that is sitting right where they left it.
        // T123 — Cached is correct here and is a deliberate choice, not the old
        // default carried forward: the seller has just picked this asset from
        // the listing endpoint, which populated that very cache entry seconds
        // ago, and the invariant that actually protects the money is re-checked
        // fresh at confirm-ready (03 §2.3, 07 §7.6a) before any address is
        // revealed. Spending a second uncached Steam round-trip here would buy
        // no safety the later gate does not already provide.
        var lookup = await _inventory.GetItemAsync(
            seller.SteamId, request.ItemAssetId,
            InventoryReadFreshness.Cached, cancellationToken);
        switch (lookup.Visibility)
        {
            case InventoryVisibility.Private:
                return Failure(CreateTransactionStatus.InventoryPrivate, TransactionErrorCodes.InventoryPrivate,
                    "The seller's Steam inventory is private (07 §6.1).");
            case InventoryVisibility.Unavailable:
                return Failure(CreateTransactionStatus.SteamUnavailable, TransactionErrorCodes.SteamUnavailable,
                    "The seller's Steam inventory could not be read (08 §2.3).");
        }

        var inventoryItem = lookup.Item;
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

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var fraud = await _fraudPreCheck.EvaluateAsync(
            sellerId,
            inventoryItem.MarketHashName,
            request.Stablecoin,
            price,
            nowUtc,
            cancellationToken);

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
            SellerPayoutAddress = payoutAddress,
            PaymentTimeoutMinutes = paymentTimeoutMinutes,
            // CREATED state requires AcceptDeadline NOT NULL (06 §3.5);
            // FLAGGED keeps every deadline NULL until admin approval (03 §7).
            AcceptDeadline = status == TransactionStatus.CREATED
                ? nowUtc + TimeSpan.FromMinutes(limits.AcceptTimeoutMinutes ?? DefaultAcceptTimeoutMinutes)
                : null,
        };

        _db.Set<Transaction>().Add(transaction);

        // WP15 — genesis audit-trail row (06 §3.6 "ilk kayıtta null"). Records the
        // origin status (CREATED or FLAGGED) with PreviousStatus = null so the
        // TransactionHistory trail is complete from creation. The seller is the
        // actor (USER). Committed in the Stage-11 SaveChanges below.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus: null, TransactionHistoryRecorder.GenesisTrigger,
            ActorType.USER, sellerId, nowUtc);

        // ---------- Stage 9: pre-create fraud flag row (T54 / T55) ----------
        // When the pre-check decided FLAGGED, persist the matching FraudFlag
        // row in the same SaveChanges so an admin can never observe a
        // FLAGGED transaction without a flag row to review (02 §14.0,
        // 06 §3.12 invariant). The pre-check service owns the per-rule
        // detail shape (07 §9.3 — PRICE_DEVIATION / HIGH_VOLUME /
        // ABNORMAL_BEHAVIOR); the orchestrator just relays the result.
        if (status == TransactionStatus.FLAGGED)
        {
            await _flagWriter.StagePreCreateFlagAsync(
                userId: sellerId,
                transactionId: transaction.Id,
                type: fraud.FlagType!.Value,
                details: fraud.FlagDetailsJson!,
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

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbConstraintViolations.IsUnique(ex))
        {
            // T128 — the Stage 5a gate is a read, so two creates racing over
            // the same asset can both clear it. The unique index is the only
            // party that sees both inserts, and the loser must hear the same
            // answer the gate would have given rather than a 500.
            //
            // The collision is confirmed by re-reading, not by matching the
            // index name in the driver's message: Transactions carries a second
            // unique index (InviteToken), and a token collision reported as
            // "item already listed" would send the seller to fix something that
            // is not wrong. No conflicting row ⇒ not our constraint ⇒ rethrow.
            var conflictId = await FindOpenListingAsync(
                sellerId, transaction.ItemAssetId, cancellationToken);
            if (conflictId is null) throw;

            // The unit of work is abandoned. SaveChanges is atomic, so nothing
            // landed; clearing the tracker stops the staged transaction,
            // history, fraud-flag and outbox rows from being replayed by any
            // later save on this scoped context.
            _db.ChangeTracker.Clear();

            _logger.LogWarning(ex,
                "Create lost the uniqueness race for seller {SellerId}: asset {ItemAssetId} is committed to open transaction {TransactionId} (02 §2.3).",
                sellerId, transaction.ItemAssetId, conflictId);

            return Failure(CreateTransactionStatus.ItemAlreadyListed, TransactionErrorCodes.ItemAlreadyListed,
                "The seller already has an open transaction for this item (02 §2.3).");
        }

        // ---------- Stage 10b: best-effort inventory cache invalidation ----------
        // 08 §2.3 — the seller's inventory snapshot is now stale (the listed
        // item is committed to this transaction and will leave their inventory
        // when they send it to the buyer). The invalidator port is a no-op in tests and HTTP
        // in production; failures are swallowed inside the implementation
        // (cache miss costs at most the next 2-minute TTL window — never a
        // hard failure).
        await _inventoryCacheInvalidator.InvalidateAsync(seller.SteamId, cancellationToken);

        // ---------- Stage 10c: best-effort payment-address allocation (T70) ----------
        // 08 §3.2 — derive a Tron deposit address from the HD wallet and
        // persist a PaymentAddress row. Only runs for CREATED transactions;
        // FLAGGED transactions wait until admin approval transitions them
        // back to CREATED (future task entry point). Failures here are NOT
        // fatal: the EnsurePaymentAddressJob recurring sweep recovers any
        // transaction whose inline allocation lost the sidecar round-trip.
        if (status == TransactionStatus.CREATED)
        {
            var allocation = await _paymentAddressAllocator.AllocateAsync(
                transaction.Id, cancellationToken);
            if (allocation.Status is not PaymentAddressAllocationStatus.Created
                and not PaymentAddressAllocationStatus.AlreadyExisted)
            {
                _logger.LogWarning(
                    "Inline payment-address allocation skipped for transaction {TransactionId}: {Status} — {Message}. EnsurePaymentAddressJob will retry.",
                    transaction.Id, allocation.Status, allocation.ErrorMessage);
            }
        }

        // ---------- Stage 11: response ----------
        var response = new CreateTransactionResponse(
            Id: transaction.Id,
            Status: transaction.Status,
            InviteUrl: BuildInviteUrl(transaction),
            CreatedAt: transaction.CreatedAt,
            FlagReason: fraud.FlagType?.ToString());

        return new CreateTransactionOutcome(
            CreateTransactionStatus.Created,
            Body: response,
            ErrorCode: null,
            ErrorMessage: null);
    }

    /// <summary>
    /// T128 — the seller's open (non-terminal) transaction for this asset, if
    /// one exists (02 §2.3).
    /// </summary>
    /// <remarks>
    /// Mirrors <c>UQ_Transactions_SellerId_ItemAssetId_Active</c> (06 §5.1) leg
    /// for leg: same key, same six terminal exclusions. The index's
    /// <c>IsDeleted = 0</c> leg is not repeated here because the global
    /// soft-delete query filter already applies it — repeating it would read as
    /// if the two filters could diverge. Any drift between this predicate and
    /// the index shows up as a rejected insert rather than a wrong decision:
    /// the index is the arbiter, this query is how the seller hears about it.
    /// </remarks>
    private Task<Guid?> FindOpenListingAsync(
        Guid sellerId, string itemAssetId, CancellationToken cancellationToken)
        => _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t =>
                t.SellerId == sellerId &&
                t.ItemAssetId == itemAssetId &&
                t.Status != TransactionStatus.COMPLETED &&
                t.Status != TransactionStatus.CANCELLED_TIMEOUT &&
                t.Status != TransactionStatus.CANCELLED_SELLER &&
                t.Status != TransactionStatus.CANCELLED_BUYER &&
                t.Status != TransactionStatus.CANCELLED_ADMIN &&
                t.Status != TransactionStatus.REFUNDED)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

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
