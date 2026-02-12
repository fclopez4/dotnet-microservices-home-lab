using MediatR;
using Project.Application.DTOs;

namespace Project.Application.Commands.Login;

public record LoginCommand(string Username, string Password) : IRequest<LoginResponse>;
