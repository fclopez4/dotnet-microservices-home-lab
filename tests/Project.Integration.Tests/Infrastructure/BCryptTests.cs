using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Project.Domain.Ports;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests.Infrastructure;

[Collection("Integration")]
public class BCryptTests(IntegrationTestFixture fixture)
{
    [Fact]
    public void Hash_ReturnsNonEmptyHash()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var hash = hasher.Hash("MyPassword123!");

        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Hash_DifferentFromInput()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var password = "MyPassword123!";
        var hash = hasher.Hash(password);

        hash.Should().NotBe(password);
    }

    [Fact]
    public void Hash_SamePassword_ProducesDifferentHashes()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var hash1 = hasher.Hash("SamePassword!");
        var hash2 = hasher.Hash("SamePassword!");

        hash1.Should().NotBe(hash2, "BCrypt uses random salt per hash");
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var hash = hasher.Hash("CorrectPassword123!");
        var result = hasher.Verify("CorrectPassword123!", hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var hash = hasher.Hash("CorrectPassword123!");
        var result = hasher.Verify("WrongPassword456!", hash);

        result.Should().BeFalse();
    }
}
