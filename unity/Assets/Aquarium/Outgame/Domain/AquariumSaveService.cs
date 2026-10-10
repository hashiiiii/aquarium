using System;

namespace Aquarium.Core
{
    public interface IAquariumSaveStore
    {
        bool TryRead(out string contents);
        void Write(string contents);
    }

    public interface IAquariumSerializer
    {
        string Serialize(AquariumState state);
        AquariumState Deserialize(string contents);
    }

    public sealed class LoadResult
    {
        public AquariumState state;
        public bool restored;
        public string warning;
        public AdvanceResult advance;
    }

    /// <summary>
    /// Persistence boundary; the host supplies its JSON and disk adapters. Invalid/unsupported
    /// saves are never overwritten by Load. The host should preserve them before explicitly saving.
    /// </summary>
    public sealed class AquariumSaveService
    {
        private readonly IAquariumSaveStore store;
        private readonly IAquariumSerializer serializer;
        public const int MaximumSaveCharacters = 65536;

        public AquariumSaveService(IAquariumSaveStore store, IAquariumSerializer serializer)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public LoadResult Load(DateTime utcNow)
        {
            AquariumSimulation.RequireUtc(utcNow);
            try
            {
                if (!store.TryRead(out var contents))
                    return new LoadResult { state = AquariumSimulation.CreateNew(utcNow) };
                if (string.IsNullOrWhiteSpace(contents) || contents.Length > MaximumSaveCharacters)
                    return Fresh(utcNow, "Save is empty or too large.");
                var state = serializer.Deserialize(contents);
                if (!AquariumSimulation.Validate(state, out var error)) return Fresh(utcNow, error);
                return new LoadResult
                {
                    state = state, restored = true,
                    advance = AquariumSimulation.AdvanceTo(state, utcNow)
                };
            }
            catch (Exception exception)
            {
                return Fresh(utcNow, "Could not load the saved aquarium (" + exception.GetType().Name + ").");
            }
        }

        public ActionResult Save(AquariumState state, DateTime utcNow)
        {
            AquariumSimulation.RequireUtc(utcNow);
            if (!AquariumSimulation.Validate(state, out var error)) return new ActionResult(false, error);
            AquariumSimulation.AdvanceTo(state, utcNow);
            try
            {
                var contents = serializer.Serialize(state);
                if (string.IsNullOrWhiteSpace(contents) || contents.Length > MaximumSaveCharacters)
                    return new ActionResult(false, "Serialized save is empty or too large.");
                store.Write(contents);
                return new ActionResult(true, "Aquarium saved.");
            }
            catch (Exception exception)
            {
                return new ActionResult(false, "Could not save the aquarium (" + exception.GetType().Name + ").");
            }
        }

        private static LoadResult Fresh(DateTime utcNow, string warning)
        {
            return new LoadResult { state = AquariumSimulation.CreateNew(utcNow), warning = warning };
        }
    }
}
