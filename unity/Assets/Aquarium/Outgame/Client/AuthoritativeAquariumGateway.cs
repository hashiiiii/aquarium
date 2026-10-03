using System;
using System.Threading;
using System.Threading.Tasks;
using Aquarium.Online;
using Aquarium.Outgame.Application;
using Aquarium.Outgame.Domain;
using Aquarium.Runtime;

namespace Aquarium.Outgame.Client
{
    /// <summary>Reads the same development identity and local endpoint used by the online aquarium scene.</summary>
    public sealed class AuthoritativeAquariumGateway : IGateway, IDisposable
    {
        private readonly IDisposable transport;
        private readonly ReefApiClient client;

        public AuthoritativeAquariumGateway(AquariumConnectionSettings settings)
            : this(settings, new UnityOnlineJsonCodec(), new HttpClientOnlineTransport())
        {
        }

        public AuthoritativeAquariumGateway(AquariumConnectionSettings settings, IOnlineJsonCodec codec,
            IOnlineTransport transport)
        {
            var options = new DevServerOptions(settings.Endpoint, settings.DevelopmentPlayer);
            this.transport = transport as IDisposable;
            client = new ReefApiClient(options, codec, transport);
        }

        public async Task<AquariumModel> GetAquariumAsync(CancellationToken cancellationToken)
        {
            var snapshot = await client.GetAquariumAsync(cancellationToken);
            return new AquariumModel(snapshot.revision, snapshot.pearls, snapshot.fullness,
                snapshot.cleanliness, snapshot.creatures == null ? 0 : snapshot.creatures.Length);
        }

        public void Dispose() => transport?.Dispose();
    }
}
