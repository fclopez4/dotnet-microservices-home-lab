using MongoDB.Bson;
using MongoDB.Driver;

namespace Project.Infrastructure.Migrations;

public class M001_CreateIndexes : IMigration
{
    public int Version => 1;
    public string Description => "Create unique indexes on users collection";

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct = default)
    {
        var users = database.GetCollection<BsonDocument>("users");

        var usernameIndex = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("Username"),
            new CreateIndexOptions { Unique = true });

        var emailIndex = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("Email.Value"),
            new CreateIndexOptions { Unique = true });

        await users.Indexes.CreateManyAsync([usernameIndex, emailIndex], ct);
    }
}