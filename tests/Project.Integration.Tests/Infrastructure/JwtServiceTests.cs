using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Project.Application.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;
using Project.Domain.ValueObjects;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests.Infrastructure;

[Collection("Integration")]
public class JwtServiceTests(IntegrationTestFixture fixture)
{
    [Fact]
    public void GenerateToken_ValidUser_ReturnsValidJwt()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var user = User.Create("jwtuser", Email.Create("jwt@test.com"), "hash", UserRole.User);

        var token = jwtService.GenerateToken(user);

        token.Should().NotBeNullOrEmpty();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Should().NotBeNull();
        jwt.Issuer.Should().Be("Project.Tests");
        jwt.Audiences.Should().Contain("Project.Tests");
    }

    [Fact]
    public void GenerateToken_ContainsExpectedClaims()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var user = User.Create("claimsuser", Email.Create("claims@test.com"), "hash", UserRole.Admin);

        var token = jwtService.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == user.Id);
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "claimsuser");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == "claims@test.com");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }

    [Fact]
    public void GenerateToken_HasFutureExpiration()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var user = User.Create("expiryuser", Email.Create("expiry@test.com"), "hash", UserRole.User);

        var token = jwtService.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.ValidTo.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void ValidateTokenWithoutLifetime_ValidToken_ReturnsPrincipal()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var user = User.Create("validateuser", Email.Create("validate@test.com"), "hash", UserRole.User);
        var token = jwtService.GenerateToken(user);

        var principal = jwtService.ValidateTokenWithoutLifetime(token);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(user.Id);
    }

    [Fact]
    public void ValidateTokenWithoutLifetime_InvalidToken_ReturnsNull()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var principal = jwtService.ValidateTokenWithoutLifetime("completely.invalid.token");

        principal.Should().BeNull();
    }
}
