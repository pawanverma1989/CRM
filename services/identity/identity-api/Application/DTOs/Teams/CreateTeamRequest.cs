namespace IdentityApi.Application.DTOs.Teams;
public record CreateTeamRequest(string Name, Guid? ManagerId);
