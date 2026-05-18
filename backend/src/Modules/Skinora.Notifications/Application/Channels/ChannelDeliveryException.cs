namespace Skinora.Notifications.Application.Channels;

/// <summary>
/// Base type for failures surfaced by an
/// <see cref="INotificationChannelHandler"/>. Always throw one of the
/// two subclasses (<see cref="TransientChannelDeliveryException"/> /
/// <see cref="PermanentChannelDeliveryException"/>) so the delivery
/// pipeline can classify the failure correctly (T78 — 08 §4.3, 05 §7.5).
/// </summary>
/// <remarks>
/// <para>
/// Any other exception type bubbling out of <see cref="INotificationChannelHandler.SendAsync"/>
/// is conservatively treated as transient — the immediate retry tier
/// will pick the row up. This lets channel handlers that have not yet
/// been hardened (T79 Telegram stub, T80 Discord stub) keep working
/// without leaking provider-specific exception types into the dispatcher.
/// </para>
/// </remarks>
public abstract class ChannelDeliveryException : Exception
{
    protected ChannelDeliveryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Transient channel failure — retry per the immediate (1 dk / 5 dk /
/// 15 dk) and then deferred (30 dk / 1 sa / 4 sa) backoff tiers.
/// </summary>
public sealed class TransientChannelDeliveryException : ChannelDeliveryException
{
    public TransientChannelDeliveryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Permanent channel failure — flip the row to <c>FAILED</c>
/// immediately, fire the admin alert, no retry, no DEFERRED.
/// </summary>
public sealed class PermanentChannelDeliveryException : ChannelDeliveryException
{
    public PermanentChannelDeliveryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
