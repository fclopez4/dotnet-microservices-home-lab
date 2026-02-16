using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Project.Application.Ports;
using Project.Domain.Entities;
using Project.Domain.Enums;
using Project.Domain.Ports;
using Project.Domain.ValueObjects;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests.Infrastructure;

[Collection("Integration")]
public class MongoDbRepositoryTests(IntegrationTestFixture fixture)
{
    // --- UserRepository ---

    [Fact]
    public async Task UserRepository_CreateAndGetById_ReturnsUser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = User.Create("mongo_test_1", Email.Create("mongo1@test.com"), "hash", UserRole.User);
        await repo.CreateAsync(user);

        var fetched = await repo.GetByIdAsync(user.Id);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(user.Id);
        fetched.Username.Should().Be("mongo_test_1");
        fetched.Email.Value.Should().Be("mongo1@test.com");
        fetched.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UserRepository_GetByUsername_ReturnsUser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = User.Create("mongo_byname", Email.Create("byname@test.com"), "hash", UserRole.User);
        await repo.CreateAsync(user);

        var fetched = await repo.GetByUsernameAsync("mongo_byname");

        fetched.Should().NotBeNull();
        fetched!.Username.Should().Be("mongo_byname");
    }

    [Fact]
    public async Task UserRepository_GetByEmail_ReturnsUser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = User.Create("mongo_byemail", Email.Create("byemail@test.com"), "hash", UserRole.User);
        await repo.CreateAsync(user);

        var fetched = await repo.GetByEmailAsync("byemail@test.com");

        fetched.Should().NotBeNull();
        fetched!.Email.Value.Should().Be("byemail@test.com");
    }

    [Fact]
    public async Task UserRepository_GetByUsername_NonExistent_ReturnsNull()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var result = await repo.GetByUsernameAsync("nonexistent_user_abc");

        result.Should().BeNull();
    }

    [Fact]
    public async Task UserRepository_UpdateUser_PersistsChanges()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = User.Create("mongo_update", Email.Create("update@test.com"), "hash", UserRole.User);
        await repo.CreateAsync(user);

        user.ChangeRole(UserRole.Admin);
        await repo.UpdateAsync(user);

        var fetched = await repo.GetByIdAsync(user.Id);
        fetched!.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task UserRepository_Deactivate_PersistsChange()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = User.Create("mongo_deact", Email.Create("deact@test.com"), "hash", UserRole.User);
        await repo.CreateAsync(user);

        user.Deactivate();
        await repo.UpdateAsync(user);

        var fetched = await repo.GetByIdAsync(user.Id);
        fetched!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UserRepository_GetAll_ReturnsAllUsers()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user1 = User.Create("mongo_all_1", Email.Create("all1@test.com"), "hash", UserRole.User);
        var user2 = User.Create("mongo_all_2", Email.Create("all2@test.com"), "hash", UserRole.Admin);
        await repo.CreateAsync(user1);
        await repo.CreateAsync(user2);

        var all = await repo.GetAllAsync();

        all.Should().Contain(u => u.Username == "mongo_all_1");
        all.Should().Contain(u => u.Username == "mongo_all_2");
    }

    // --- RefreshTokenRepository ---

    [Fact]
    public async Task RefreshTokenRepository_CreateAndGet_ReturnsToken()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

        var token = RefreshToken.Create("test-user-id-123");
        await repo.CreateAsync(token);

        var fetched = await repo.GetByTokenAsync(token.Token);

        fetched.Should().NotBeNull();
        fetched!.UserId.Should().Be("test-user-id-123");
        fetched.IsActive.Should().BeTrue();
        fetched.Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshTokenRepository_RevokeByUserId_RevokesAllTokens()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

        var userId = Guid.NewGuid().ToString();
        var token1 = RefreshToken.Create(userId);
        var token2 = RefreshToken.Create(userId);
        await repo.CreateAsync(token1);
        await repo.CreateAsync(token2);

        await repo.RevokeByUserIdAsync(userId);

        var fetched1 = await repo.GetByTokenAsync(token1.Token);
        var fetched2 = await repo.GetByTokenAsync(token2.Token);
        fetched1!.Revoked.Should().BeTrue();
        fetched1.IsActive.Should().BeFalse();
        fetched2!.Revoked.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshTokenRepository_GetByToken_NonExistent_ReturnsNull()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

        var result = await repo.GetByTokenAsync("nonexistent-token-value");

        result.Should().BeNull();
    }

    // --- UserReadRepository (CQRS) ---

    [Fact]
    public async Task UserReadRepository_GetById_ReturnsReadModel()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var writeRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var readRepo = scope.ServiceProvider.GetRequiredService<IUserReadRepository>();

        var user = User.Create("read_detail", Email.Create("detail@test.com"), "hash", UserRole.Admin);
        await writeRepo.CreateAsync(user);

        var readModel = await readRepo.GetByIdAsync(user.Id);

        readModel.Should().NotBeNull();
        readModel!.Id.Should().Be(user.Id);
        readModel.Username.Should().Be("read_detail");
        readModel.Email.Should().Be("detail@test.com");
        readModel.Role.Should().Be("Admin");
        readModel.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UserReadRepository_GetAll_ReturnsListItemReadModels()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var writeRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var readRepo = scope.ServiceProvider.GetRequiredService<IUserReadRepository>();

        var user = User.Create("read_list_item", Email.Create("listitem@test.com"), "hash", UserRole.User);
        await writeRepo.CreateAsync(user);

        var all = await readRepo.GetAllAsync();

        all.Should().Contain(u => u.Username == "read_list_item");
        var item = all.First(u => u.Username == "read_list_item");
        item.Email.Should().Be("listitem@test.com");
        item.Role.Should().Be("User");
    }

    [Fact]
    public async Task UserReadRepository_GetById_NonExistent_ReturnsNull()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var readRepo = scope.ServiceProvider.GetRequiredService<IUserReadRepository>();

        var result = await readRepo.GetByIdAsync(Guid.NewGuid().ToString());

        result.Should().BeNull();
    }
}
