using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Skinora.Shared.Telegram;

/// <summary>
/// Coordinates Telegram <c>sendMessage</c> throughput against the
/// documented limits (08 §5.3):
/// </summary>
/// <list type="bullet">
///   <item><c>PerChatRatePerSecond</c> messages per second per chat (default 1).</item>
///   <item><c>GlobalRatePerSecond</c> messages per second across all chats (default 30).</item>
/// </list>
/// <remarks>
/// <para>
/// Per-chat ordering is preserved via a <see cref="SemaphoreSlim"/>(1,1)
/// per chat id; the global limit is enforced with a sliding-window
/// timestamp list. Both gates are observed before <c>sendMessage</c>
/// runs; if a retry-after is supplied (Telegram 429) the caller passes
/// it back via <see cref="RegisterRetryAfter"/> so the chat is paused
/// for the requested interval.
/// </para>
/// <para>
/// The clock is injected as a <see cref="Func{DateTimeOffset}"/> so
/// unit tests can advance time deterministically without
/// <c>Task.Delay</c>.
/// </para>
/// </remarks>
public sealed class TelegramRateLimiter : ITelegramRateLimiter, IDisposable
{
    private readonly TelegramSettings _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, ChatGate> _chats = new(StringComparer.Ordinal);
    private readonly object _globalLock = new();
    private readonly LinkedList<DateTimeOffset> _globalSends = new();

    public TelegramRateLimiter(IOptions<TelegramSettings> settings)
        : this(settings.Value, () => DateTimeOffset.UtcNow)
    {
    }

    public TelegramRateLimiter(TelegramSettings settings, Func<DateTimeOffset> clock)
    {
        _settings = settings;
        _clock = clock;
    }

    /// <summary>
    /// Awaits both the per-chat and global gates. Returns when the
    /// caller is free to <c>POST sendMessage</c>; the next send for
    /// this chat will be blocked until 1/perChatRate has passed.
    /// </summary>
    public async Task WaitAsync(string chatId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatId);

        var gate = _chats.GetOrAdd(chatId, _ => new ChatGate());
        await gate.Semaphore.WaitAsync(cancellationToken);

        try
        {
            await WaitForPerChatBudgetAsync(gate, cancellationToken);
            await WaitForGlobalBudgetAsync(cancellationToken);

            var now = _clock();
            gate.LastSendUtc = now;
            RecordGlobalSend(now);
        }
        finally
        {
            // Per-chat semaphore released only after the send window is
            // booked. The caller does the HTTP POST outside the gate;
            // the next WaitAsync for the same chat will then see the
            // booked timestamp and pause as needed.
            gate.Semaphore.Release();
        }
    }

    /// <summary>
    /// Honours a Telegram <c>retry_after</c> by holding the chat gate
    /// for the specified seconds. Called by the bot client after a 429.
    /// </summary>
    public void RegisterRetryAfter(string chatId, int seconds)
    {
        if (string.IsNullOrEmpty(chatId) || seconds <= 0)
        {
            return;
        }

        var gate = _chats.GetOrAdd(chatId, _ => new ChatGate());
        gate.RetryAfterUtc = _clock().AddSeconds(seconds);
    }

    private async Task WaitForPerChatBudgetAsync(ChatGate gate, CancellationToken cancellationToken)
    {
        if (_settings.PerChatRatePerSecond <= 0)
        {
            return;
        }

        var minInterval = TimeSpan.FromSeconds(1.0 / _settings.PerChatRatePerSecond);

        while (true)
        {
            var now = _clock();
            var wait = TimeSpan.Zero;

            if (gate.RetryAfterUtc is { } retry && retry > now)
            {
                wait = retry - now;
            }
            else if (gate.LastSendUtc is { } last)
            {
                var elapsed = now - last;
                if (elapsed < minInterval)
                {
                    wait = minInterval - elapsed;
                }
            }

            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(wait, cancellationToken);
        }
    }

    private async Task WaitForGlobalBudgetAsync(CancellationToken cancellationToken)
    {
        if (_settings.GlobalRatePerSecond <= 0)
        {
            return;
        }

        while (true)
        {
            TimeSpan wait;
            lock (_globalLock)
            {
                TrimGlobalSends(_clock());
                if (_globalSends.Count < _settings.GlobalRatePerSecond)
                {
                    return;
                }

                // Oldest timestamp + 1 second is when the window slides
                // far enough to admit one more send.
                wait = _globalSends.First!.Value.AddSeconds(1) - _clock();
            }

            if (wait <= TimeSpan.Zero)
            {
                continue;
            }

            await Task.Delay(wait, cancellationToken);
        }
    }

    private void RecordGlobalSend(DateTimeOffset now)
    {
        lock (_globalLock)
        {
            _globalSends.AddLast(now);
            TrimGlobalSends(now);
        }
    }

    private void TrimGlobalSends(DateTimeOffset now)
    {
        var cutoff = now.AddSeconds(-1);
        while (_globalSends.First is { } first && first.Value <= cutoff)
        {
            _globalSends.RemoveFirst();
        }
    }

    public void Dispose()
    {
        foreach (var gate in _chats.Values)
        {
            gate.Semaphore.Dispose();
        }
    }

    private sealed class ChatGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public DateTimeOffset? LastSendUtc { get; set; }
        public DateTimeOffset? RetryAfterUtc { get; set; }
    }
}
