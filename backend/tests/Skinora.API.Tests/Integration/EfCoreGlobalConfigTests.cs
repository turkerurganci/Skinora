using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Domain;
using Skinora.Shared.Persistence;

namespace Skinora.API.Tests.Integration;

#region Test Entities

public class TestEntity : BaseEntity, ISoftDeletable, IAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class TestParentEntity : BaseEntity, IAuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public ICollection<TestChildEntity> Children { get; set; } = [];
}

public class TestChildEntity : BaseEntity, IAuditableEntity
{
    public Guid ParentId { get; set; }
    public TestParentEntity Parent { get; set; } = null!;
}

#endregion

#region Test DbContext

public class TestDbContext : AppDbContext
{
    public TestDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<TestEntity> TestEntities => Set<TestEntity>();
    public DbSet<TestParentEntity> TestParents => Set<TestParentEntity>();
    public DbSet<TestChildEntity> TestChildren => Set<TestChildEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TestEntity>(b =>
        {
            b.HasKey(e => e.Id);
            // SQLite doesn't support byte[] RowVersion natively, configure as concurrency token
            b.Property(e => e.RowVersion)
                .IsConcurrencyToken()
                .HasDefaultValue(Array.Empty<byte>());
        });

        modelBuilder.Entity<TestParentEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasMany(e => e.Children)
                .WithOne(e => e.Parent)
                .HasForeignKey(e => e.ParentId);
            b.Property(e => e.RowVersion)
                .IsConcurrencyToken()
                .HasDefaultValue(Array.Empty<byte>());
        });

        modelBuilder.Entity<TestChildEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.RowVersion)
                .IsConcurrencyToken()
                .HasDefaultValue(Array.Empty<byte>());
        });
    }
}

#endregion

public class EfCoreGlobalConfigTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _context;

    public EfCoreGlobalConfigTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TestDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task UtcConverter_ReadDateTime_ReturnsUtcKind()
    {
        // Arrange: insert a record with a known DateTime
        var entity = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "UTC Test",
            CreatedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc)
        };

        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // Act: read from a fresh context to ensure value comes from DB
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var readContext = new TestDbContext(options);
        var loaded = await readContext.TestEntities.FirstAsync(e => e.Id == entity.Id);

        // Assert: DateTime.Kind should be Utc (converter applied)
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, loaded.UpdatedAt.Kind);
    }

    [Fact]
    public async Task SoftDeleteFilter_ExcludesDeletedEntities()
    {
        // Arrange
        var active = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Active",
            IsDeleted = false
        };
        var deleted = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Deleted",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };

        _context.TestEntities.AddRange(active, deleted);
        await _context.SaveChangesAsync();

        // Act: query without IgnoreQueryFilters
        var results = await _context.TestEntities.ToListAsync();

        // Assert: only active entity returned
        Assert.Single(results);
        Assert.Equal("Active", results[0].Name);
    }

    [Fact]
    public async Task SoftDeleteFilter_IgnoreQueryFilters_ReturnsDeletedEntities()
    {
        // Arrange
        var active = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Active2",
            IsDeleted = false
        };
        var deleted = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Deleted2",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };

        _context.TestEntities.AddRange(active, deleted);
        await _context.SaveChangesAsync();

        // Act: query with IgnoreQueryFilters
        var results = await _context.TestEntities
            .IgnoreQueryFilters()
            .Where(e => e.Name.EndsWith("2"))
            .ToListAsync();

        // Assert: both entities returned
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void ForeignKeys_HaveNoActionDeleteBehavior()
    {
        // Act: inspect the model for FK delete behaviors
        var childEntityType = _context.Model.FindEntityType(typeof(TestChildEntity))!;
        var foreignKeys = childEntityType.GetForeignKeys();

        // Assert: all FKs should have NoAction
        foreach (var fk in foreignKeys)
        {
            Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void RowVersion_IsConfigured_OnBaseEntity()
    {
        // Act: check model configuration for RowVersion
        var entityType = _context.Model.FindEntityType(typeof(TestEntity))!;
        var rowVersionProp = entityType.FindProperty(nameof(BaseEntity.RowVersion))!;

        // Assert: property exists and is concurrency token
        Assert.True(rowVersionProp.IsConcurrencyToken);
    }

    [Fact]
    public async Task AuditFields_SetAutomatically_OnAdd()
    {
        // Arrange
        var entity = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Audit Test"
        };

        var beforeSave = DateTime.UtcNow;

        // Act
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // Assert
        Assert.True(entity.CreatedAt >= beforeSave);
        Assert.True(entity.UpdatedAt >= beforeSave);
        Assert.Equal(entity.CreatedAt, entity.UpdatedAt);
    }

    [Fact]
    public async Task AuditFields_UpdatedAt_ChangesOnModify()
    {
        // Arrange
        var entity = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Audit Update"
        };

        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync();

        var originalCreatedAt = entity.CreatedAt;
        var originalUpdatedAt = entity.UpdatedAt;

        // Small delay to ensure timestamp difference
        await Task.Delay(10);

        // Act: modify the entity
        entity.Name = "Audit Update Modified";
        await _context.SaveChangesAsync();

        // Assert: CreatedAt unchanged, UpdatedAt advanced
        Assert.Equal(originalCreatedAt, entity.CreatedAt);
        Assert.True(entity.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public async Task NonSoftDeletableEntity_NoQueryFilter_Applied()
    {
        // Arrange: TestParentEntity does NOT implement ISoftDeletable
        var parent = new TestParentEntity
        {
            Id = Guid.NewGuid(),
            Title = "Parent"
        };

        _context.TestParents.Add(parent);
        await _context.SaveChangesAsync();

        // Act
        var results = await _context.TestParents.ToListAsync();

        // Assert: no filter applied, entity returned
        Assert.Single(results);
    }
}
