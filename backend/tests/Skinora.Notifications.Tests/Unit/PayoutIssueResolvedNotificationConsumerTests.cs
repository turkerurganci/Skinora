using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Infrastructure.Email;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Email;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Backlog <c>F7Gate-EventsWithoutConsumer</c> (second half) — the producer for
/// <see cref="NotificationType.PAYOUT_ISSUE_RESOLVED"/>.
/// </summary>
/// <remarks>
/// The event was published on both resolution paths and consumed by nobody, so
/// the seller who asked "where is my payout?" was the only participant never
/// told the answer. These tests pin the two things that made this gap worth a
/// new notification type rather than a reuse: it must reach the SELLER, and it
/// must carry no parameters — the near-miss type promises an amount this event
/// does not have.
/// </remarks>
[Trait("Category", "Unit")]
public class PayoutIssueResolvedNotificationConsumerTests
{
    private static readonly Guid Seller = Guid.NewGuid();
    private static readonly Guid Tx = Guid.NewGuid();

    private static SellerPayoutIssueResolvedEvent Event(
        string? payoutTxHash, Guid? resolvedByAdminId) => new(
            EventId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            TransactionId: Tx,
            SellerId: Seller,
            PayoutTxHash: payoutTxHash,
            ResolvedByAdminId: resolvedByAdminId,
            OccurredAt: DateTime.UtcNow);

    private static async Task<RecordingNotificationDispatcher> HandleAsync(
        SellerPayoutIssueResolvedEvent domainEvent)
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new PayoutIssueResolvedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<PayoutIssueResolvedNotificationConsumer>.Instance);

        await sut.Handle(domainEvent, CancellationToken.None);
        return dispatcher;
    }

    [Fact]
    public async Task ChainConfirmedResolution_Notifies_The_Seller()
    {
        var dispatcher = await HandleAsync(Event("0xpayout-hash", resolvedByAdminId: null));

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(Seller, request.UserId);
        Assert.Equal(NotificationType.PAYOUT_ISSUE_RESOLVED, request.Type);
        Assert.Equal(Tx, request.TransactionId);
    }

    [Fact]
    public async Task AdminResolution_Notifies_The_Seller_The_Same_Way()
    {
        var dispatcher = await HandleAsync(
            Event(payoutTxHash: null, resolvedByAdminId: Guid.NewGuid()));

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(Seller, request.UserId);
        Assert.Equal(NotificationType.PAYOUT_ISSUE_RESOLVED, request.Type);
        // Deliberately identical to the chain path: the seller's question is
        // "is my payout sorted?", and our internal resolution route is not part
        // of the answer.
        Assert.Equal(Tx, request.TransactionId);
    }

    [Fact]
    public async Task Carries_No_Template_Parameters()
    {
        var dispatcher = await HandleAsync(Event("0xpayout-hash", resolvedByAdminId: null));

        // This is the WP7 lesson encoded as a test. A template that asks for a
        // value its producer does not have renders the literal placeholder to
        // the user (05 §7.3 substitutes rather than throws) — which is exactly
        // why SELLER_PAYMENT_SENT and its {Amount} could not be reused here.
        Assert.Empty(Assert.Single(dispatcher.Requests).Parameters);
    }

    [Fact]
    public void Is_Categorised_For_Email()
    {
        // EmailCategoryMap throws for an unmapped type, so a new notification
        // that reaches the email channel without an entry fails in production
        // rather than in a test. Pin it here instead.
        Assert.Equal(
            EmailCategory.Transaction,
            EmailCategoryMap.Resolve(NotificationType.PAYOUT_ISSUE_RESOLVED));
    }

    [Fact]
    public async Task Replayed_Event_Does_Not_Notify_Twice()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var store = new InMemoryProcessedEventStore();
        var sut = new PayoutIssueResolvedNotificationConsumer(
            dispatcher, store,
            NullLogger<PayoutIssueResolvedNotificationConsumer>.Instance);
        var domainEvent = Event("0xpayout-hash", resolvedByAdminId: null);

        await sut.Handle(domainEvent, CancellationToken.None);
        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Single(dispatcher.Requests);
    }
}
