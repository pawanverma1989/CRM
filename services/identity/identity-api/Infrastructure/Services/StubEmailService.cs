namespace IdentityApi.Infrastructure.Services;
using IdentityApi.Application.Interfaces;
using Microsoft.Extensions.Logging;

public class StubEmailService(ILogger<StubEmailService> logger) : IEmailService
{
    public Task SendInvitationAsync(string toEmail, string inviteToken, CancellationToken ct = default)
    {
        logger.LogInformation("STUB EMAIL — Invitation to {UserId}: token={Token}", toEmail, inviteToken);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        logger.LogInformation("STUB EMAIL — PasswordReset to {UserId}: token={Token}", toEmail, resetToken);
        return Task.CompletedTask;
    }

    public Task SendEmailVerificationAsync(string toEmail, string verifyToken, CancellationToken ct = default)
    {
        logger.LogInformation("STUB EMAIL — EmailVerification to {UserId}: token={Token}", toEmail, verifyToken);
        return Task.CompletedTask;
    }
}
