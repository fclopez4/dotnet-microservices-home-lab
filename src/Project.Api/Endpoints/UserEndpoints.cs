using MediatR;
using Project.Application.Queries.GetAllUsers;
using Project.Application.Queries.GetUserById;
using Project.Application.ReadModels;
using Project.Domain.Enums;

namespace Project.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").WithTags("Usuarios").RequireAuthorization();

        group.MapGet("/{id}", async (string id, IMediator mediator) =>
        {
            var user = await mediator.Send(new GetUserByIdQuery(id));
            return user is not null ? Results.Ok(user) : Results.NotFound();
        })
        .WithName("GetUser")
        .Produces<UserReadModel>(200)
        .Produces(404);

        group.MapGet("/", async (IMediator mediator) =>
        {
            var users = await mediator.Send(new GetAllUsersQuery());
            return Results.Ok(users);
        })
        .WithName("GetAllUsers")
        .Produces<IReadOnlyList<UserListItemReadModel>>(200);

        group.MapGet("/admin/dashboard", () =>
        {
            return Results.Ok(new { message = "Panel de administracion de Project" });
        })
        .RequireAuthorization(policy => policy.RequireRole(UserRole.Admin.ToString()));
    }
}
