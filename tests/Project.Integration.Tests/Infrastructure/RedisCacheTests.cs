using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests.Infrastructure;

[Collection("Integration")]
public class RedisCacheTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task SetAndGet_ReturnsStoredValue()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        var key = $"test_key_{Guid.NewGuid():N}";
        var value = "cached_value_123";

        await cache.SetStringAsync(key, value);
        var result = await cache.GetStringAsync(key);

        result.Should().Be(value);
    }

    [Fact]
    public async Task Get_NonExistentKey_ReturnsNull()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        var result = await cache.GetStringAsync("nonexistent_key_xyz");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Remove_ExistingKey_RemovesIt()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        var key = $"test_remove_{Guid.NewGuid():N}";
        await cache.SetStringAsync(key, "to_remove");

        await cache.RemoveAsync(key);
        var result = await cache.GetStringAsync(key);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetWithAbsoluteExpiry_StoresCorrectly()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        var key = $"test_expiry_{Guid.NewGuid():N}";
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        };

        await cache.SetStringAsync(key, "with_expiry", options);
        var result = await cache.GetStringAsync(key);

        result.Should().Be("with_expiry");
    }

    [Fact]
    public async Task SetBinaryData_ReturnsCorrectBytes()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        var key = $"test_binary_{Guid.NewGuid():N}";
        var data = Encoding.UTF8.GetBytes("{\"userId\":\"123\",\"role\":\"Admin\"}");

        await cache.SetAsync(key, data);
        var result = await cache.GetAsync(key);

        result.Should().BeEquivalentTo(data);
    }
}
