using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Project.Api.Endpoints;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests.Api;

[Collection("Integration")]
public class EmailEndpointTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task SendEmail_Authenticated_ReturnsAccepted()
    {
        var client = fixture.CreateClient();
        await fixture.RegisterAndLoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/email/send",
            new EmailRequest("recipient@test.com", "Test Subject", "Test body content"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Verify the message was captured by InMemoryEmailQueue
        fixture.EmailQueue.Messages.Should().Contain(m => m.To == "recipient@test.com");
    }

    [Fact]
    public async Task SendEmail_Unauthenticated_ReturnsUnauthorized()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/email/send",
            new EmailRequest("recipient@test.com", "Subject", "Body"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendEmail_InvalidEmail_ReturnsBadRequest()
    {
        var client = fixture.CreateClient();
        await fixture.RegisterAndLoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/email/send",
            new EmailRequest("not-a-valid-email", "Subject", "Body"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendEmail_EmptySubject_ReturnsBadRequest()
    {
        var client = fixture.CreateClient();
        await fixture.RegisterAndLoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/email/send",
            new EmailRequest("valid@test.com", "", "Body"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
