using FluentAssertions;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests;

[Collection("Integration")]
public class HealthCheckTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task HealthEndpoint_ShouldReturnOk()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
