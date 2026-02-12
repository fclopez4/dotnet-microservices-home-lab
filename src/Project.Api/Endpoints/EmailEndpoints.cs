using MediatR;
using Project.Application.Commands.SendEmail;

namespace Project.Api.Endpoints;

public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/email").WithTags("Email").RequireAuthorization();

        group.MapPost("/send", async (EmailRequest request, IMediator mediator) =>
        {
            await mediator.Send(new EnqueueEmailCommand(request.To, request.Subject, request.Body));
            return Results.Accepted(value: new { message = "Email encolado para envio" });
        })
        .WithName("SendEmail")
        .Produces(202);
    }
}

public record EmailRequest(string To, string Subject, string Body);
