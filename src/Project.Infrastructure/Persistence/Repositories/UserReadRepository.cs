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
        {
            var user = await context.Users
                .Find(u => u.Id == id)
                .FirstOrDefaultAsync(token);

            return user is null
                ? null
                : new UserReadModel(
                    user.Id, user.Username, user.Email.Value,
                    user.Role.ToString(), user.CreatedAt, user.IsActive);
        }, ct);

    public async Task<IReadOnlyList<UserListItemReadModel>> GetAllAsync(CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
        {
            var users = await context.Users
                .Find(_ => true)
                .ToListAsync(token);

            return (IReadOnlyList<UserListItemReadModel>)users
                .Select(u => new UserListItemReadModel(
                    u.Id, u.Username, u.Email.Value, u.Role.ToString()))
                .ToList();
        }, ct);
}
