using MongoDB.Driver;
using Project.Domain.Entities;

namespace Project.Infrastructure.Persistence;

public class MongoDbContext
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;

    public MongoDbContext(string connectionString, string databaseName)
    {
        _client = new MongoClient(connectionString);
        _database = _client.GetDatabase(databaseName);
    }

    public IMongoClient Client => _client;
    public IMongoDatabase Database => _database;

    public IMongoCollection<User> Users => _database.GetCollection<User>("users");
    public IMongoCollection<EmailMessage> EmailMessages => _database.GetCollection<EmailMessage>("email_messages");
}
