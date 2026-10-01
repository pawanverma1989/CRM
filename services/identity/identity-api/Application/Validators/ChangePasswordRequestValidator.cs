namespace IdentityApi.Application.Validators;
using FluentValidation;
using IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.Helpers;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .Must(p => !CommonPasswords.Contains(p)).WithMessage("Password is too common. Please choose a stronger password.");
    }
}
