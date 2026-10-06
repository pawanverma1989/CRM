namespace IdentityApi.Infrastructure.Data;
using IdentityApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<ServiceClient> ServiceClients => Set<ServiceClient>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<UserVisibility> UserVisibilities => Set<UserVisibility>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DefaultCurrency).HasMaxLength(3);
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(e => e.Id);
            entity.HasOne(t => t.Organization)
                  .WithMany(o => o.Teams)
                  .HasForeignKey(t => t.OrganizationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(t => t.Manager)
                  .WithMany(u => u.ManagedTeams)
                  .HasForeignKey(t => t.ManagerId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(t => new { t.OrganizationId, t.Name }).IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", t => t.HasCheckConstraint("chk_users_role", "role IN ('admin','manager','sales_rep')"));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasColumnType("citext");
            entity.Property(e => e.PendingEmail).HasColumnType("citext");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.EmailVerifiedAt).HasColumnName("email_verified_at");
            entity.Property(e => e.PendingEmail).HasColumnName("pending_email");
            // Owned by the DB (default 1, trg_users_version bumps it): EF never writes it and reads it
            // back after INSERT/UPDATE via RETURNING.
            entity.Property(e => e.Version).ValueGeneratedOnAddOrUpdate();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.TeamId);
            entity.HasOne(u => u.Organization)
                  .WithMany(o => o.Users)
                  .HasForeignKey(u => u.OrganizationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(u => u.Team)
                  .WithMany(t => t.Members)
                  .HasForeignKey(u => u.TeamId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserToken>(entity =>
        {
            entity.ToTable("user_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Purpose).HasColumnName("purpose");
            entity.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasOne(p => p.User)
                  .WithMany(u => u.UserTokens)
                  .HasForeignKey(p => p.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.IpAddress)
                  .HasColumnType("inet")
                  .HasConversion(
                      v => v == null ? (System.Net.IPAddress?)null : System.Net.IPAddress.Parse(v),
                      v => v == null ? null : v.ToString());
            entity.Property(e => e.ReplacedById).HasColumnName("replaced_by_id");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.HasOne(s => s.User)
                  .WithMany(u => u.Sessions)
                  .HasForeignKey(s => s.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(s => s.ReplacedBy)
                  .WithMany()
                  .HasForeignKey(s => s.ReplacedById)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ServiceClient>(entity =>
        {
            entity.ToTable("service_clients");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientId).IsUnique();
            entity.HasOne(sc => sc.Organization)
                  .WithMany()
                  .HasForeignKey(sc => sc.OrganizationId)
                  .OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<UserVisibility>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("v_user_visibility");
        });
    }
}
