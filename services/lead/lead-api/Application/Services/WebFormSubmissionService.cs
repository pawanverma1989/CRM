namespace LeadApi.Application.Services;
using System.Text.Json;
using LeadApi.Application.DTOs;
using LeadApi.Application.Events;
using LeadApi.Application.Normalization;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Handles public web form submissions (WEB-1..WEB-4).
/// Rate-limits by IP, deduplicates by email within the configured window,
/// and creates or updates a lead.
/// </summary>
public interface IWebFormSubmissionService
{
    Task<PublicFormSubmitResponse> SubmitAsync(
        string publicKey,
        PublicFormSubmitRequest request,
        string? ipAddress,
        CancellationToken ct);
}

public class WebFormSubmissionService(
    ILookupRepository lookups,
    ILeadRepository leads,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    LeadDbContext context,
    TimeProvider clock,
    IOptions<LeadSettings> settings,
    ILogger<WebFormSubmissionService> logger) : IWebFormSubmissionService
{
    private readonly LeadSettings _settings = settings.Value;

    public async Task<PublicFormSubmitResponse> SubmitAsync(
        string publicKey,
        PublicFormSubmitRequest request,
        string? ipAddress,
        CancellationToken ct)
    {
        // CAP-1 / AC-2: must have email OR phone.
        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Phone))
            throw new LeadValidationException("A submission must include at least an email or a phone number.");

        var form = await lookups.GetWebFormByPublicKeyAsync(publicKey, ct)
            ?? throw new NotFoundException($"No active web form with key '{publicKey}'.");

        // WEB-4: Rate limit by IP.
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var windowStart = clock.GetUtcNow().AddHours(-1);
            var recentCount = await context.WebFormSubmissions
                .CountAsync(s => s.WebFormId == form.Id
                              && s.IpAddress == ipAddress
                              && s.SubmittedAt >= windowStart, ct);

            if (recentCount >= _settings.WebFormSubmissionsPerIpPerHour)
                throw new LeadValidationException("Too many form submissions from this IP address. Please try again later.");
        }

        var now = clock.GetUtcNow();
        var email = request.Email?.Trim().ToLowerInvariant();
        var phoneNormalized = PhoneNormalizer.Normalize(request.Phone, _settings.DefaultPhoneCountryCode);

        // Duplicate check within the configured window — if a matching lead exists, update it.
        Lead? existingLead = null;

        if (!string.IsNullOrWhiteSpace(email))
        {
            existingLead = await leads.FindByEmailAsync(form.OrganizationId, email, null, ct);
        }

        if (existingLead is null && phoneNormalized is not null)
        {
            existingLead = await leads.FindByPhoneAsync(form.OrganizationId, phoneNormalized, null, ct);
        }

        Lead lead;

        if (existingLead is not null)
        {
            // Update the existing lead with any new data.
            lead = existingLead;
            if (!string.IsNullOrWhiteSpace(request.FirstName)) lead.FirstName = request.FirstName.Trim();
            if (!string.IsNullOrWhiteSpace(request.LastName)) lead.LastName = request.LastName.Trim();
            if (!string.IsNullOrWhiteSpace(request.CompanyName)) lead.CompanyName = request.CompanyName.Trim();
            if (!string.IsNullOrWhiteSpace(request.JobTitle)) lead.JobTitle = request.JobTitle.Trim();
            if (!string.IsNullOrWhiteSpace(request.Notes)) lead.Notes = request.Notes.Trim();
            if (!string.IsNullOrWhiteSpace(request.UtmSource)) lead.UtmSource = request.UtmSource;
            if (!string.IsNullOrWhiteSpace(request.UtmMedium)) lead.UtmMedium = request.UtmMedium;
            if (!string.IsNullOrWhiteSpace(request.UtmCampaign)) lead.UtmCampaign = request.UtmCampaign;
            lead.WebFormId = form.Id;
            lead.Version += 1;
            lead.UpdatedAt = now;
        }
        else
        {
            // Create a new lead.
            lead = new Lead
            {
                Id = Guid.NewGuid(),
                OrganizationId = form.OrganizationId,
                OwnerId = form.DefaultOwnerId,
                FirstName = request.FirstName?.Trim(),
                LastName = request.LastName?.Trim(),
                Email = email,
                Phone = request.Phone?.Trim(),
                PhoneNormalized = phoneNormalized,
                CompanyName = request.CompanyName?.Trim(),
                JobTitle = request.JobTitle?.Trim(),
                Notes = request.Notes?.Trim(),
                LeadSourceId = form.LeadSourceId,
                WebFormId = form.Id,
                UtmSource = request.UtmSource,
                UtmMedium = request.UtmMedium,
                UtmCampaign = request.UtmCampaign,
                Status = "new",
                Tags = [],
                CustomFields = "{}",
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            };

            leads.Add(lead);
        }

        // Record the submission.
        var submission = new WebFormSubmission
        {
            Id = Guid.NewGuid(),
            WebFormId = form.Id,
            LeadId = lead.Id,
            IpAddress = ipAddress,
            SubmittedAt = now
        };

        context.WebFormSubmissions.Add(submission);

        // Publish web_form.submitted event if consent was given.
        if (request.ConsentGiven && !string.IsNullOrWhiteSpace(form.ConsentText))
        {
            outbox.Add(EventTypes.WebFormSubmitted, AggregateTypes.WebForm, form.Id,
                form.OrganizationId, 1, null,
                new WebFormSubmittedPayload(form.Id, lead.Id, form.ConsentText, ipAddress));
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Web form {FormKey} submission for lead {LeadId}.", publicKey, lead.Id);

        return new PublicFormSubmitResponse(lead.Id, form.SuccessMessage, form.RedirectUrl);
    }
}
