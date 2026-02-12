using MongoDB.Driver;
using Polly;
using Polly.Registry;
using Project.Application.Ports;
using Project.Application.ReadModels;
using Project.Domain.Entities;
using Project.Infrastructure.Resilience;

namespace Project.Infrastructure.Persistence.Repositories;

public class UserReadRepository(
    MongoDbContext context,
    ResiliencePipelineProvider<string> resilienceProvider) : IUserReadRepository
{
    private readonly ResiliencePipeline _pipeline =
        resilienceProvider.GetPipeline(ResilienceExtensions.MongoDbPipeline);

    public async Task<UserReadModel?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
            await context.Users
                .Find(u => u.Id == id)
                .Project(Builders<User>.Projection.Expression(u => new UserReadModel(
                    u.Id,
                    u.Username,
                    u.Email.Value,
                    u.Role.ToString(),
                    u.CreatedAt,
                    u.IsActive)))
                .FirstOrDefaultAsync(token), ct);

    public async Task<IReadOnlyList<UserListItemReadModel>> GetAllAsync(CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
            (IReadOnlyList<UserListItemReadModel>)await context.Users
                .Find(_ => true)
                .Project(Builders<User>.Projection.Expression(u => new UserListItemReadModel(
                    u.Id,
                    u.Username,
                    u.Email.Value,
                    u.Role.ToString())))
                .ToListAsync(token), ct);
}
