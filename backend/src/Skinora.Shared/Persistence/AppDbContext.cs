using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Skinora.Shared.Domain;
using Skinora.Shared.Persistence.Converters;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.Shared.Persistence;

public class AppDbContext : DbContext
{
    private static readonly List<Assembly> _moduleAssemblies = [];

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Registers a module assembly so its IEntityTypeConfiguration implementations
    /// are discovered during OnModelCreating. Call once per module at startup.
    /// </summary>
    public static void RegisterModuleAssembly(Assembly assembly)
    {
        if (!_moduleAssemblies.Contains(assembly))
            _moduleAssemblies.Add(assembly);
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

        // T17 / 06 §2: All Skinora enums stored as strings in DB
        var enumNamespace = typeof(Skinora.Shared.Enums.TransactionStatus).Namespace!;
        var enumTypes = typeof(Skinora.Shared.Enums.TransactionStatus).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == enumNamespace);

        foreach (var enumType in enumTypes)
        {
            configurationBuilder.Properties(enumType)
                .HaveConversion(typeof(EnumToStringConverter<>).MakeGenericType(enumType));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply IEntityTypeConfiguration<T> classes from the shared persistence layer
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Apply IEntityTypeConfiguration<T> classes from registered module assemblies
        foreach (var assembly in _moduleAssemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // 09 §10.3: Soft delete global query filter for ISoftDeletable entities
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                ApplySoftDeleteFilter(modelBuilder, entityType.ClrType);
            }

            // 09 §10.4: RowVersion concurrency token for BaseEntity descendants.
            // Production uses SQL Server, where IsRowVersion() maps to the
            // server-generated `rowversion` type. Test hosts that use SQLite
            // (OutboxTests, EfCoreGlobalConfigTests, API middleware tests)
            // get a plain concurrency-tracked byte[] with a zero default so
            // HasData seed inserts — which EF would otherwise omit the column
            // from, expecting server generation — still satisfy NOT NULL.
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var rowVersion = modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.RowVersion));

                // Provider is checked by name to avoid pulling the SQL Server
                // or SQLite packages into Skinora.Shared.
                if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
                {
                    rowVersion.IsRowVersion();
                }
                else
                {
                    rowVersion
                        .IsRequired()
                        .IsConcurrencyToken()
                        .HasDefaultValue(new byte[8]);
                }
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
        EnforceAppendOnly();
        UpdateAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnforceAppendOnly();
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

    // 06 §4.2: Append-only entity'lerde INSERT sonrası UPDATE/DELETE tanımlı
    // değil. Rule is enforced at the DbContext so every caller — including
    // background jobs and ad-hoc maintenance paths — is bound by it.
    private void EnforceAppendOnly()
    {
        foreach (var entry in ChangeTracker.Entries<IAppendOnly>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{entry.Entity.GetType().Name} is append-only (06 §4.2) — " +
                    $"{entry.State} is not permitted.");
            }
        }
    }
}
