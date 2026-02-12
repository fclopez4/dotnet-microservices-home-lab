using System.Security.Claims;
using MediatR;
using Project.Application.DTOs;
using Project.Application.Interfaces;
using Project.Domain.Ports;

namespace Project.Application.Commands.RefreshToken;

public class RefreshTokenHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IUserRepository userRepository,
    IJwtService jwtService) : IRequestHandler<RefreshTokenCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        // Validate the expired access token to extract user identity
        var principal = jwtService.ValidateTokenWithoutLifetime(request.AccessToken)
            ?? throw new UnauthorizedAccessException("Invalid access token.");

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Invalid token claims.");

        // Validate refresh token
        var storedToken = await refreshTokenRepository.GetByTokenAsync(request.RefreshToken, ct)
            ?? throw new UnauthorizedAccessException("Invalid refresh token.");

        if (!storedToken.IsActive || storedToken.UserId != userId)
            throw new UnauthorizedAccessException("Refresh token is expired or revoked.");

        // Revoke old refresh tokens for this user
        await refreshTokenRepository.RevokeByUserIdAsync(userId, ct);

        // Get user and generate new tokens
        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw new UnauthorizedAccessException("User not found.");

        var newAccessToken = jwtService.GenerateToken(user);
        var newRefreshToken = Domain.Entities.RefreshToken.Create(user.Id);
        await refreshTokenRepository.CreateAsync(newRefreshToken, ct);

        return new LoginResponse(newAccessToken, user.Username, user.Role.ToString(), newRefreshToken.Token);
    }
}
