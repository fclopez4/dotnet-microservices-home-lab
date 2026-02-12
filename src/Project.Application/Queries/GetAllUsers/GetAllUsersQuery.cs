using MediatR;
using Project.Application.Ports;
using Project.Application.ReadModels;

namespace Project.Application.Queries.GetAllUsers;

public record GetAllUsersQuery : IRequest<IReadOnlyList<UserListItemReadModel>>;

public class GetAllUsersHandler(IUserReadRepository userReadRepository)
    : IRequestHandler<GetAllUsersQuery, IReadOnlyList<UserListItemReadModel>>
{
    public async Task<IReadOnlyList<UserListItemReadModel>> Handle(
        GetAllUsersQuery request, CancellationToken ct)
    {
        return await userReadRepository.GetAllAsync(ct);
    }
}
