namespace IdentityApi.Application.Interfaces;
using IdentityApi.Application.DTOs.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(Guid organizationId, string? role, Guid? teamId, string? status, string? search, CancellationToken ct = default);
    Task<UserDto> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<UserDto> GetMeAsync(Guid userId, Guid organizationId, CancellationToken ct = default);
    Task<UserDto> UpdateMeAsync(Guid userId, UpdateMeRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task<UserDto> InviteAsync(InviteUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task ResendInvitationAsync(Guid invitationId, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task CancelInvitationAsync(Guid invitationId, Guid organizationId, CancellationToken ct = default);
    Task AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid id, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task ReactivateAsync(Guid id, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task AdminLogoutAllAsync(Guid targetUserId, Guid organizationId, CancellationToken ct = default);
    Task InitiateEmailChangeAsync(Guid userId, string newEmail, Guid organizationId, CancellationToken ct = default);
    Task ConfirmEmailChangeAsync(string token, CancellationToken ct = default);

    /// <summary>Queues a user.updated (resync=true) outbox row for every user of the organization; returns the count.</summary>
    Task<int> ResyncUserEventsAsync(Guid organizationId, Guid actorId, CancellationToken ct = default);
}
