using Project.Application.Commands.RegisterUser;
using Project.Domain.Entities;
using Project.Domain.Ports;
using FluentAssertions;
using NSubstitute;

namespace Project.Application.Tests;

public class RegisterUserTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();

    [Fact]
    public async Task Handle_NewUser_ShouldCreateSuccessfully()
    {
        _userRepo.GetByUsernameAsync(Arg.Any<string>()).Returns((User?)null);
        _userRepo.GetByEmailAsync(Arg.Any<string>()).Returns((User?)null);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed_password");

        var handler = new RegisterUserHandler(_userRepo, _hasher);
        var command = new RegisterUserCommand("usuario", "usuario@project.es", "Password123!");

        var result = await handler.Handle(command, CancellationToken.None);

        result.Username.Should().Be("usuario");
        result.Email.Should().Be("usuario@project.es");
        result.Role.Should().Be("User");
        await _userRepo.Received(1).CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingUsername_ShouldThrow()
    {
        var existing = User.Create("usuario",
            Project.Domain.ValueObjects.Email.Create("old@test.com"), "hash",
            Project.Domain.Enums.UserRole.User);
        _userRepo.GetByUsernameAsync("usuario").Returns(existing);

        var handler = new RegisterUserHandler(_userRepo, _hasher);
        var command = new RegisterUserCommand("usuario", "new@test.com", "pass");

        var act = () => handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }
}
