using Project.Domain.Enums;
using Project.Domain.ValueObjects;

namespace Project.Domain.Entities;

public class User
{
    public string Id { get; private set; } = null!;
    public string Username { get; private set; } = null!;
    public Email Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }

    private User() { } // Para MongoDB

    public static User Create(string username, Email email, string passwordHash, UserRole role)
    {
        return new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = username,
            Email = email,
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    public static User CreateGuest()
    {
        return new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "guest",
            Email = Email.Create("guest@system.local"),
            PasswordHash = string.Empty,
            Role = UserRole.Guest,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    public void Deactivate() => IsActive = false;
    public void ChangeRole(UserRole newRole) => Role = newRole;
}
