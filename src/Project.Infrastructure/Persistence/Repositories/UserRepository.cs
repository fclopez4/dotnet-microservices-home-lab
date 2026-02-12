using MongoDB.Driver;
using Polly;
using Polly.Registry;
using Project.Domain.Entities;
using Project.Domain.Ports;
using Project.Infrastructure.Resilience;

namespace Project.Infrastructure.Persistence.Repositories;

public class UserRepository(
    MongoDbContext context,
    ResiliencePipelineProvider<string> resilienceProvider) : IUserRepository
{
    private readonly ResiliencePipeline _pipeline = resilienceProvider.GetPipeline(ResilienceExtensions.MongoDbPipeline);

    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
            await context.Users.Find(u => u.Id == id).FirstOrDefaultAsync(token), ct);

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
            await context.Users.Find(u => u.Username == username).FirstOrDefaultAsync(token), ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
            await context.Users.Find(u => u.Email == Domain.ValueObjects.Email.Create(email)).FirstOrDefaultAsync(token), ct);

    public async Task CreateAsync(User user, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
            await context.Users.InsertOneAsync(user, cancellationToken: token), ct);

    public async Task UpdateAsync(User user, CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
            await context.Users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: token), ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) =>
        await _pipeline.ExecuteAsync(async token =>
            (IReadOnlyList<User>)await context.Users.Find(_ => true).ToListAsync(token), ct);
}
