using MediatR;
using Project.Application.DTOs;
using Project.Application.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Ports;

namespace Project.Application.Commands.Login;

public class LoginHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtService jwtService,
    IRefreshTokenRepository refreshTokenRepository) : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await userRepository.GetByUsernameAsync(request.Username, ct)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is deactivated.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials.");

        // Revoke previous refresh tokens
        await refreshTokenRepository.RevokeByUserIdAsync(user.Id, ct);

        var token = jwtService.GenerateToken(user);
        var refreshToken = Domain.Entities.RefreshToken.Create(user.Id);
        await refreshTokenRepository.CreateAsync(refreshToken, ct);

        return new LoginResponse(token, user.Username, user.Role.ToString(), refreshToken.Token);
    }
}
