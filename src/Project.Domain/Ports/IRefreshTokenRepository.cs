using Project.Domain.Entities;

namespace Project.Domain.Ports;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task CreateAsync(RefreshToken refreshToken, CancellationToken ct = default);
    Task RevokeByUserIdAsync(string userId, CancellationToken ct = default);
}
