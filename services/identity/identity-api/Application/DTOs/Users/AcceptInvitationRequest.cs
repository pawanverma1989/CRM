namespace IdentityApi.Application.DTOs.Users;
public record AcceptInvitationRequest(string Token, string FirstName, string LastName, string Password);
