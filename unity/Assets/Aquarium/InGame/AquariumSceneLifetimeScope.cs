using Aquarium.Outgame.Client;
using Aquarium.Runtime;
using VContainer;
using VContainer.Unity;

namespace Aquarium.Outgame.Client
{
    /// <summary>Composition root for the existing online aquarium scene.</summary>
    public sealed class AquariumSceneLifetimeScope : LifetimeScope
    {
        protected override void Awake()
        {
            if (GlobalLifetimeScope.Instance != null)
                parentReference.Object = GlobalLifetimeScope.Instance;
            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<AquariumOnlineSessionFactory>(Lifetime.Scoped).As<IAquariumOnlineSessionFactory>();
            builder.RegisterComponentInHierarchy<OnlineAquariumGame>();
        }

        public void UseGlobalParent()
        {
            parentReference = ParentReference.Create<GlobalLifetimeScope>();
        }
    }
}
