using System;
using System.IO;
using Aquarium.Core;
using NUnit.Framework;
using UnityEngine;

namespace Aquarium.Tests
{
    public sealed class AquariumCoreTests
    {
        private static readonly DateTime Start = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void NewAquariumHasOneValidStarter()
        {
            var state = AquariumSimulation.CreateNew(Start);
            Assert.That(AquariumSimulation.Validate(state, out _), Is.True);
            Assert.That(state.creatures.Count, Is.EqualTo(1));
            Assert.That(state.creatures[0].speciesId, Is.EqualTo("tide_sprite"));
            Assert.That(state.pearls, Is.EqualTo(30));
        }

        [TestCase(75, 85)]
        [TestCase(90, 75)]
        [TestCase(30, 35)]
        public void ElapsedPartitionDoesNotChangeOutcome(double fullness, double cleanliness)
        {
            var whole = AquariumSimulation.CreateNew(Start);
            var split = AquariumSimulation.CreateNew(Start);
            whole.fullness = split.fullness = fullness;
            whole.cleanliness = split.cleanliness = cleanliness;
            AquariumSimulation.AdvanceTo(whole, Start.AddHours(8));
            for (var i = 1; i <= 480; i++) AquariumSimulation.AdvanceTo(split, Start.AddMinutes(i));
            Assert.That(split.fullness, Is.EqualTo(whole.fullness).Within(1e-9));
            Assert.That(split.cleanliness, Is.EqualTo(whole.cleanliness).Within(1e-9));
            Assert.That(split.creatures[0].growthHours, Is.EqualTo(whole.creatures[0].growthHours).Within(1e-9));
            Assert.That(split.creatures[0].pendingPearls, Is.EqualTo(whole.creatures[0].pendingPearls).Within(1e-9));
        }

        [Test]
        public void OfflineGapIsCappedAndCannotBeReplayed()
        {
            var state = AquariumSimulation.CreateNew(Start);
            var result = AquariumSimulation.AdvanceTo(state, Start.AddDays(7));
            Assert.That(result.simulatedSeconds, Is.EqualTo(8 * 3600));
            Assert.That(result.wasCapped, Is.True);
            Assert.That(state.lastUpdatedUtcTicks, Is.EqualTo(Start.AddDays(7).Ticks));
            Assert.That(AquariumSimulation.AdvanceTo(state, Start.AddDays(7)).simulatedSeconds, Is.Zero);
        }

        [Test]
        public void RollbackDoesNotRegressTimestampOrDuplicateProgress()
        {
            var state = AquariumSimulation.CreateNew(Start);
            AquariumSimulation.AdvanceTo(state, Start.AddHours(1));
            var earned = state.creatures[0].pendingPearls;
            Assert.That(AquariumSimulation.AdvanceTo(state, Start).clockRolledBack, Is.True);
            Assert.That(state.lastUpdatedUtcTicks, Is.EqualTo(Start.AddHours(1).Ticks));
            AquariumSimulation.AdvanceTo(state, Start.AddHours(1));
            Assert.That(state.creatures[0].pendingPearls, Is.EqualTo(earned));
        }

        [Test]
        public void LowCarePausesWithoutDeathAndFreeCareCanRecover()
        {
            var state = AquariumSimulation.CreateNew(Start);
            state.pearls = 0;
            state.fullness = state.cleanliness = 0;
            AquariumSimulation.AdvanceTo(state, Start.AddHours(8));
            Assert.That(state.creatures[0].growthHours, Is.Zero);
            Assert.That(state.creatures.Count, Is.EqualTo(1));
            Assert.That(AquariumSimulation.Feed(state).success, Is.True);
            Assert.That(AquariumSimulation.Clean(state).success, Is.True);
            AquariumSimulation.AdvanceTo(state, Start.AddHours(8.25));
            Assert.That(state.creatures[0].growthHours, Is.GreaterThan(0));
            Assert.That(state.pearls, Is.Zero);
        }

        [Test]
        public void FreeCareClampsAndFullCareFailsWithoutMutation()
        {
            var state = AquariumSimulation.CreateNew(Start);
            state.fullness = state.cleanliness = 99;
            AquariumSimulation.Feed(state);
            AquariumSimulation.Clean(state);
            Assert.That(state.fullness, Is.EqualTo(100));
            Assert.That(state.cleanliness, Is.EqualTo(100));
            Assert.That(AquariumSimulation.Feed(state).success, Is.False);
            Assert.That(AquariumSimulation.Clean(state).success, Is.False);
            Assert.That(state.pearls, Is.EqualTo(30));
        }

        [Test]
        public void AcquisitionCostsExactlyAndRejectsDuplicatesAndUnknownSpecies()
        {
            var state = AquariumSimulation.CreateNew(Start);
            state.pearls = 10;
            Assert.That(AquariumSimulation.Acquire(state, "moon_jelly").success, Is.False);
            Assert.That(state.pearls, Is.EqualTo(10));
            Assert.That(AquariumSimulation.Acquire(state, "missing").success, Is.False);
            state.pearls = 90;
            Assert.That(AquariumSimulation.Acquire(state, "moon_jelly").success, Is.True);
            Assert.That(state.pearls, Is.EqualTo(60));
            Assert.That(AquariumSimulation.Acquire(state, "moon_jelly").success, Is.False);
            Assert.That(AquariumSimulation.Acquire(state, "coral_drake").success, Is.True);
            Assert.That(state.pearls, Is.Zero);
            Assert.That(state.creatures.Count, Is.EqualTo(AquariumSimulation.Capacity));
        }

