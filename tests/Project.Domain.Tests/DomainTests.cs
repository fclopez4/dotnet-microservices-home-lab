using Project.Domain.ValueObjects;
using Project.Domain.Entities;
using Project.Domain.Enums;
using Project.Domain.Exceptions;
using FluentAssertions;

namespace Project.Domain.Tests;

public class EmailTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("test@project.es")]
    public void Create_ValidEmail_ShouldSucceed(string email)
    {
        var result = Email.Create(email);
        result.Value.Should().Be(email.ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("@no-user.com")]
    public void Create_InvalidEmail_ShouldThrow(string email)
    {
        var act = () => Email.Create(email);
        act.Should().Throw<DomainException>();
    }
}

public class UserTests
{
    [Fact]
    public void Create_ValidUser_ShouldSetProperties()
    {
        var email = Email.Create("usuario@project.es");
        var user = User.Create("usuario", email, "hash123", UserRole.Admin);

        user.Username.Should().Be("usuario");
        user.Email.Value.Should().Be("usuario@project.es");
        user.Role.Should().Be(UserRole.Admin);
        user.IsActive.Should().BeTrue();
        user.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateGuest_ShouldHaveGuestRole()
    {
        var guest = User.CreateGuest();
        guest.Role.Should().Be(UserRole.Guest);
        guest.Username.Should().Be("guest");
    }

    [Fact]
    public void Deactivate_ShouldSetIsActiveFalse()
    {
        var email = Email.Create("test@test.com");
        var user = User.Create("test", email, "hash", UserRole.User);
        user.Deactivate();
        user.IsActive.Should().BeFalse();
    }
}
