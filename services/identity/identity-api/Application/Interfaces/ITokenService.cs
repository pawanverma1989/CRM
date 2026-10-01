namespace IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

public interface ITokenService
{
    string GenerateAccessToken(User user, Guid[]? visibleOwnerIds);
    string GenerateRefreshToken();
    JsonWebKeySet GetPublicKeySet();
}