        [Test]
        public void CollectionRetainsFractionAndWalletOverflow()
        {
            var state = AquariumSimulation.CreateNew(Start);
            state.pearls = AquariumSimulation.WalletCapacity - 2;
            state.creatures[0].pendingPearls = 5.75;
            Assert.That(AquariumSimulation.Collect(state).amount, Is.EqualTo(2));
            Assert.That(state.creatures[0].pendingPearls, Is.EqualTo(3.75));
            Assert.That(AquariumSimulation.Collect(state).success, Is.False);
            state.pearls = 0;
            Assert.That(AquariumSimulation.Collect(state).amount, Is.EqualTo(3));
            Assert.That(state.creatures[0].pendingPearls, Is.EqualTo(0.75));
        }

        [Test]
        public void GrowthAndRewardsStayBoundedAfterLongPlay()
        {
            var state = AquariumSimulation.CreateNew(Start);
            for (var i = 1; i <= 100; i++)
            {
                state.fullness = state.cleanliness = 100;
                AquariumSimulation.AdvanceTo(state, Start.AddHours(i * 8));
            }
            Assert.That(state.creatures[0].growthHours, Is.EqualTo(SpeciesCatalog.Get("tide_sprite").matureAfterCareHours));
            Assert.That(state.creatures[0].pendingPearls, Is.EqualTo(AquariumSimulation.RewardCapacity));
            Assert.That(AquariumSimulation.Validate(state, out _), Is.True);
        }

        [Test]
        public void ValidationRejectsUnsupportedNonfiniteDuplicateAndMissingData()
        {
            var state = AquariumSimulation.CreateNew(Start);
            state.version++;
            Assert.That(AquariumSimulation.Validate(state, out _), Is.False);
            state.version--;
            state.fullness = double.NaN;
            Assert.That(AquariumSimulation.Validate(state, out _), Is.False);
            state.fullness = 50;
            state.creatures.Add(new CreatureState { speciesId = "tide_sprite" });
            Assert.That(AquariumSimulation.Validate(state, out _), Is.False);
            Assert.That(AquariumSimulation.Validate(new AquariumState(), out _), Is.False);
            Assert.That(AquariumSimulation.Validate(null, out _), Is.False);
        }

        [Test]
        public void SaveRoundtripPreservesFieldsAndAppliesOfflineProgressOnce()
        {
            var store = new MemoryStore();
            var service = new AquariumSaveService(store, new JsonSerializer());
            var state = AquariumSimulation.CreateNew(Start);
            Assert.That(service.Save(state, Start).success, Is.True);
            var loaded = service.Load(Start.AddHours(2));
            Assert.That(loaded.restored, Is.True);
            Assert.That(loaded.state.creatures[0].growthHours, Is.GreaterThan(0));
            Assert.That(service.Save(loaded.state, Start.AddHours(2)).success, Is.True);
            var again = service.Load(Start.AddHours(2));
            Assert.That(again.state.creatures[0].growthHours, Is.EqualTo(loaded.state.creatures[0].growthHours).Within(1e-9));
        }

        [TestCase("garbage")]
        [TestCase("{}")]
        [TestCase("")]
        [TestCase("{\"version\":999}")]
        public void InvalidSaveRecoversWithoutOverwritingOriginal(string bad)
        {
            var store = new MemoryStore { text = bad };
            var result = new AquariumSaveService(store, new JsonSerializer()).Load(Start);
            Assert.That(result.restored, Is.False);
            Assert.That(result.warning, Is.Not.Null.And.Not.Empty);
            Assert.That(store.text, Is.EqualTo(bad));
            Assert.That(store.writes, Is.Zero);
            Assert.That(AquariumSimulation.Validate(result.state, out _), Is.True);
        }

        [Test]
        public void StorageFailuresAreReported()
        {
            var store = new MemoryStore { fail = true };
            var service = new AquariumSaveService(store, new JsonSerializer());
            Assert.That(service.Load(Start).warning, Is.Not.Null);
            Assert.That(service.Save(AquariumSimulation.CreateNew(Start), Start).success, Is.False);
        }

        [Test]
        public void UnspecifiedLocalOrZeroTimestampsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => AquariumSimulation.CreateNew(DateTime.SpecifyKind(Start, DateTimeKind.Unspecified)));
            Assert.Throws<ArgumentException>(() => AquariumSimulation.CreateNew(DateTime.SpecifyKind(Start, DateTimeKind.Local)));
            Assert.Throws<ArgumentException>(() => AquariumSimulation.CreateNew(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)));
        }

        private sealed class JsonSerializer : IAquariumSerializer
        {
            public string Serialize(AquariumState state) => JsonUtility.ToJson(state);
            public AquariumState Deserialize(string text) => JsonUtility.FromJson<AquariumState>(text);
        }

        private sealed class MemoryStore : IAquariumSaveStore
        {
            public string text;
            public int writes;
            public bool fail;
            public bool TryRead(out string contents)
            {
                if (fail) throw new IOException("simulated read failure");
                contents = text;
                return text != null;
            }
            public void Write(string contents)
            {
                if (fail) throw new IOException("simulated write failure");
                text = contents;
                writes++;
            }
        }
    }
}
