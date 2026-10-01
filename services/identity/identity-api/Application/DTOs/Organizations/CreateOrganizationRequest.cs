namespace IdentityApi.Application.DTOs.Organizations;
public record CreateOrganizationRequest(string Name, string DefaultCurrency, string Timezone, string AdminEmail, string AdminPassword, string AdminFirstName, string? AdminLastName);
