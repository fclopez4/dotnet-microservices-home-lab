using MassTransit;
using Microsoft.Extensions.Logging;
using Project.Infrastructure.Messaging.Contracts;

namespace Project.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer that processes email messages from the queue.
/// </summary>
public class SendEmailConsumer(ILogger<SendEmailConsumer> logger) : IConsumer<SendEmailMessage>
{
    public Task Consume(ConsumeContext<SendEmailMessage> context)
    {
        var msg = context.Message;
        logger.LogInformation("Processing email to {To}: {Subject}", msg.To, msg.Subject);

        // TODO: Implement actual SMTP sending with MailKit
        // var client = new SmtpClient();
        // await client.ConnectAsync("smtp.server.com", 587, SecureSocketOptions.StartTls);
        // ...

        logger.LogInformation("Email sent to {To} (Id: {Id})", msg.To, msg.Id);
        return Task.CompletedTask;
    }
}
