namespace CustomerApi.Application.Services;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Mapping;
using CustomerApi.Application.Normalization;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.Extensions.Options;

/// <summary>
/// <c>POST /duplicates/check</c>: what the client should warn about before a save.
/// A hard duplicate (DUP-1) is reported with <c>blocking: true</c> because the save will be
/// refused; the soft warnings (DUP-2, DUP-3) never block anything (AC-5).
/// </summary>
public interface IDuplicateService
{
    Task<DuplicateCheckResponse> CheckAsync(DuplicateCheckRequest request, CancellationToken ct);
}

public class DuplicateService(
    ICompanyRepository companies,
    IContactRepository contacts,
    IRequestContext ctx,
    IOptions<CustomerSettings> settings) : IDuplicateService
{
    private readonly CustomerSettings _settings = settings.Value;

    public async Task<DuplicateCheckResponse> CheckAsync(DuplicateCheckRequest request, CancellationToken ct)
    {
        var entityType = EntityTypes.Require(request.EntityType);

        var matches = entityType == EntityTypes.Company
            ? await CheckCompanyAsync(request, ct)
            : await CheckContactAsync(request, ct);

        return new DuplicateCheckResponse(matches.Any(m => m.Blocking), matches);
    }

    private async Task<List<DuplicateMatchDto>> CheckCompanyAsync(
        DuplicateCheckRequest request, CancellationToken ct)
    {
        var matches = new List<DuplicateMatchDto>();

        // DUP-1 / AC-4: domains are normalised first, so "https://www.Acme.com/" and "acme.com"
        // are recognised as the same company.
        var domain = DomainNormalizer.Normalize(request.Domain);
        if (domain is not null)
        {
            var clash = await companies.FindLiveByDomainAsync(ctx.OrganizationId, domain, request.ExcludeId, ct);
            if (clash is not null)
                matches.Add(new DuplicateMatchDto(
                    clash.Id, clash.Name, "same_domain", true, 1.0, null, clash.Phone, clash.Domain, null));
        }

        // DUP-3: a very similar name is only a warning.
        var name = request.Name?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var similar = await companies.FindSimilarByNameAsync(
                ctx.OrganizationId, name, request.ExcludeId, _settings.SimilarityThreshold,
                _settings.MaxDuplicateWarnings, ct);

            foreach (var candidate in similar.Where(c => matches.All(m => m.Id != c.Id)))
                matches.Add(new DuplicateMatchDto(
                    candidate.Id, candidate.Name, "similar_name", false,
                    Similarity(name, candidate.Name), null, candidate.Phone, candidate.Domain, null));
        }

        return matches;
    }

    private async Task<List<DuplicateMatchDto>> CheckContactAsync(
        DuplicateCheckRequest request, CancellationToken ct)
    {
        var matches = new List<DuplicateMatchDto>();

        // DUP-1 / AC-3: emails are compared case-insensitively (citext, CON-3).
        var email = Validation.EmailValidator.Clean(request.Email);
        if (email is not null)
        {
            var clash = await contacts.FindLiveByEmailAsync(ctx.OrganizationId, email, request.ExcludeId, ct);
            if (clash is not null)
                matches.Add(new DuplicateMatchDto(
                    clash.Id, CustomerMapper.DisplayName(clash), "same_email", true, 1.0,
                    clash.Email, clash.Phone, null, clash.CompanyId));
        }

        // DUP-2 / AC-5: the same number in E.164 form is a warning; saving is still allowed.
        var phone = PhoneNormalizer.Normalize(request.Phone, _settings.DefaultPhoneCountryCode);
        if (phone is not null)
        {
            var samePhone = await contacts.FindByNormalizedPhoneAsync(
                ctx.OrganizationId, phone, request.ExcludeId, _settings.MaxDuplicateWarnings, ct);

            foreach (var candidate in samePhone.Where(c => matches.All(m => m.Id != c.Id)))
                matches.Add(new DuplicateMatchDto(
                    candidate.Id, CustomerMapper.DisplayName(candidate), "same_phone", false, 0.9,
                    candidate.Email, candidate.Phone, null, candidate.CompanyId));
        }

        // DUP-2: a very similar name at the same company.
        var fullName = $"{request.FirstName?.Trim()} {request.LastName?.Trim()}".Trim();
        if (!string.IsNullOrEmpty(fullName))
        {
            var similar = await contacts.FindSimilarByNameAsync(
                ctx.OrganizationId, fullName, request.CompanyId, request.ExcludeId,
                _settings.SimilarityThreshold, _settings.MaxDuplicateWarnings, ct);

            foreach (var candidate in similar.Where(c => matches.All(m => m.Id != c.Id)))
                matches.Add(new DuplicateMatchDto(
                    candidate.Id, CustomerMapper.DisplayName(candidate), "similar_name", false,
                    Similarity(fullName, CustomerMapper.DisplayName(candidate)),
                    candidate.Email, candidate.Phone, null, candidate.CompanyId));
        }

        return matches;
    }

    /// <summary>
    /// Trigram similarity recomputed in memory purely to report a score; the matching itself was
    /// done by PostgreSQL's <c>similarity()</c> against the GIN indexes.
    /// </summary>
    private static double Similarity(string left, string right)
    {
        var a = Trigrams(left);
        var b = Trigrams(right);
        if (a.Count == 0 || b.Count == 0) return 0;

        var shared = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0 : Math.Round((double)shared / union, 3);
    }

    private static HashSet<string> Trigrams(string value)
    {
        var padded = "  " + value.Trim().ToLowerInvariant() + " ";
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 3 <= padded.Length; i++) set.Add(padded.Substring(i, 3));
        return set;
    }
}
