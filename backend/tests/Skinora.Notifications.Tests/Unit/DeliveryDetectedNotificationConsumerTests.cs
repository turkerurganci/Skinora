using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Resources;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// P2P-DeliveryPollingJob (owner decision 2026-10-02) — the poll saw the item
/// arrive behind a closed launch gate; the BUYER is asked to check and confirm.
/// </summary>
[Trait("Category", "Unit")]
public class DeliveryDetectedNotificationConsumerTests
{
    private static readonly Guid Buyer = Guid.NewGuid();
    private static readonly Guid Tx = Guid.NewGuid();

    private static DeliveryDetectedEvent Event() => new(
        EventId: Guid.NewGuid(),
        TransactionId: Tx,
        BuyerId: Buyer,
        ItemName: "AK-47 | Redline (Field-Tested)",
        OccurredAt: DateTime.UtcNow);

    private static DeliveryDetectedNotificationConsumer BuildSut(
        RecordingNotificationDispatcher dispatcher, InMemoryProcessedEventStore? store = null) =>
        new(dispatcher, store ?? new InMemoryProcessedEventStore(),
            NullLogger<DeliveryDetectedNotificationConsumer>.Instance);

    [Fact]
    public async Task Notifies_Only_The_Buyer_With_The_Item_Name()
    {
        var dispatcher = new RecordingNotificationDispatcher();

        await BuildSut(dispatcher).Handle(Event(), CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(Buyer, request.UserId);
        Assert.Equal(NotificationType.DELIVERY_DETECTED, request.Type);
        Assert.Equal(Tx, request.TransactionId);
        Assert.Equal("AK-47 | Redline (Field-Tested)", request.Parameters["ItemName"]);
    }

    [Fact]
    public async Task Replayed_Event_Does_Not_Notify_Twice()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = BuildSut(dispatcher, new InMemoryProcessedEventStore());
        var domainEvent = Event();

        await sut.Handle(domainEvent, CancellationToken.None);
        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Single(dispatcher.Requests);
    }

    /// <summary>
    /// Every placeholder a template asks for must be one the consumer sends —
    /// 05 §7.3 renders a missing one as the literal "{Name}" to the user.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("tr")]
    [InlineData("es")]
    [InlineData("zh")]
    public void Templates_Ask_Only_For_The_ItemName_The_Consumer_Sends(string locale)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        using var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<NotificationTemplates>>();

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = string.IsNullOrEmpty(locale)
            ? CultureInfo.InvariantCulture
            : new CultureInfo(locale);
        try
        {
            foreach (var key in new[] { "DELIVERY_DETECTED_Title", "DELIVERY_DETECTED_Body" })
            {
                var value = localizer[key];
                Assert.False(value.ResourceNotFound, $"{key} missing for '{locale}'");
                var placeholders = System.Text.RegularExpressions.Regex
                    .Matches(value.Value, @"\{(\w+)\}")
                    .Select(m => m.Groups[1].Value)
                    .ToHashSet(StringComparer.Ordinal);
                Assert.True(
                    placeholders.IsSubsetOf(["ItemName"]),
                    $"{key} ({locale}) asks for {string.Join(", ", placeholders)}");
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
