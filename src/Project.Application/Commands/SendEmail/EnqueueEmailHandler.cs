using MediatR;
using Project.Domain.Entities;
using Project.Domain.Ports;

namespace Project.Application.Commands.SendEmail;

public class EnqueueEmailHandler(IEmailQueue emailQueue) : IRequestHandler<EnqueueEmailCommand>
{
    public async Task Handle(EnqueueEmailCommand request, CancellationToken ct)
    {
        var message = EmailMessage.Create(request.To, request.Subject, request.Body);
        await emailQueue.EnqueueAsync(message, ct);
    }
}
