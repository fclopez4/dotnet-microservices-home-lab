using Project.Application.ReadModels;

namespace Project.Application.Ports;

public interface IUserReadRepository
{
    Task<UserReadModel?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<UserListItemReadModel>> GetAllAsync(CancellationToken ct = default);
}
