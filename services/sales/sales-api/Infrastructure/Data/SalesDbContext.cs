namespace SalesApi.Infrastructure.Data;
using SalesApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// sales_db only (CLAUDE.md rule 1). The schema is owned by
/// <c>services/sales/db/migrations</c>; this model mirrors it and never generates migrations.
/// </summary>
public class SalesDbContext(DbContextOptions<SalesDbContext> options) : DbContext(options)
{
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<LossReason> LossReasons => Set<LossReason>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealContact> DealContacts => Set<DealContact>();
    public DbSet<DealStageHistory> DealStageHistories => Set<DealStageHistory>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomerRef> CustomerRefs => Set<CustomerRef>();
    public DbSet<UserRef> UserRefs => Set<UserRef>();
    public DbSet<ReassignmentQueue> ReassignmentQueue => Set<ReassignmentQueue>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Pipeline>(entity =>
        {
            entity.ToTable("pipelines");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique();
            entity.HasMany(e => e.Stages)
                  .WithOne(s => s.Pipeline)
                  .HasForeignKey(s => s.PipelineId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PipelineStage>(entity =>
        {
            entity.ToTable("pipeline_stages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.PipelineId, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.PipelineId, e.SortOrder }).IsUnique();
        });

        modelBuilder.Entity<LossReason>(entity =>
        {
            entity.ToTable("loss_reasons");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<CustomerRef>(entity =>
        {
            entity.ToTable("customer_refs");
            entity.HasKey(e => new { e.EntityType, e.Id });
            entity.Property(e => e.Email).HasColumnType("citext");
            entity.HasIndex(e => e.OrganizationId);
        });

        modelBuilder.Entity<Deal>(entity =>
        {
            entity.ToTable("deals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Tags).HasColumnType("text[]");
            entity.Property(e => e.CustomFields).HasColumnType("jsonb");
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Pipeline)
                  .WithMany()
                  .HasForeignKey(e => e.PipelineId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Stage)
                  .WithMany()
                  .HasForeignKey(e => e.StageId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.LossReason)
                  .WithMany()
                  .HasForeignKey(e => e.LossReasonId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.DealContacts)
                  .WithOne(dc => dc.Deal)
                  .HasForeignKey(dc => dc.DealId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.StageHistory)
                  .WithOne(h => h.Deal)
                  .HasForeignKey(h => h.DealId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.PipelineId, e.StageId });
            entity.HasIndex(e => new { e.OrganizationId, e.OwnerId, e.Status });
            entity.HasIndex(e => e.SourceLeadId).IsUnique().HasFilter("source_lead_id IS NOT NULL");
        });

        modelBuilder.Entity<DealContact>(entity =>
        {
            entity.ToTable("deal_contacts");
            entity.HasKey(e => new { e.DealId, e.ContactId });
            entity.HasIndex(e => e.ContactId);
        });

        modelBuilder.Entity<DealStageHistory>(entity =>
        {
            entity.ToTable("deal_stage_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.DealId, e.ChangedAt });
        });

        modelBuilder.Entity<CustomFieldDefinition>(entity =>
        {
            entity.ToTable("custom_field_definitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Options).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.OrganizationId, e.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<UserRef>(entity =>
        {
            entity.ToTable("user_refs");
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.OrganizationId);
        });

        modelBuilder.Entity<ReassignmentQueue>(entity =>
        {
            entity.ToTable("reassignment_queue");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasOne(e => e.Deal)
                  .WithMany()
                  .HasForeignKey(e => e.DealId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.DealId).IsUnique();
            entity.HasIndex(e => new { e.OrganizationId, e.DeactivatedUserId });
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
