namespace LeadApi.Infrastructure.Data;
using LeadApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// lead_db only (CLAUDE.md rule 1). The schema is owned by
/// <c>services/lead/db/migrations</c>; this model mirrors it and never generates migrations.
/// </summary>
public class LeadDbContext(DbContextOptions<LeadDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<DisqualifyReason> DisqualifyReasons => Set<DisqualifyReason>();
    public DbSet<WebForm> WebForms => Set<WebForm>();
    public DbSet<WebFormSubmission> WebFormSubmissions => Set<WebFormSubmission>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<LeadConversion> LeadConversions => Set<LeadConversion>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<UserRef> UserRefs => Set<UserRef>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("leads");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Email).HasColumnType("citext");
            entity.Property(e => e.Tags).HasColumnType("text[]");
            entity.Property(e => e.CustomFields).HasColumnType("jsonb");
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.LeadSource)
                  .WithMany()
                  .HasForeignKey(e => e.LeadSourceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.WebForm)
                  .WithMany()
                  .HasForeignKey(e => e.WebFormId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.DisqualifyReasonNav)
                  .WithMany()
                  .HasForeignKey(e => e.DisqualifyReasonId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.OrganizationId, e.OwnerId });
            entity.HasIndex(e => new { e.OrganizationId, e.Email });
        });

        modelBuilder.Entity<LeadSource>(entity =>
        {
            entity.ToTable("lead_sources");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<DisqualifyReason>(entity =>
        {
            entity.ToTable("disqualify_reasons");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<WebForm>(entity =>
        {
            entity.ToTable("web_forms");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Fields).HasColumnType("jsonb");
            entity.Property(e => e.RequiredFields).HasColumnType("jsonb");
            entity.HasIndex(e => e.PublicKey).IsUnique();
            entity.HasIndex(e => new { e.OrganizationId });
        });

        modelBuilder.Entity<WebFormSubmission>(entity =>
        {
            entity.ToTable("web_form_submissions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.IpAddress)
                  .HasColumnType("inet")
                  .HasConversion(
                      v => v == null ? null : System.Net.IPAddress.Parse(v),
                      v => v == null ? null : v.ToString());
            entity.HasOne(e => e.WebForm)
                  .WithMany()
                  .HasForeignKey(e => e.WebFormId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.LeadId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.WebFormId, e.SubmittedAt });
            entity.HasIndex(e => new { e.IpAddress, e.SubmittedAt });
        });

        modelBuilder.Entity<CustomFieldDefinition>(entity =>
        {
            entity.ToTable("custom_field_definitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Options).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.OrganizationId, e.EntityType, e.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<LeadConversion>(entity =>
        {
            entity.ToTable("lead_conversions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Request).HasColumnType("jsonb");
            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.LeadId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.LeadId).IsUnique();
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

        modelBuilder.Entity<UserRef>(entity =>
        {
            entity.ToTable("user_refs");
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.OrganizationId);
        });
    }
}
