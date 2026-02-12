using MediatR;
using Project.Application.DTOs;

namespace Project.Application.Commands.RefreshToken;

public record RefreshTokenCommand(string AccessToken, string RefreshToken) : IRequest<LoginResponse>;
