using MediatR;
using Project.Application.DTOs;

namespace Project.Application.Commands.RegisterUser;

public record RegisterUserCommand(string Username, string Email, string Password) : IRequest<RegisterUserResponse>;
