using Project.Application.Ports;
using Project.Application.Queries.GetUserById;
using Project.Application.ReadModels;
using FluentAssertions;
using NSubstitute;

namespace Project.Application.Tests;

public class GetUserByIdTests
{
    private readonly IUserReadRepository _readRepo = Substitute.For<IUserReadRepository>();

    [Fact]
    public async Task Handle_ExistingUser_ShouldReturnReadModel()
    {
        var expected = new UserReadModel("123", "usuario", "usuario@test.com", "User",
            DateTime.UtcNow, true);
        _readRepo.GetByIdAsync("123", Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new GetUserByIdHandler(_readRepo);
        var result = await handler.Handle(new GetUserByIdQuery("123"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("123");
        result.Username.Should().Be("usuario");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NonExistingUser_ShouldReturnNull()
    {
        _readRepo.GetByIdAsync("999", Arg.Any<CancellationToken>()).Returns((UserReadModel?)null);

        var handler = new GetUserByIdHandler(_readRepo);
        var result = await handler.Handle(new GetUserByIdQuery("999"), CancellationToken.None);

        result.Should().BeNull();
    }
}
