using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Project.Infrastructure.Messaging.Consumers;
using Project.Infrastructure.Messaging.Contracts;

namespace Project.Integration.Tests.Infrastructure;

public class MassTransitTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<SendEmailConsumer>();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Publish_SendEmailMessage_IsConsumedByConsumer()
    {
        var message = new SendEmailMessage(
            Guid.NewGuid().ToString(),
            "consumer@test.com",
            "Test Subject",
            "Test Body",
            DateTime.UtcNow);

        await _harness.Bus.Publish(message);

        (await _harness.Consumed.Any<SendEmailMessage>()).Should().BeTrue();

        var consumerHarness = _harness.GetConsumerHarness<SendEmailConsumer>();
        (await consumerHarness.Consumed.Any<SendEmailMessage>()).Should().BeTrue();
    }

    [Fact]
    public async Task Publish_MultipleMessages_AllConsumed()
    {
        for (var i = 0; i < 3; i++)
        {
            await _harness.Bus.Publish(new SendEmailMessage(
                Guid.NewGuid().ToString(),
                $"batch{i}@test.com",
                $"Subject {i}",
                $"Body {i}",
                DateTime.UtcNow));
        }

        // Wait for all messages to be consumed
        await Task.Delay(500);

        var consumerHarness = _harness.GetConsumerHarness<SendEmailConsumer>();
        (await consumerHarness.Consumed.Any<SendEmailMessage>(x => x.Context.Message.To == "batch0@test.com"))
            .Should().BeTrue();
        (await consumerHarness.Consumed.Any<SendEmailMessage>(x => x.Context.Message.To == "batch2@test.com"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Publish_SendEmailMessage_MessageContainsCorrectData()
    {
        var messageId = Guid.NewGuid().ToString();
        var message = new SendEmailMessage(
            messageId,
            "data@test.com",
            "Important Subject",
            "Detailed body content",
            DateTime.UtcNow);

        await _harness.Bus.Publish(message);

        (await _harness.Published.Any<SendEmailMessage>(x =>
            x.Context.Message.Id == messageId &&
            x.Context.Message.To == "data@test.com" &&
            x.Context.Message.Subject == "Important Subject")).Should().BeTrue();
    }
}
