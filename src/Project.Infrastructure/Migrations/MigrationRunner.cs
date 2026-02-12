using System.Reflection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Project.Infrastructure.Persistence;

namespace Project.Infrastructure.Migrations;

public class MigrationRunner(MongoDbContext context, ILogger<MigrationRunner> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var migrationsCollection = context.Database.GetCollection<MigrationRecord>("_migrations");

        var applied = await migrationsCollection.Find(_ => true).ToListAsync(ct);
        var appliedVersions = applied.Select(m => m.Version).ToHashSet();

        var migrations = DiscoverMigrations()
            .Where(m => !appliedVersions.Contains(m.Version))
            .OrderBy(m => m.Version);

        foreach (var migration in migrations)
        {
            logger.LogInformation("Applying migration {Version}: {Description}",
                migration.Version, migration.Description);

            try
            {
                await migration.UpAsync(context.Database, ct);

                await migrationsCollection.InsertOneAsync(new MigrationRecord
                {
                    Version = migration.Version,
                    Description = migration.Description,
                    AppliedAt = DateTime.UtcNow
                }, cancellationToken: ct);

                logger.LogInformation("Migration {Version} applied successfully", migration.Version);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Migration {Version} failed: {Description}",
                    migration.Version, migration.Description);
                throw;
            }
        }
    }

    private static IEnumerable<IMigration> DiscoverMigrations()
    {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IMigration).IsAssignableFrom(t))
            .Select(t => (IMigration)Activator.CreateInstance(t)!)
            .OrderBy(m => m.Version);
    }
}