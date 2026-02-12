using MediatR;
using Project.Application.Ports;
using Project.Application.ReadModels;

namespace Project.Application.Queries.GetUserById;

public record GetUserByIdQuery(string Id) : IRequest<UserReadModel?>;

public class GetUserByIdHandler(IUserReadRepository userReadRepository)
    : IRequestHandler<GetUserByIdQuery, UserReadModel?>
{
    public async Task<UserReadModel?> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        return await userReadRepository.GetByIdAsync(request.Id, ct);
    }
}
