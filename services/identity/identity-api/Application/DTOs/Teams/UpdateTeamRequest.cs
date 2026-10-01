namespace IdentityApi.Application.DTOs.Teams;
public record UpdateTeamRequest(string? Name, Guid? ManagerId);
