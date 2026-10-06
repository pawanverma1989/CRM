namespace IdentityApi.Application.Validators;
using FluentValidation;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Helpers;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    private static readonly string[] ValidRoles = ["admin", "manager", "sales_rep"];

    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty().Must(r => ValidRoles.Contains(r))
            .WithMessage("Role must be one of: admin, manager, sales_rep.");
        // Messages are fixed strings: never use {PropertyValue} here, so the password is never echoed back.
        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .Must(p => !CommonPasswords.Contains(p)).WithMessage("Password is too common. Please choose a stronger password.");
    }
}
