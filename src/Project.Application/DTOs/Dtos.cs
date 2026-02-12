namespace Project.Application.DTOs;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, string Role, string? RefreshToken = null);
public record RefreshRequest(string AccessToken, string RefreshToken);
public record RegisterRequest(string Username, string Email, string Password);
public record RegisterUserResponse(string Id, string Username, string Email, string Role, DateTime CreatedAt);
