using Aquarium.Outgame.Application;
using Aquarium.Online;
using Aquarium.Runtime;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Aquarium.Outgame.Client
{
    /// <summary>Persistent application-wide registrations shared by all scene scopes.</summary>
    public sealed class GlobalLifetimeScope : LifetimeScope
    {
        [SerializeField] private AquariumConnectionSettings connectionSettings;
        public AquariumConnectionSettings ConnectionSettings => connectionSettings;
        public static GlobalLifetimeScope Instance { get; private set; }

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            if (connectionSettings == null)
                throw new System.InvalidOperationException("GlobalLifetimeScope requires AquariumConnectionSettings.");
            builder.RegisterInstance(connectionSettings);
            builder.Register<UnityOnlineJsonCodec>(Lifetime.Singleton).As<IOnlineJsonCodec>();
            builder.Register<HttpClientOnlineTransport>(Lifetime.Singleton).As<IOnlineTransport>();
            builder.Register<AuthoritativeAquariumGateway>(Lifetime.Singleton).As<IGateway>();
        }

        public void Configure(AquariumConnectionSettings settings)
        {
            connectionSettings = settings;
        }

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
