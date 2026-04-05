namespace Skinora.Shared.Enums;

public enum OutboxMessageStatus
{
    PENDING,
    PROCESSED,
    DEFERRED,
    FAILED
}
