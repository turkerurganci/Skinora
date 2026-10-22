using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Shared.Enums;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>T169a — the per-network lock rows of the deposit refund daily limit (06 §3.25).</summary>
public class DepositRefundLimitLockConfiguration : IEntityTypeConfiguration<DepositRefundLimitLock>
{
    public void Configure(EntityTypeBuilder<DepositRefundLimitLock> builder)
    {
        builder.ToTable("DepositRefundLimitLocks");
        builder.HasKey(l => l.Network);
        builder.Property(l => l.Network)
            .HasMaxLength(20)
            .ValueGeneratedNever();
        builder.Property(l => l.LockedAt);

        // One row per network, always there: the lock is an UPDATE of the row.
        builder.HasData(Enum.GetValues<BlockchainNetwork>()
            .Select(n => new DepositRefundLimitLock { Network = n, LockedAt = null }));
    }
}
