namespace IdentityApi.Application.DTOs.Organizations;
public record UpdateOrganizationRequest(string? Name, string? DefaultCurrency, string? Timezone);
