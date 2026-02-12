using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Polly;
using Polly.Registry;
using Project.Domain.Entities;
using Project.Domain.Ports;
using Project.Infrastructure.Resilience;

namespace Project.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository(MongoDbContext context, ResiliencePipelineProvider<string> resilience) : IRefreshTokenRepository
{
    private readonly IMongoCollection<RefreshToken> _collection = context.Database.GetCollection<RefreshToken>("refresh_tokens");
    private readonly ResiliencePipeline _pipeline = resilience.GetPipeline(ResilienceExtensions.MongoDbPipeline);

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async tk =>
            await _collection.Find(rt => rt.Token == token).FirstOrDefaultAsync(tk), ct);

    public async Task CreateAsync(RefreshToken refreshToken, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async tk =>
            { await _collection.InsertOneAsync(refreshToken, cancellationToken: tk); return true; }, ct);

    public async Task RevokeByUserIdAsync(string userId, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async tk =>
        {
            var filter = Builders<RefreshToken>.Filter.Where(rt => rt.UserId == userId && !rt.Revoked);
            var update = Builders<RefreshToken>.Update.Set(rt => rt.Revoked, true);
            await _collection.UpdateManyAsync(filter, update, cancellationToken: tk);
            return true;
        }, ct);
}
