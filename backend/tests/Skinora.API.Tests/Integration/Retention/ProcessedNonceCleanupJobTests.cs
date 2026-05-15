using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Retention;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Webhooks;
using Skinora.Shared.Tests.Integration;

namespace Skinora.API.Tests.Integration.Retention;

/// <summary>
/// T68 — coverage for <see cref="ProcessedNonceCleanupJob"/>. Verifies the
/// sweep deletes only rows past <see cref="ProcessedNonce.ExpiresAt"/> and
/// leaves still-valid replay markers in place.
/// </summary>
public class ProcessedNonceCleanupJobTests : IntegrationTestBase
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Expired_Rows_Are_Purged_Fresh_Rows_Preserved()
    {
        await SeedAsync(
            ("nonce-stale-1", DateTime.UtcNow.AddMinutes(-60)),
            ("nonce-stale-2", DateTime.UtcNow.AddSeconds(-10)),
            ("nonce-future", DateTime.UtcNow.AddMinutes(60)));

        var sut = NewJob();
        var deleted = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, deleted);

        var remaining = await Context.Set<ProcessedNonce>().AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("nonce-future", remaining[0].Nonce);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task No_Expired_Rows_Returns_Zero()
    {
        await SeedAsync(("nonce-fresh", DateTime.UtcNow.AddMinutes(30)));

        var sut = NewJob();
        var deleted = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(1, await Context.Set<ProcessedNonce>().CountAsync());
    }

    private async Task SeedAsync(params (string Nonce, DateTime ExpiresAt)[] rows)
    {
        foreach (var (nonce, expiresAt) in rows)
        {
            Context.Set<ProcessedNonce>().Add(new ProcessedNonce
            {
                Id = Guid.NewGuid(),
                Source = "steam-sidecar",
                Nonce = nonce,
                ProcessedAt = expiresAt.AddHours(-1),
                ExpiresAt = expiresAt,
            });
        }
        await Context.SaveChangesAsync();
    }

    private ProcessedNonceCleanupJob NewJob() =>
        new(Context, NullLogger<ProcessedNonceCleanupJob>.Instance);
}
