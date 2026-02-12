namespace Project.Application.ReadModels;

public sealed record UserReadModel(
    string Id,
    string Username,
    string Email,
    string Role,
    DateTime CreatedAt,
    bool IsActive);
