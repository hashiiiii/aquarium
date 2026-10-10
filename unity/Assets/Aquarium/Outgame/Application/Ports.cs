using System.Threading;
using System.Threading.Tasks;
using Aquarium.Outgame.Domain;

namespace Aquarium.Outgame.Application
{
    public interface IGateway
    {
        Task<AquariumModel> GetAquariumAsync(CancellationToken cancellationToken);
    }

    // The requested port vocabulary is preserved at the application boundary.
    public interface IReader
    {
        Task<AquariumModel> GetAsync(CancellationToken cancellationToken);
        Task LoadAsync(CancellationToken cancellationToken);
    }

    public interface IWriter
    {
        Task SetAsync(AquariumModel model, CancellationToken cancellationToken);
        Task SaveAsync(CancellationToken cancellationToken);
    }

    public interface IGetAquariumUseCase
    {
        Task<AquariumModel> GetAquariumAsync(CancellationToken cancellationToken);
    }
}
