using MediatR;
using Project.Application.DTOs;
using Project.Domain.Entities;
using Project.Domain.Enums;
using Project.Domain.Ports;
using Project.Domain.ValueObjects;

namespace Project.Application.Commands.RegisterUser;

public class RegisterUserHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher) : IRequestHandler<RegisterUserCommand, RegisterUserResponse>
{
    public async Task<RegisterUserResponse> Handle(RegisterUserCommand request, CancellationToken ct)
    {
        var existing = await userRepository.GetByUsernameAsync(request.Username, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Username '{request.Username}' already exists.");

        var existingEmail = await userRepository.GetByEmailAsync(request.Email, ct);
        if (existingEmail is not null)
            throw new InvalidOperationException($"Email '{request.Email}' already registered.");

        var email = Email.Create(request.Email);
        var hash = passwordHasher.Hash(request.Password);
        var user = User.Create(request.Username, email, hash, UserRole.User);

        await userRepository.CreateAsync(user, ct);

        return new RegisterUserResponse(user.Id, user.Username, user.Email.Value, user.Role.ToString(), user.CreatedAt);
    }
}
