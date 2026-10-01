namespace IdentityApi.Settings;

public class AuthSettings
{
    public int MaxFailedLoginAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
}
