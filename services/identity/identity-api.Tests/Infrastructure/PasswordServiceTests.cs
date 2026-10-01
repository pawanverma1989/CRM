namespace IdentityApi.Tests.Infrastructure;
using FluentAssertions;
using IdentityApi.Infrastructure.Services;

public class PasswordServiceTests
{
    private readonly PasswordService _svc = new();

    [Fact]
    public void Hash_ReturnsNonNullBcryptHash()
    {
        var hash = _svc.Hash("my_password");

        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().StartWith("$2");
    }

    [Fact]
    public void Hash_SamePasswordProducesDifferentHashes()
    {
        // BCrypt includes a random salt
        var hash1 = _svc.Hash("same_password");
        var hash2 = _svc.Hash("same_password");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Hash_DifferentPasswordsProduceDifferentHashes()
    {
        var hash1 = _svc.Hash("password1");
        var hash2 = _svc.Hash("password2");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _svc.Hash("correct_password");

        _svc.Verify("correct_password", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _svc.Hash("correct_password");

        _svc.Verify("wrong_password", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyPassword_ReturnsFalse()
    {
        var hash = _svc.Hash("correct_password");

        _svc.Verify("", hash).Should().BeFalse();
    }
}
