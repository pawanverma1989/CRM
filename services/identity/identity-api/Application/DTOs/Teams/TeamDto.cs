namespace IdentityApi.Application.DTOs.Teams;
public record TeamDto(Guid Id, Guid OrganizationId, string Name, Guid? ManagerId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
