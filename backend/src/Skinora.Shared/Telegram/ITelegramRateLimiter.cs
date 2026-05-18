namespace Skinora.Shared.Telegram;

/// <summary>
/// Per-chat + global rate gate for Telegram <c>sendMessage</c> calls
/// (08 §5.3). Extracted as an interface so the channel handler can be
/// unit-tested with a spy that records the retry-after handshake
/// without driving real <see cref="Task.Delay(TimeSpan)"/> waits.
/// </summary>
public interface ITelegramRateLimiter
{
    Task WaitAsync(string chatId, CancellationToken cancellationToken);

    void RegisterRetryAfter(string chatId, int seconds);
}
