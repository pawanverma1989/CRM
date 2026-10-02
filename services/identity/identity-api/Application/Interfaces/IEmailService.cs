namespace IdentityApi.Application.Interfaces;

public interface IEmailService
{
    Task SendInvitationAsync(string toEmail, string inviteToken, CancellationToken ct = default);
    Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default);
    Task SendEmailVerificationAsync(string toEmail, string verifyToken, CancellationToken ct = default);
}
