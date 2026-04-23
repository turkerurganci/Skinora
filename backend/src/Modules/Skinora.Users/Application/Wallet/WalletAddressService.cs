using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Wallet;

public sealed class WalletAddressService : IWalletAddressService
{
    private readonly AppDbContext _db;
    private readonly ITrc20AddressValidator _addressValidator;
    private readonly IWalletSanctionsCheck _sanctions;
    private readonly IActiveTransactionCounter _activeCounter;
    private readonly TimeProvider _clock;

    public WalletAddressService(
        AppDbContext db,
        ITrc20AddressValidator addressValidator,
        IWalletSanctionsCheck sanctions,
        IActiveTransactionCounter activeCounter,
        TimeProvider clock)
    {
        _db = db;
        _addressValidator = addressValidator;
        _sanctions = sanctions;
        _activeCounter = activeCounter;
        _clock = clock;
    }

    public async Task<WalletUpdateResult> UpdateWalletAsync(
        Guid userId,
        WalletRole role,
        string? newAddress,
        bool reAuthValidated,
        CancellationToken cancellationToken)
    {
        if (!_addressValidator.IsValid(newAddress))
            return WalletUpdateResult.Failure(WalletUpdateStatus.InvalidAddress);

        // Validator guarantees non-null, trimmed-equivalent content when IsValid returns true.
        var candidate = newAddress!;

        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null)
            return WalletUpdateResult.Failure(WalletUpdateStatus.UserNotFound);

        var previous = role == WalletRole.Seller
            ? user.DefaultPayoutAddress
            : user.DefaultRefundAddress;

        // 02 §12.3 / 07 §5.3 "Ek Auth": changing an existing address requires
        // a valid X-ReAuth-Token. The controller consumes the token and passes
        // the outcome via reAuthValidated — see UsersController.
        if (!string.IsNullOrEmpty(previous) && !reAuthValidated)
            return WalletUpdateResult.Failure(WalletUpdateStatus.ReAuthRequired);

        var sanctions = await _sanctions.EvaluateAsync(candidate, cancellationToken);
        if (sanctions.IsMatch)
            return WalletUpdateResult.Failure(
                WalletUpdateStatus.SanctionsMatch, sanctions.MatchedList);

        var activeUsingOld = 0;
        if (!string.IsNullOrEmpty(previous) && previous != candidate)
        {
            activeUsingOld = await _activeCounter.CountActiveUsingAddressAsync(
                userId, role, previous, cancellationToken);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        if (role == WalletRole.Seller)
        {
            user.DefaultPayoutAddress = candidate;
            user.PayoutAddressChangedAt = now;
        }
        else
        {
            user.DefaultRefundAddress = candidate;
            user.RefundAddressChangedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return WalletUpdateResult.Success(candidate, user.UpdatedAt, activeUsingOld);
    }
}
