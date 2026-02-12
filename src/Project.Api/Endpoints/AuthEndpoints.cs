using MediatR;
using Project.Application.Commands.Login;
using Project.Application.Commands.RefreshToken;
using Project.Application.Commands.RegisterUser;
using Project.Application.DTOs;

namespace Project.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Autenticacion");

        group.MapPost("/login", async (LoginRequest request, IMediator mediator) =>
        {
            try
            {
                var result = await mediator.Send(new LoginCommand(request.Username, request.Password));
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
        })
        .WithName("Login")
        .AllowAnonymous()
        .Produces<LoginResponse>(200)
        .Produces(401);

        group.MapPost("/register", async (RegisterRequest request, IMediator mediator) =>
        {
            try
            {
                var result = await mediator.Send(
                    new RegisterUserCommand(request.Username, request.Email, request.Password));
                return Results.Created($"/api/users/{result.Id}", result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("Register")
        .AllowAnonymous()
        .Produces<RegisterUserResponse>(201)
        .Produces(409);

        group.MapPost("/refresh", async (RefreshRequest request, IMediator mediator) =>
        {
            try
            {
                var result = await mediator.Send(
                    new RefreshTokenCommand(request.AccessToken, request.RefreshToken));
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
        })
        .WithName("RefreshToken")
        .AllowAnonymous()
        .Produces<LoginResponse>(200)
        .Produces(401);

        group.MapPost("/guest", () =>
        {
            return Results.Ok(new LoginResponse("guest-session", "guest", "Guest"));
        })
        .WithName("GuestLogin")
        .AllowAnonymous();
    }
}
