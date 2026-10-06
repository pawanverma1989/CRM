namespace IdentityApi.Tests.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Validators;

public class CreateUserRequestValidatorTests
{
    private readonly CreateUserRequestValidator _validator = new();

    private static CreateUserRequest Valid() =>
        new("new@example.com", "Corr3ct-Horse-Battery", "Bob", null, null, "sales_rep", null);

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
        => _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("admin")]
    [InlineData("manager")]
    [InlineData("sales_rep")]
    public void Validate_EachValidRole_HasNoErrors(string role)
        => _validator.TestValidate(Valid() with { Role = role }).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("superuser")]
    [InlineData("Admin")]
    public void Validate_InvalidRole_HasRoleError(string role)
        => _validator.TestValidate(Valid() with { Role = role }).ShouldHaveValidationErrorFor(x => x.Role);

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_InvalidEmail_HasEmailError(string email)
        => _validator.TestValidate(Valid() with { Email = email }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Validate_MissingFirstName_HasFirstNameError()
        => _validator.TestValidate(Valid() with { FirstName = "" }).ShouldHaveValidationErrorFor(x => x.FirstName);

    [Fact]
    public void Validate_FirstNameTooLong_HasFirstNameError()
        => _validator.TestValidate(Valid() with { FirstName = new string('a', 101) }).ShouldHaveValidationErrorFor(x => x.FirstName);

    [Fact]
    public void Validate_MissingPassword_HasPasswordError()
        => _validator.TestValidate(Valid() with { Password = "" }).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Validate_ShortPassword_HasSpecificMessageWithoutEchoingPassword()
    {
        const string pwd = "Sh0rt!pw9"; // 9 chars
        var result = _validator.TestValidate(Valid() with { Password = pwd });

        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must be at least 10 characters.");
        result.Errors.Should().NotContain(e => e.ErrorMessage.Contains(pwd));
    }

    [Fact]
    public void Validate_CommonPassword_HasSpecificMessageWithoutEchoingPassword()
    {
        const string pwd = "password1234";
        var result = _validator.TestValidate(Valid() with { Password = pwd });

        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password is too common. Please choose a stronger password.");
        result.Errors.Should().NotContain(e => e.ErrorMessage.Contains(pwd));
    }
}
