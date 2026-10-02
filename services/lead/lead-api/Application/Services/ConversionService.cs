namespace LeadApi.Application.Services;
using System.Text.Json;
using LeadApi.Application.DTOs;
using LeadApi.Application.Events;
using LeadApi.Application.Normalization;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using LeadApi.Infrastructure.Services;
using Microsoft.Extensions.Options;

/// <summary>
/// Lead conversion saga (CLAUDE.md rule 7, CNV-1..CNV-5).
/// Creates a company and a contact in the Customer service, and optionally a deal in Sales.
/// Each step is idempotent — the idempotency key is the lead_id sent as a request header.
/// State is persisted to <c>lead_conversions</c> so the <see cref="Workers.ConversionRetryWorker"/>
/// can resume from the last completed step.
/// </summary>
public interface IConversionService
{
    Task<ConversionDto> StartConversionAsync(Guid leadId, ConversionRequest request, CancellationToken ct);
    Task<ConversionDto> GetConversionAsync(Guid conversionId, CancellationToken ct);
    Task ResumeAsync(LeadConversion conversion, CancellationToken ct);
}

public class ConversionService(
    ILeadRepository leadRepo,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    ICustomerApiClient customerApi,
    ISalesApiClient salesApi,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<LeadSettings> settings,
    ILogger<ConversionService> logger) : IConversionService
{
    private readonly LeadSettings _settings = settings.Value;

    public async Task<ConversionDto> StartConversionAsync(Guid leadId, ConversionRequest request, CancellationToken ct)
    {
        var lead = await leadRepo.GetAsync(leadId, ctx, ct) ?? throw new NotFoundException($"No lead with id {leadId}.");

        if (lead.Status == "converted")
            throw new ConflictException($"Lead {leadId} is already converted.");

        if (lead.Status != "qualified" && lead.Status != "contacted" && lead.Status != "new")
            throw new LeadValidationException("status", "Only new/contacted/qualified leads can be converted.");

        var existing = await leadRepo.GetConversionByLeadAsync(leadId, ct);
        if (existing is not null && existing.Status is "started" or "company_done" or "contact_done" or "deal_done")
            return ToDto(existing); // Resume instead of duplicating.

        var now = clock.GetUtcNow();
        var conversion = new LeadConversion
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            LeadId = leadId,
            RequestedBy = ctx.ActorUserId,
            Request = JsonSerializer.Serialize(request),
            Status = "started",
            Attempts = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        leadRepo.AddConversion(conversion);
        await unitOfWork.SaveChangesAsync(ct);

        // Run the first saga step synchronously in the request.
        await ResumeAsync(conversion, ct);

        return ToDto(conversion);
    }

    public async Task<ConversionDto> GetConversionAsync(Guid conversionId, CancellationToken ct)
    {
        var conversion = await leadRepo.GetConversionAsync(conversionId, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No conversion with id {conversionId}.");

        return ToDto(conversion);
    }

    public async Task ResumeAsync(LeadConversion conversion, CancellationToken ct)
    {
        conversion.Attempts += 1;
        conversion.UpdatedAt = clock.GetUtcNow();

        try
        {
            ConversionRequest request;
            try { request = JsonSerializer.Deserialize<ConversionRequest>(conversion.Request) ?? new ConversionRequest(); }
            catch { request = new ConversionRequest(); }

            var lead = await leadRepo.GetAsync(conversion.LeadId, new InternalContext(conversion.OrganizationId), ct)
                ?? throw new NotFoundException($"Lead {conversion.LeadId} not found during conversion.");

            // Step 1: Create company
            if (conversion.Status == "started" && request.CreateCompany)
            {
                var companyPayload = new CreateCompanyPayload(
                    request.CompanyName ?? lead.CompanyName ?? "Unknown Company",
                    null,
                    lead.PhoneNormalized ?? lead.Phone,
                    lead.Id);

                var companyId = await customerApi.CreateCompanyAsync(companyPayload, lead.Id, conversion.RequestedBy, ct);
                if (companyId.HasValue)
                {
                    conversion.CompanyId = companyId;
                    conversion.Status = "company_done";
                    conversion.UpdatedAt = clock.GetUtcNow();
                    await unitOfWork.SaveChangesAsync(ct);
                }
            }
            else if (conversion.Status == "started")
            {
                conversion.Status = "company_done";
            }

            // Step 2: Create contact
            if (conversion.Status == "company_done")
            {
                var contactPayload = new CreateContactPayload(
                    lead.FirstName, lead.LastName, lead.Email,
                    lead.Phone, lead.JobTitle,
                    conversion.CompanyId, lead.Id);

                var contactId = await customerApi.CreateContactAsync(contactPayload, lead.Id, conversion.RequestedBy, ct);
                if (contactId.HasValue)
                {
                    conversion.ContactId = contactId;
                    conversion.Status = "contact_done";
                    conversion.UpdatedAt = clock.GetUtcNow();
                    await unitOfWork.SaveChangesAsync(ct);
                }
                else
                {
                    throw new InvalidOperationException("Customer service did not return a contact ID.");
                }
            }

            // Step 3: Create deal (optional)
            if (conversion.Status == "contact_done" && request.CreateDeal)
            {
                var dealPayload = new CreateDealPayload(
                    request.DealName ?? $"{lead.FirstName} {lead.LastName}".Trim(),
                    conversion.CompanyId, conversion.ContactId,
                    lead.Id, request.Currency ?? "INR");

                var dealId = await salesApi.CreateDealAsync(dealPayload, lead.Id, conversion.RequestedBy, ct);
                conversion.DealId = dealId;
                conversion.Status = "deal_done";
                conversion.UpdatedAt = clock.GetUtcNow();
                await unitOfWork.SaveChangesAsync(ct);
            }
            else if (conversion.Status == "contact_done")
            {
                conversion.Status = "deal_done";
            }

            // Mark lead as converted
            if (conversion.Status == "deal_done")
            {
                var now = clock.GetUtcNow();
                lead.Status = "converted";
                lead.ConvertedAt = now;
                lead.ConvertedBy = conversion.RequestedBy;
                lead.ConvertedContactId = conversion.ContactId;
                lead.ConvertedCompanyId = conversion.CompanyId;
                lead.ConvertedDealId = conversion.DealId;
                lead.Version += 1;
                lead.UpdatedAt = now;

                conversion.Status = "completed";
                conversion.UpdatedAt = now;

                outbox.Add(EventTypes.LeadConverted, AggregateTypes.Lead, lead.Id,
                    lead.OrganizationId, lead.Version, conversion.RequestedBy,
                    new ConvertedPayload(lead.Id, conversion.ContactId, conversion.CompanyId, conversion.DealId, lead.Version));

                await unitOfWork.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Lead {LeadId} converted: contact={ContactId}, company={CompanyId}, deal={DealId}.",
                    lead.Id, conversion.ContactId, conversion.CompanyId, conversion.DealId);
            }
        }
        catch (Exception ex)
        {
            conversion.LastError = ex.Message;
            conversion.UpdatedAt = clock.GetUtcNow();

            if (conversion.Attempts >= _settings.MaxConversionRetries)
            {
                conversion.Status = "failed";
                logger.LogError(ex, "Conversion {ConversionId} for lead {LeadId} failed after {Attempts} attempts.",
                    conversion.Id, conversion.LeadId, conversion.Attempts);
            }
            else
            {
                // Back-off: retry after 2^attempts minutes
                conversion.NextRetryAt = clock.GetUtcNow().AddMinutes(Math.Pow(2, conversion.Attempts));
                logger.LogWarning(ex, "Conversion {ConversionId} failed on attempt {Attempt}; next retry at {NextRetry}.",
                    conversion.Id, conversion.Attempts, conversion.NextRetryAt);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static ConversionDto ToDto(LeadConversion c)
        => new(c.Id, c.LeadId, c.Status, c.CompanyId, c.ContactId, c.DealId, c.LastError, c.Attempts, c.CreatedAt, c.UpdatedAt);

    /// <summary>Minimal context for internal / background operations that bypass the HTTP context.</summary>
    private sealed class InternalContext(Guid organizationId) : IRequestContext
    {
        public Guid OrganizationId { get; } = organizationId;
        public Guid ActorUserId { get; } = Guid.Empty;
        public string Role { get; } = "admin";
        public Guid[]? VisibleOwnerIds { get; } = null;
        public bool IsAdmin => true;
        public bool IsManagerOrAbove => true;
        public bool IsService => true;
        public string? ServiceName => "lead-conversion";
    }
}
