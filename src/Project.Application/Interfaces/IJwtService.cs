using System.Security.Claims;
using Project.Domain.Entities;

namespace Project.Application.Interfaces;

public interface IJwtService
{
    string GenerateToken(User user);
    ClaimsPrincipal? ValidateTokenWithoutLifetime(string token);
}
