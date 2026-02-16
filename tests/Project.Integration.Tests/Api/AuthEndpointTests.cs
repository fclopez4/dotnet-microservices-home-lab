using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Project.Application.DTOs;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests.Api;

[Collection("Integration")]
public class AuthEndpointTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Register_ValidUser_ReturnsCreated()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"newuser_{suffix}", $"newuser_{suffix}@test.com", "Password123!"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<RegisterUserResponse>();
        result.Should().NotBeNull();
        result!.Username.Should().Be($"newuser_{suffix}");
        result.Email.Should().Be($"newuser_{suffix}@test.com");
        result.Role.Should().Be("User");
        result.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_DuplicateUsername_ReturnsConflict()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterRequest($"dupuser_{suffix}", $"dupuser_{suffix}@test.com", "Password123!");

        await client.PostAsJsonAsync("/api/auth/register", request);
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"dupuser_{suffix}", $"other_{suffix}@test.com", "Password123!"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"emaildup1_{suffix}", $"shared_{suffix}@test.com", "Password123!"));

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"emaildup2_{suffix}", $"shared_{suffix}@test.com", "Password123!"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsBadRequest()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("weakpwduser", "weakpwd@test.com", "123"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsBadRequest()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("bademailuser", "not-an-email", "Password123!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokens()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"loginuser_{suffix}", $"loginuser_{suffix}@test.com", "Password123!"));

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"loginuser_{suffix}", "Password123!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.Username.Should().Be($"loginuser_{suffix}");
        result.Role.Should().Be("User");
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"wrongpwd_{suffix}", $"wrongpwd_{suffix}@test.com", "Password123!"));

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"wrongpwd_{suffix}", "WrongPassword999!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_NonExistentUser_ReturnsUnauthorized()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nonexistent_user_xyz", "Password123!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ValidTokens_ReturnsNewTokens()
    {
        var client = fixture.CreateClient();
        var (token, refreshToken, _) = await fixture.RegisterAndLoginAsync(client);

        // Clear auth header to test refresh endpoint (which is AllowAnonymous)
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(token, refreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        // The refresh token must be different (always a new random value)
        result.RefreshToken.Should().NotBe(refreshToken);
    }

    [Fact]
    public async Task RefreshToken_InvalidRefreshToken_ReturnsUnauthorized()
    {
        var client = fixture.CreateClient();
        var (token, _, _) = await fixture.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(token, "invalid-refresh-token"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GuestLogin_ReturnsGuestSession()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsync("/api/auth/guest", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        result.Should().NotBeNull();
        result!.Username.Should().Be("guest");
        result.Role.Should().Be("Guest");
    }
}
