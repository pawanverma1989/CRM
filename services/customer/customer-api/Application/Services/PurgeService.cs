namespace CustomerApi.Application.Services;
using CustomerApi.Application.Events;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// DEL-3 / AC-15: the nightly job that permanently removes records soft-deleted longer ago than
/// the retention window and publishes <c>*.purged</c> for each. It also keeps the outbox and the
/// idempotency ledger from growing without bound (architecture §4.1: published rows go after 7 days).
/// The recycle-bin rule itself is enforced by the query filters, so running late only costs disk.
/// </summary>
public interface IPurgeService
{
    Task<PurgeOutcome> RunAsync(CancellationToken ct);
}

public sealed record PurgeOutcome(int Companies, int Contacts, int OutboxRowsRemoved, int ProcessedRowsRemoved);

public class PurgeService(
    CustomerDbContext context,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<CustomerSettings> settings,
    ILogger<PurgeService> logger) : IPurgeService
{
    private const int PublishedOutboxRetentionDays = 7;
    private const int ProcessedEventRetentionDays = 30;

    private readonly CustomerSettings _settings = settings.Value;

    public async Task<PurgeOutcome> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now.AddDays(-_settings.RecycleBinRetentionDays);

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        var contacts = await context.Contacts
            .Where(c => c.DeletedAt != null && c.DeletedAt < cutoff)
            .Select(c => new { c.Id, c.OrganizationId, c.Version })
            .ToListAsync(ct);

        var companies = await context.Companies
            .Where(c => c.DeletedAt != null && c.DeletedAt < cutoff)
            .Select(c => new { c.Id, c.OrganizationId, c.Version })
            .ToListAsync(ct);

        foreach (var contact in contacts)
            outbox.Add(EventTypes.ContactPurged, AggregateTypes.Contact, contact.Id,
                contact.OrganizationId, contact.Version, null,
                new PurgedEventPayload(contact.Id, "retention"));

        foreach (var company in companies)
            outbox.Add(EventTypes.CompanyPurged, AggregateTypes.Company, company.Id,
                company.OrganizationId, company.Version, null,
                new PurgedEventPayload(company.Id, "retention"));

        await unitOfWork.SaveChangesAsync(ct);

        if (contacts.Count > 0)
        {
            var ids = contacts.Select(c => c.Id).ToList();

            // merged_into_id is a real foreign key inside this database; a merge loser that is not
            // being purged must stop pointing at a survivor that is.
            await unitOfWork.ExecuteSqlAsync(
                $"UPDATE contacts SET merged_into_id = NULL WHERE merged_into_id = ANY({ids})", ct);

            await unitOfWork.ExecuteSqlAsync($"DELETE FROM contacts WHERE id = ANY({ids})", ct);
        }

        if (companies.Count > 0)
        {
            var ids = companies.Select(c => c.Id).ToList();

            await unitOfWork.ExecuteSqlAsync(
                $"UPDATE companies SET merged_into_id = NULL WHERE merged_into_id = ANY({ids})", ct);

            // contacts.company_id is ON DELETE SET NULL; those contacts were already unlinked when
            // the company was soft-deleted (COM-5), so no further event is due.
            await unitOfWork.ExecuteSqlAsync($"DELETE FROM companies WHERE id = ANY({ids})", ct);
        }

        var outboxCutoff = now.AddDays(-PublishedOutboxRetentionDays);
        var outboxRemoved = await unitOfWork.ExecuteSqlAsync(
            $"DELETE FROM outbox_events WHERE published_at IS NOT NULL AND published_at < {outboxCutoff}", ct);

        var processedCutoff = now.AddDays(-ProcessedEventRetentionDays);
        var processedRemoved = await unitOfWork.ExecuteSqlAsync(
            $"DELETE FROM processed_events WHERE processed_at < {processedCutoff}", ct);

        await transaction.CommitAsync(ct);

        if (contacts.Count > 0 || companies.Count > 0)
            logger.LogInformation(
                "Purge removed {Contacts} contact(s) and {Companies} company/companies deleted before {Cutoff:o}.",
                contacts.Count, companies.Count, cutoff);

        return new PurgeOutcome(companies.Count, contacts.Count, outboxRemoved, processedRemoved);
    }
}
