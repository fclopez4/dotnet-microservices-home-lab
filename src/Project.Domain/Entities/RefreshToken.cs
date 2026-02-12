namespace Project.Domain.Entities;

public class RefreshToken
{
    public string Id { get; private set; } = null!;
    public string Token { get; private set; } = null!;
    public string UserId { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool Revoked { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(string userId, int expirationDays = 7)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid().ToString(),
            Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                    + Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(expirationDays),
            CreatedAt = DateTime.UtcNow,
            Revoked = false
        };
    }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsActive => !Revoked && !IsExpired;
    public void Revoke() => Revoked = true;
}
