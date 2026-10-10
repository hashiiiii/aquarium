using System;
using System.Threading.Tasks;
using Aquarium.Online;
using Aquarium.Runtime;

namespace Aquarium.Outgame.Client
{
    public interface IAquariumOnlineSessionFactory
    {
        AquariumOnlineSession Create();
        void Track(Task operation);
    }

    /// <summary>Creates the scene's authoritative session and releases its transport/journal with its child scope.</summary>
    public sealed class AquariumOnlineSessionFactory : IAquariumOnlineSessionFactory, IDisposable
    {
        private readonly AquariumConnectionSettings settings;
        private HttpClientOnlineTransport transport;
        private FilePendingCommandStore journal;
        private Task currentOperation;
        private bool disposed;

        public AquariumOnlineSessionFactory(AquariumConnectionSettings settings)
        {
            this.settings = settings;
        }

        public AquariumOnlineSession Create()
        {
            if (disposed) throw new ObjectDisposedException(nameof(AquariumOnlineSessionFactory));
            if (transport != null) throw new InvalidOperationException("This scene already owns an online session.");
            var options = new DevServerOptions(settings.Endpoint, settings.DevelopmentPlayer);
            transport = new HttpClientOnlineTransport();
            try
            {
                var api = new ReefApiClient(options, new UnityOnlineJsonCodec(), transport);
                journal = new FilePendingCommandStore(GetJournalPath(options));
                return new AquariumOnlineSession(api, journal);
            }
            catch
            {
                DisposeOwnedResources();
                throw;
            }
        }

        public void Track(Task operation)
        {
            currentOperation = operation;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (currentOperation != null && !currentOperation.IsCompleted)
                currentOperation.ContinueWith(_ => DisposeOwnedResources(), TaskScheduler.Default);
            else
                DisposeOwnedResources();
        }

        private void DisposeOwnedResources()
        {
            transport?.Dispose();
            transport = null;
            journal?.Dispose();
            journal = null;
        }

        private static string GetJournalPath(DevServerOptions options)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                var digest = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(options.Endpoint + "\n" + options.DevPlayer));
                var key = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
                return System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "online-command-" + key + ".json");
            }
        }
    }
}
