namespace IdentityApi.Infrastructure.Services;
using IdentityApi.Application.Interfaces;

public class PasswordService : IPasswordService
{
    public string Hash(string plaintext) => BCrypt.Net.BCrypt.HashPassword(plaintext, workFactor: 12);
    public bool Verify(string plaintext, string hash) => BCrypt.Net.BCrypt.Verify(plaintext, hash);
}
