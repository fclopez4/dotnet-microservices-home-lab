using Project.Domain.Entities;

namespace Project.Domain.Ports;

public interface IEmailQueue
{
    Task EnqueueAsync(EmailMessage message, CancellationToken ct = default);
    Task<EmailMessage?> DequeueAsync(CancellationToken ct = default);
}
