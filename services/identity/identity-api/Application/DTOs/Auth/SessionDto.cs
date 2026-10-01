namespace IdentityApi.Application.DTOs.Auth;
public record SessionDto(Guid Id, string? IpAddress, string? UserAgent, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt);
