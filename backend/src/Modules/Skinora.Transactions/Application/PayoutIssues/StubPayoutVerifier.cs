namespace Skinora.Transactions.Application.PayoutIssues;

/// <summary>
/// Conservative <see cref="IPayoutVerifier"/> that always returns
/// <see cref="PayoutVerificationOutcome.UnableToVerify"/>. Forward-deferred
/// until the Tron blockchain sidecar (T64–T69) ships a real implementation.
/// The fail-closed default ensures every seller-reported payout issue
/// reaches an admin instead of silently dangling.
/// </summary>
/// <remarks>
/// Mirrors the T31 <c>StubMobileAuthenticatorCheck</c> contract: the stub is
/// safe in production, gives deterministic behavior in tests, and is replaced
/// at DI level when the real sidecar lands.
/// </remarks>
public sealed class StubPayoutVerifier : IPayoutVerifier
{
    public Task<PayoutVerificationResult> VerifyAsync(
        Guid transactionId,
        string? expectedPayoutTxHash,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new PayoutVerificationResult(
            Outcome: PayoutVerificationOutcome.UnableToVerify,
            VerifiedTxHash: null,
            Message: "Blockchain doğrulaması bekleniyor — admin incelemesi gerekiyor."));
    }
}
