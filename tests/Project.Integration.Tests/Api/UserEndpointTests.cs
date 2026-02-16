using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Project.Application.ReadModels;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests.Api;

[Collection("Integration")]
public class UserEndpointTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task GetUser_Authenticated_ReturnsUserReadModel()
    {
        var client = fixture.CreateClient();
        var (_, _, userId) = await fixture.RegisterAndLoginAsync(client);

        var response = await client.GetAsync($"/api/users/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<UserReadModel>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(userId);
        result.IsActive.Should().BeTrue();
        result.Role.Should().Be("User");
    }

    [Fact]
    public async Task GetUser_NonExistent_ReturnsNotFound()
    {
        var client = fixture.CreateClient();
        await fixture.RegisterAndLoginAsync(client);

        var response = await client.GetAsync($"/api/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUser_Unauthenticated_ReturnsUnauthorized()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/users/some-id");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAllUsers_Authenticated_ReturnsList()
    {
        var client = fixture.CreateClient();
        await fixture.RegisterAndLoginAsync(client);

        var response = await client.GetAsync("/api/users/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<UserListItemReadModel>>();
        result.Should().NotBeNull();
        result!.Count.Should().BeGreaterThanOrEqualTo(1);
        result.First().Id.Should().NotBeNullOrEmpty();
        result.First().Username.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAllUsers_Unauthenticated_ReturnsUnauthorized()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/users/");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminDashboard_NonAdminUser_ReturnsForbidden()
    {
        var client = fixture.CreateClient();
        await fixture.RegisterAndLoginAsync(client);

        var response = await client.GetAsync("/api/users/admin/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
