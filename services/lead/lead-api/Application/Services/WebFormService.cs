namespace LeadApi.Application.Services;
using System.Text.Json;
using LeadApi.Application.DTOs;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;

public interface IWebFormService
{
    Task<List<WebFormDto>> ListAsync(CancellationToken ct);
    Task<WebFormDto> GetAsync(Guid id, CancellationToken ct);
    Task<WebFormDto> CreateAsync(CreateWebFormRequest request, CancellationToken ct);
    Task<WebFormDto> UpdateAsync(Guid id, UpdateWebFormRequest request, CancellationToken ct);
    Task<WebFormEmbedDto> GetEmbedAsync(Guid id, IHttpContextAccessor httpContextAccessor, CancellationToken ct);
}

public class WebFormService(
    ILookupRepository lookups,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock) : IWebFormService
{
    public async Task<List<WebFormDto>> ListAsync(CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage web forms.");

        var forms = await lookups.ListWebFormsAsync(ctx.OrganizationId, ct);
        return [.. forms.Select(ToDto)];
    }

    public async Task<WebFormDto> GetAsync(Guid id, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may access web forms.");

        var form = await lookups.GetWebFormAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No web form with id {id}.");

        return ToDto(form);
    }

    public async Task<WebFormDto> CreateAsync(CreateWebFormRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may create web forms.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new LeadValidationException("name", "Name is required.");

        var now = clock.GetUtcNow();
        var form = new WebForm
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            Name = request.Name.Trim(),
            PublicKey = GeneratePublicKey(),
            Fields = request.Fields?.GetRawText() ?? "[]",
            RequiredFields = request.RequiredFields?.GetRawText() ?? "[]",
            LeadSourceId = request.LeadSourceId,
            DefaultOwnerId = request.DefaultOwnerId,
            ConsentText = request.ConsentText?.Trim(),
            SuccessMessage = request.SuccessMessage?.Trim(),
            RedirectUrl = request.RedirectUrl?.Trim(),
            CaptchaEnabled = request.CaptchaEnabled,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        lookups.AddWebForm(form);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(form);
    }

    public async Task<WebFormDto> UpdateAsync(Guid id, UpdateWebFormRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may update web forms.");

        var form = await lookups.GetWebFormAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No web form with id {id}.");

        if (request.Name is not null) form.Name = request.Name.Trim();
        if (request.Fields.HasValue) form.Fields = request.Fields.Value.GetRawText();
        if (request.RequiredFields.HasValue) form.RequiredFields = request.RequiredFields.Value.GetRawText();
        if (request.LeadSourceId.HasValue) form.LeadSourceId = request.LeadSourceId.Value;
        if (request.DefaultOwnerId.HasValue) form.DefaultOwnerId = request.DefaultOwnerId.Value;
        if (request.ConsentText is not null) form.ConsentText = request.ConsentText.Trim();
        if (request.SuccessMessage is not null) form.SuccessMessage = request.SuccessMessage.Trim();
        if (request.RedirectUrl is not null) form.RedirectUrl = request.RedirectUrl.Trim();
        if (request.CaptchaEnabled.HasValue) form.CaptchaEnabled = request.CaptchaEnabled.Value;
        if (request.IsActive.HasValue) form.IsActive = request.IsActive.Value;
        form.UpdatedAt = clock.GetUtcNow();

        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(form);
    }

    public async Task<WebFormEmbedDto> GetEmbedAsync(Guid id, IHttpContextAccessor httpContextAccessor, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may access web form embed snippets.");

        var form = await lookups.GetWebFormAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No web form with id {id}.");

        var request = httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null
            ? $"{request.Scheme}://{request.Host}"
            : "https://your-crm-domain.com";

        var submitUrl = $"{baseUrl}/api/lead/v1/public/forms/{form.PublicKey}/submit";

        var snippet = $"""<script src="{baseUrl}/forms/embed.js" data-form="{form.PublicKey}" defer></script>""";

        return new WebFormEmbedDto(form.Id, form.PublicKey, submitUrl, snippet);
    }

    private static WebFormDto ToDto(WebForm f)
    {
        JsonElement ParseJson(string json)
        {
            try { return JsonSerializer.Deserialize<JsonElement>(json); }
            catch { return JsonSerializer.Deserialize<JsonElement>("[]"); }
        }

        return new WebFormDto(
            f.Id, f.Name, f.PublicKey,
            ParseJson(f.Fields), ParseJson(f.RequiredFields),
            f.LeadSourceId, f.DefaultOwnerId,
            f.ConsentText, f.SuccessMessage, f.RedirectUrl,
            f.CaptchaEnabled, f.IsActive, f.CreatedAt, f.UpdatedAt);
    }

    private static string GeneratePublicKey()
        => Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
}
