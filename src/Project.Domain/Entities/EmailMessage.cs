namespace Project.Domain.Entities;

public class EmailMessage
{
    public string Id { get; private set; } = null!;
    public string To { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public bool Sent { get; private set; }
    public DateTime? SentAt { get; private set; }

    private EmailMessage() { }

    public static EmailMessage Create(string to, string subject, string body)
    {
        return new EmailMessage
        {
            Id = Guid.NewGuid().ToString(),
            To = to,
            Subject = subject,
            Body = body,
            CreatedAt = DateTime.UtcNow,
            Sent = false
        };
    }

    public void MarkAsSent()
    {
        Sent = true;
        SentAt = DateTime.UtcNow;
    }
}
