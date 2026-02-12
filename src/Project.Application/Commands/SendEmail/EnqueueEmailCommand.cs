using MediatR;

namespace Project.Application.Commands.SendEmail;

public record EnqueueEmailCommand(string To, string Subject, string Body) : IRequest;
