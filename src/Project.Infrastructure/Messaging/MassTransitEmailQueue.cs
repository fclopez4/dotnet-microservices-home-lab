using MassTransit;
using Microsoft.Extensions.Logging;
using Project.Domain.Entities;
using Project.Domain.Ports;
using Project.Infrastructure.Messaging.Contracts;

namespace Project.Infrastructure.Messaging;

/// <summary>
/// IEmailQueue implementation using MassTransit publish.
/// Messages are published through the outbox for guaranteed delivery.
/// </summary>
public class MassTransitEmailQueue(IPublishEndpoint publishEndpoint, ILogger<MassTransitEmailQueue> logger) : IEmailQueue
{
    public async Task EnqueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        await publishEndpoint.Publish(new SendEmailMessage(
            message.Id,
            message.To,
            message.Subject,
            message.Body,
            message.CreatedAt), ct);

        logger.LogInformation("Email message {Id} published to bus for {To}", message.Id, message.To);
    }

    public Task<EmailMessage?> DequeueAsync(CancellationToken ct = default)
    {
        // MassTransit consumers handle dequeue automatically.
        // This method exists for interface compatibility but is not used.
        return Task.FromResult<EmailMessage?>(null);
    }
}
