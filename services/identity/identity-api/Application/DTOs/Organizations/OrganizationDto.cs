namespace IdentityApi.Application.DTOs.Organizations;
public record OrganizationDto(Guid Id, string Name, string DefaultCurrency, string Timezone, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
