namespace Project.Application.ReadModels;

public sealed record UserListItemReadModel(
    string Id,
    string Username,
    string Email,
    string Role);
