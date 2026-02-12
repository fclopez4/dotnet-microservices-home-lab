namespace Project.Infrastructure.Migrations;

public class MigrationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Version { get; set; }
    public string Description { get; set; } = null!;
    public DateTime AppliedAt { get; set; }
}