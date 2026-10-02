namespace IdentityApi.Application.Validators;
using FluentValidation;
using IdentityApi.Application.DTOs.Users;

public class InviteUserRequestValidator : AbstractValidator<InviteUserRequest>
{
    private static readonly string[] ValidRoles = ["admin", "manager", "sales_rep"];

    public InviteUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Role).NotEmpty().Must(r => ValidRoles.Contains(r))
            .WithMessage("Role must be one of: admin, manager, sales_rep.");
    }
}
