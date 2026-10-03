using System.Threading;
using System.Threading.Tasks;
using Aquarium.Outgame.Domain;

namespace Aquarium.Outgame.Application
{
    public sealed class GetAquariumUseCase : IGetAquariumUseCase
    {
        private readonly IGateway gateway;

        public GetAquariumUseCase(IGateway gateway)
        {
            this.gateway = gateway;
        }

        public Task<AquariumModel> GetAquariumAsync(CancellationToken cancellationToken)
        {
            return gateway.GetAquariumAsync(cancellationToken);
        }
    }
}
