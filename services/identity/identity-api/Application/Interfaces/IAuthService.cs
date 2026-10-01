namespace IdentityApi.Application.Interfaces;
using IdentityApi.Application.DTOs.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task<LoginResponse> RefreshAsync(RefreshRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct = default);
}
