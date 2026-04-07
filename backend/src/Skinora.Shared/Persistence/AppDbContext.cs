using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Domain;
using Skinora.Shared.Persistence.Converters;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.Shared.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // T10 — outbox infrastructure tables (06 §3.18–§3.21). The pattern lives
    // in F0 because the dispatcher and consumer-idempotency abstractions need
    // working tables; T25 (F1) refines any additional altyapı entities.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<ExternalIdempotencyRecord> ExternalIdempotencyRecords
        => Set<ExternalIdempotencyRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 09 §7.1: All DateTime properties use UTC converter
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply IEntityTypeConfiguration<T> classes that live alongside the
        // shared persistence layer (currently the T10 outbox configurations).
        // Module-owned configurations are still registered separately by each
        // module's DbContext extension.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // 09 §10.3: Soft delete global query filter for ISoftDeletable entities
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                ApplySoftDeleteFilter(modelBuilder, entityType.ClrType);
            }

            // 09 §10.4: RowVersion concurrency token for BaseEntity descendants
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.RowVersion))
                    .IsRowVersion();
            }

            // 09 §10.6: DeleteBehavior.NoAction for all FK relationships
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
            }
        }
    }

    private static void ApplySoftDeleteFilter(ModelBuilder modelBuilder, Type entityType)
    {
        // Build: entity => !((ISoftDeletable)entity).IsDeleted
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var isDeletedProperty = System.Linq.Expressions.Expression.Property(
            System.Linq.Expressions.Expression.Convert(parameter, typeof(ISoftDeletable)),
            nameof(ISoftDeletable.IsDeleted));
        var notDeleted = System.Linq.Expressions.Expression.Not(isDeletedProperty);
        var lambda = System.Linq.Expressions.Expression.Lambda(notDeleted, parameter);

        modelBuilder.Entity(entityType).HasQueryFilter(lambda);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateAuditFields();
        return base.SaveChanges();
    }

    private void UpdateAuditFields()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
