using System;
using System.Threading;
using System.Threading.Tasks;
using Aquarium.Online;
using Aquarium.Outgame.Application;
using Aquarium.Outgame.Client;
using Aquarium.Outgame.Domain;
using Aquarium.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Aquarium.Tests
{
    public sealed class OutgameUseCaseClientTests
    {
        [Test]
        public async Task GetAquariumUseCaseReturnsTheGatewayModelAndForwardsCancellation()
        {
            var model = new AquariumModel("7", "20", 40, 60, 1);
            var gateway = new FakeGateway(model);
            var useCase = new GetAquariumUseCase(gateway);
            using var cancellation = new CancellationTokenSource();

            var result = await useCase.GetAquariumAsync(cancellation.Token);

            Assert.That(result, Is.SameAs(model));
            Assert.That(gateway.Token, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public async Task GatewaySendsGetRequestAndMapsAuthoritativeSnapshot()
        {
            var settings = ScriptableObject.CreateInstance<AquariumConnectionSettings>();
            using var transport = new FakeTransport(new OnlineHttpResponse(200,
                "{\"state\":{\"version\":1,\"revision\":\"17\",\"lastUpdatedAtUnixMs\":\"1790985600000\",\"pearls\":\"42\",\"fullness\":75,\"cleanliness\":85,\"creatures\":[{\"speciesId\":\"tide_sprite\"},{\"speciesId\":\"moon_jelly\"}]}}"));
            using var gateway = new AuthoritativeAquariumGateway(settings, new UnityOnlineJsonCodec(), transport);
            using var cancellation = new CancellationTokenSource();

            var model = await gateway.GetAquariumAsync(cancellation.Token);

            Assert.That(transport.Endpoint.AbsolutePath, Does.EndWith("/aquarium.v1.AquariumService/GetAquarium"));
            Assert.That(transport.Player, Is.EqualTo("unity-dev"));
            Assert.That(transport.Body, Is.EqualTo("{}"));
            Assert.That(transport.Token, Is.EqualTo(cancellation.Token));
            Assert.That(model.Revision, Is.EqualTo("17"));
            Assert.That(model.PearlBalance, Is.EqualTo(42));
            Assert.That(model.Fullness, Is.EqualTo(75));
            Assert.That(model.Cleanliness, Is.EqualTo(85));
            Assert.That(model.CreatureCount, Is.EqualTo(2));
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void GatewayPreservesTypedServerErrors()
        {
            var settings = ScriptableObject.CreateInstance<AquariumConnectionSettings>();
            using var transport = new FakeTransport(new OnlineHttpResponse(503,
                "{\"code\":\"unavailable\",\"message\":\"offline\"}"));
            using var gateway = new AuthoritativeAquariumGateway(settings, new UnityOnlineJsonCodec(), transport);

            var exception = Assert.ThrowsAsync<OnlineRpcException>(async () =>
                await gateway.GetAquariumAsync(CancellationToken.None));

            Assert.That(exception.Code, Is.EqualTo("unavailable"));
            Assert.That(exception.StatusCode, Is.EqualTo(503));
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void GatewayDoesNotDisposeTransportOwnedByTheContainer()
        {
            var settings = ScriptableObject.CreateInstance<AquariumConnectionSettings>();
            using var transport = new FakeTransport(new OnlineHttpResponse(200, "{}"));
            var gateway = new AuthoritativeAquariumGateway(settings, new UnityOnlineJsonCodec(), transport);

            gateway.Dispose();

            Assert.That(transport.IsDisposed, Is.False);
            transport.Dispose();
            Assert.That(transport.IsDisposed, Is.True);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        private sealed class FakeGateway : IGateway
        {
            private readonly AquariumModel model;
            public CancellationToken Token { get; private set; }
            public FakeGateway(AquariumModel model) => this.model = model;
            public Task<AquariumModel> GetAquariumAsync(CancellationToken cancellationToken)
            {
                Token = cancellationToken;
                return Task.FromResult(model);
            }
        }

        private sealed class FakeTransport : IOnlineTransport, IDisposable
        {
            private readonly OnlineHttpResponse response;
            public bool IsDisposed { get; private set; }
            public Uri Endpoint { get; private set; }
            public string Player { get; private set; }
            public string Body { get; private set; }
            public CancellationToken Token { get; private set; }

            public FakeTransport(OnlineHttpResponse response) => this.response = response;

            public Task<OnlineHttpResponse> PostAsync(Uri endpoint, string devPlayer, string json,
                CancellationToken cancellationToken)
            {
                Endpoint = endpoint;
                Player = devPlayer;
                Body = json;
                Token = cancellationToken;
                return Task.FromResult(response);
            }

            public void Dispose() => IsDisposed = true;
        }
    }
}
