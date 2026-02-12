namespace Project.Infrastructure.Messaging.Contracts;

/// <summary>
/// MassTransit message contract for sending emails.
/// </summary>
public record SendEmailMessage(
    string Id,
    string To,
    string Subject,
    string Body,
    DateTime CreatedAt);
