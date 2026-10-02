namespace CustomerApi.Infrastructure.Data;
using CustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// customer_db only (CLAUDE.md rule 1, NFR-6). The schema is owned by
/// <c>services/customer/db/migrations</c>; this model mirrors it and never generates migrations.
/// </summary>
public class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<MergeHistory> MergeHistories => Set<MergeHistory>();
    public DbSet<Picklist> Picklists => Set<Picklist>();
    public DbSet<ReassignmentQueueEntry> ReassignmentQueue => Set<ReassignmentQueueEntry>();
    public DbSet<UserRef> UserRefs => Set<UserRef>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Domain).HasColumnType("citext");
            entity.Property(e => e.Tags).HasColumnType("text[]");
            entity.Property(e => e.CustomFields).HasColumnType("jsonb");
            entity.Property(e => e.AnnualRevenue).HasColumnType("numeric(16,2)");
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(c => c.Industry)
                  .WithMany()
                  .HasForeignKey(c => c.IndustryId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(c => c.Contacts)
                  .WithOne(c => c.Company!)
                  .HasForeignKey(c => c.CompanyId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.OrganizationId, e.OwnerId });
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("contacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Email).HasColumnType("citext");
            entity.Property(e => e.Tags).HasColumnType("text[]");
            entity.Property(e => e.CustomFields).HasColumnType("jsonb");
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(c => c.Source)
                  .WithMany()
                  .HasForeignKey(c => c.SourceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.OrganizationId, e.OwnerId });
        });

        modelBuilder.Entity<CustomFieldDefinition>(entity =>
        {
            entity.ToTable("custom_field_definitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Options).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.OrganizationId, e.EntityType, e.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<MergeHistory>(entity =>
        {
            entity.ToTable("merge_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.FieldChoices).HasColumnType("jsonb");
        });

        modelBuilder.Entity<Picklist>(entity =>
        {
            entity.ToTable("picklists");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.OrganizationId, e.ListType, e.Value }).IsUnique();
        });

        modelBuilder.Entity<ReassignmentQueueEntry>(entity =>
        {
            entity.ToTable("reassignment_queue");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<UserRef>(entity =>
        {
            entity.ToTable("user_refs");
            entity.HasKey(e => e.UserId);
        });

        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("outbox_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Payload).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");
            entity.HasKey(e => e.EventId);
        });
    }
}
