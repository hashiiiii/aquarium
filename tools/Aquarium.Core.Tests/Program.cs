using Aquarium.Core;
using Aquarium.Runtime;
using System.Text.Json;

// No NuGet dependencies: runs the actual Unity-independent game core on .NET 8.
// Unity JsonUtility compatibility is covered separately by the EditMode suite.
var start = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
var tests = new List<(string name, Action run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < 1e-8, $"Expected {expected}, got {actual}");
AquariumState Fresh() => AquariumSimulation.CreateNew(start);

Test("Starter and immediate adoption", () => {
    var s = Fresh();
    Check(AquariumSimulation.Validate(s, out _));
    Check(s.creatures.Count == 1 && s.pearls == 30);
    Check(AquariumSimulation.Acquire(s, "moon_jelly").success);
    Check(s.pearls == 0 && s.creatures.Count == 2);
});

foreach (var levels in new[] { (75.0, 85.0), (90.0, 75.0), (30.0, 35.0), (20.0, 100.0), (100.0, 20.0) })
{
    Test($"Partition invariance {levels}", () => {
        var a = Fresh(); var b = Fresh();
        a.fullness = b.fullness = levels.Item1; a.cleanliness = b.cleanliness = levels.Item2;
        AquariumSimulation.AdvanceTo(a, start.AddHours(8));
        for (var n = 1; n <= 480; n++) AquariumSimulation.AdvanceTo(b, start.AddMinutes(n));
        Near(a.fullness, b.fullness); Near(a.cleanliness, b.cleanliness);
        Near(a.creatures[0].growthHours, b.creatures[0].growthHours);
        Near(a.creatures[0].pendingPearls, b.creatures[0].pendingPearls);
    });
}
Test("Random irregular time partition invariance", () => {
    var random = new Random(4711);
    for (var trial = 0; trial < 100; trial++) {
        var a = Fresh(); var b = Fresh();
        a.fullness = b.fullness = random.NextDouble() * 100;
        a.cleanliness = b.cleanliness = random.NextDouble() * 100;
        var duration = random.Next(1, 28800);
        AquariumSimulation.AdvanceTo(a, start.AddSeconds(duration));
        var elapsed = 0;
        while (elapsed < duration) {
            elapsed = Math.Min(duration, elapsed + random.Next(1, 321));
            AquariumSimulation.AdvanceTo(b, start.AddSeconds(elapsed));
        }
        Near(a.creatures[0].growthHours, b.creatures[0].growthHours);
        Near(a.creatures[0].pendingPearls, b.creatures[0].pendingPearls);
    }
});
Test("Cap consumes gap once", () => {
    var s = Fresh();
    var result = AquariumSimulation.AdvanceTo(s, start.AddDays(30));
    Check(result.wasCapped && result.simulatedSeconds == 28800 && result.elapsedSeconds == 30 * 86400);
    Check(s.lastUpdatedUtcTicks == start.AddDays(30).Ticks);
    Check(AquariumSimulation.AdvanceTo(s, start.AddDays(30)).simulatedSeconds == 0);
});
Test("Rollback protects high-water timestamp", () => {
    var s = Fresh(); AquariumSimulation.AdvanceTo(s, start.AddHours(1));
    var pearls = s.creatures[0].pendingPearls;
    Check(AquariumSimulation.AdvanceTo(s, start.AddHours(-1)).clockRolledBack);
    Check(s.lastUpdatedUtcTicks == start.AddHours(1).Ticks);
    AquariumSimulation.AdvanceTo(s, start.AddHours(1)); Near(pearls, s.creatures[0].pendingPearls);
});
Test("Free care recovers empty wallet without death", () => {
    var s = Fresh(); s.pearls = 0; s.fullness = s.cleanliness = 0;
    AquariumSimulation.AdvanceTo(s, start.AddHours(8));
    Check(s.creatures.Count == 1 && s.creatures[0].growthHours == 0);
    Check(AquariumSimulation.Feed(s).success && AquariumSimulation.Clean(s).success);
    AquariumSimulation.AdvanceTo(s, start.AddHours(8.25));
    Check(s.creatures[0].growthHours > 0 && s.pearls == 0);
});
Test("Care bounds and failure are nonmutating", () => {
    var s = Fresh(); s.fullness = s.cleanliness = 99;
    AquariumSimulation.Feed(s); AquariumSimulation.Clean(s);
    Check(s.fullness == 100 && s.cleanliness == 100);
    Check(!AquariumSimulation.Feed(s).success && !AquariumSimulation.Clean(s).success);
    Check(s.pearls == 30);
});
Test("Acquisition debits exactly, rejects unknown, duplicate and insufficient balance", () => {
    var s = Fresh(); s.pearls = 10;
    Check(!AquariumSimulation.Acquire(s, "moon_jelly").success && s.pearls == 10);
    Check(!AquariumSimulation.Acquire(s, "missing").success);
    s.pearls = 90;
    Check(AquariumSimulation.Acquire(s, "moon_jelly").success && s.pearls == 60);
    Check(!AquariumSimulation.Acquire(s, "moon_jelly").success && s.pearls == 60);
    Check(AquariumSimulation.Acquire(s, "coral_drake").success && s.pearls == 0 && s.creatures.Count == 3);
});
Test("Collection preserves fractions and wallet overflow", () => {
    var s = Fresh(); s.pearls = AquariumSimulation.WalletCapacity - 2; s.creatures[0].pendingPearls = 5.75;
    Check(AquariumSimulation.Collect(s).amount == 2); Near(3.75, s.creatures[0].pendingPearls);
    Check(!AquariumSimulation.Collect(s).success);
    s.pearls = 0; Check(AquariumSimulation.Collect(s).amount == 3); Near(0.75, s.creatures[0].pendingPearls);
});
Test("Long-play bounds", () => {
    var s = Fresh();
    for (var n = 1; n <= 100; n++) {
        s.fullness = s.cleanliness = 100; AquariumSimulation.AdvanceTo(s, start.AddHours(n * 8));
    }
    Check(s.creatures[0].growthHours == SpeciesCatalog.Get("tide_sprite").matureAfterCareHours);
    Check(s.creatures[0].pendingPearls == AquariumSimulation.RewardCapacity);
    Check(AquariumSimulation.Validate(s, out _));
});
Test("Reject corrupted state", () => {
    var s = Fresh(); s.version++; Check(!AquariumSimulation.Validate(s, out _)); s.version--;
    s.fullness = double.NaN; Check(!AquariumSimulation.Validate(s, out _)); s.fullness = 50;
    s.cleanliness = double.PositiveInfinity; Check(!AquariumSimulation.Validate(s, out _)); s.cleanliness = 50;
    s.lastUpdatedUtcTicks = -1; Check(!AquariumSimulation.Validate(s, out _)); s.lastUpdatedUtcTicks = start.Ticks;
    s.creatures[0].growthHours = -1; Check(!AquariumSimulation.Validate(s, out _)); s.creatures[0].growthHours = 0;
    s.creatures.Add(new CreatureState { speciesId = "tide_sprite" }); Check(!AquariumSimulation.Validate(s, out _));
    Check(!AquariumSimulation.Validate(null, out _) && !AquariumSimulation.Validate(new AquariumState(), out _));
});
Test("Actual JSON roundtrip and one-time offline progress", () => {
    var store = new MemoryStore(); var service = new AquariumSaveService(store, new Serializer());
    Check(service.Save(Fresh(), start).success);
    var result = service.Load(start.AddHours(2)); Check(result.restored && result.state.creatures[0].growthHours > 0);
    Check(service.Save(result.state, start.AddHours(2)).success);
    var again = service.Load(start.AddHours(2)); Near(result.state.creatures[0].growthHours, again.state.creatures[0].growthHours);
});
foreach (var bad in new[] { "garbage", "{}", "", "{\"version\":999}", new string('x', AquariumSaveService.MaximumSaveCharacters + 1) })
{
    Test("Invalid JSON save recovery " + Math.Min(32, bad.Length) + " chars", () => {
        var store = new MemoryStore { text = bad }; var result = new AquariumSaveService(store, new Serializer()).Load(start);
        Check(!result.restored && !string.IsNullOrEmpty(result.warning));
        Check(store.text == bad && store.writes == 0 && AquariumSimulation.Validate(result.state, out _));
    });
}
Test("Missing save starts clean", () => {
    var result = new AquariumSaveService(new MemoryStore(), new Serializer()).Load(start);
    Check(!result.restored && result.warning == null && result.state.pearls == 30);
});
Test("IO failures reported", () => {
    var service = new AquariumSaveService(new MemoryStore { fail = true }, new Serializer());
    Check(service.Load(start).warning != null); Check(!service.Save(Fresh(), start).success);
});
Test("Save rejects invalid state before write", () => {
    var store = new MemoryStore(); var s = Fresh(); s.pearls = -1;
    Check(!new AquariumSaveService(store, new Serializer()).Save(s, start).success && store.writes == 0);
});
Test("Explicit UTC required", () => {
    foreach (var bad in new[] { DateTime.SpecifyKind(start, DateTimeKind.Local), DateTime.SpecifyKind(start, DateTimeKind.Unspecified), DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) }) {
        var threw = false; try { AquariumSimulation.CreateNew(bad); } catch (ArgumentException) { threw = true; } Check(threw);
    }
});

void InTempDirectory(Action<string> run) {
    var directory = Path.Combine(Path.GetTempPath(), "aquarium-store-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { run(directory); }
    finally { Directory.Delete(directory, true); }
}
Test("File store creates directory and reads first UTF-8 save", () => InTempDirectory(directory => {
    var path = Path.Combine(directory, "nested", "aquarium.json");
    var store = new AquariumFileStore(path);
    Check(!store.TryRead(out _));
    store.Write("{\"note\":\"海の友だち\"}");
    Check(store.TryRead(out var contents) && contents == "{\"note\":\"海の友だち\"}");
    Check(!File.Exists(path + ".tmp") && !File.Exists(store.BackupPath));
}));
Test("File store atomically replaces and retains previous backup", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    store.Write("first"); store.Write("second");
    Check(store.TryRead(out var contents) && contents == "second");
    Check(File.ReadAllText(store.BackupPath) == "first" && !File.Exists(store.Path + ".tmp"));
    store.Write("third");
    Check(store.TryRead(out contents) && contents == "third");
    Check(File.ReadAllText(store.BackupPath) == "second");
}));
Test("File store rejects oversized file before read", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    using (var file = File.Create(store.Path)) file.SetLength(AquariumSaveService.MaximumSaveCharacters * 4L + 1);
    var threw = false;
    try { store.TryRead(out _); } catch (InvalidDataException) { threw = true; }
    Check(threw);
    var result = new AquariumSaveService(store, new Serializer()).Load(start);
    Check(!result.restored && result.warning != null);
    Check(new FileInfo(store.Path).Length == AquariumSaveService.MaximumSaveCharacters * 4L + 1);
}));
Test("File store preserves unreadable original bytes and removes live path", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    var original = new byte[] { 0, 255, 254, 128, 65, 13, 10, 0, 42 };
    File.WriteAllBytes(store.Path, original);
    var preserved = store.PreserveUnreadable();
    Check(preserved != null && preserved.StartsWith(store.Path + ".preserved-", StringComparison.Ordinal));
    Check(!File.Exists(store.Path) && original.SequenceEqual(File.ReadAllBytes(preserved)));
    Check(store.PreserveUnreadable() == null);
    store.Write("new save");
    Check(original.SequenceEqual(File.ReadAllBytes(preserved)));
}));
Test("Blocked temporary path reports failed save and leaves valid save intact", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    var service = new AquariumSaveService(store, new Serializer());
    Check(service.Save(Fresh(), start).success);
    var original = File.ReadAllBytes(store.Path);
    Directory.CreateDirectory(store.Path + ".tmp");
    var changed = Fresh(); changed.pearls = 900;
    Check(!service.Save(changed, start).success);
    Check(original.SequenceEqual(File.ReadAllBytes(store.Path)));
    Check(service.Load(start).state.pearls == 30);
}));
Test("Blocked backup path does not delete existing live save", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    store.Write("original");
    Directory.CreateDirectory(store.BackupPath);
    var threw = false;
    try { store.Write("replacement"); }
    catch (IOException) { threw = true; }
    catch (UnauthorizedAccessException) { threw = true; }
    Check(threw && File.ReadAllText(store.Path) == "original");
    Directory.Delete(store.BackupPath);
    store.Write("recovered");
    Check(File.ReadAllText(store.Path) == "recovered" && File.ReadAllText(store.BackupPath) == "original");
}));

Test("Recovery restores exact backup when primary is missing without automatic write", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    var serializer = new Serializer(); var expected = Fresh();
    expected.pearls = 241; expected.fullness = 47.5; expected.cleanliness = 81.25;
    expected.creatures[0].growthHours = 3.5; expected.creatures[0].pendingPearls = 12.75;
    var backup = serializer.Serialize(expected); File.WriteAllText(store.BackupPath, backup);
    var recovered = AquariumRecovery.Load(store, serializer, start);
    Check(recovered.load.restored && recovered.savingAllowed && recovered.notice != null);
    Check(serializer.Serialize(recovered.load.state) == backup);
    Check(!File.Exists(store.Path) && File.ReadAllText(store.BackupPath) == backup);
}));
Test("Recovery quarantines corrupt primary and restores valid backup without automatic write", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    var serializer = new Serializer(); var expected = Fresh(); expected.pearls = 199;
    var backup = serializer.Serialize(expected); File.WriteAllText(store.BackupPath, backup);
    var corrupt = new byte[] { 255, 0, 13, 10, 17, 42 }; File.WriteAllBytes(store.Path, corrupt);
    var recovered = AquariumRecovery.Load(store, serializer, start);
    Check(recovered.load.restored && recovered.savingAllowed && recovered.load.state.pearls == 199);
    Check(serializer.Serialize(recovered.load.state) == backup);
    var preserved = Directory.GetFiles(directory, "aquarium.json.preserved-*");
    Check(preserved.Length == 1 && File.ReadAllBytes(preserved[0]).SequenceEqual(corrupt));
    Check(!File.Exists(store.Path) && File.ReadAllText(store.BackupPath) == backup);
}));
Test("Recovery quarantines corrupt primary without backup and starts fresh without saving", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    const string corrupt = "not valid JSON"; File.WriteAllText(store.Path, corrupt);
    var recovered = AquariumRecovery.Load(store, new Serializer(), start);
    Check(!recovered.load.restored && recovered.savingAllowed && recovered.notice != null);
    Check(recovered.load.state.pearls == 30 && AquariumSimulation.Validate(recovered.load.state, out _));
    var preserved = Directory.GetFiles(directory, "aquarium.json.preserved-*");
    Check(preserved.Length == 1 && File.ReadAllText(preserved[0]) == corrupt);
    Check(!File.Exists(store.Path) && !File.Exists(store.BackupPath));
}));
Test("Recovery prefers valid primary over valid backup and changes neither file", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    var serializer = new Serializer(); var current = Fresh(); current.pearls = 300;
    var old = Fresh(); old.pearls = 150;
    var primary = serializer.Serialize(current); var backup = serializer.Serialize(old);
    File.WriteAllText(store.Path, primary); File.WriteAllText(store.BackupPath, backup);
    var recovered = AquariumRecovery.Load(store, serializer, start);
    Check(recovered.load.restored && recovered.load.state.pearls == 300 && recovered.notice == null);
    Check(File.ReadAllText(store.Path) == primary && File.ReadAllText(store.BackupPath) == backup);
    Check(Directory.GetFiles(directory).Length == 2);
}));
Test("Recovery with no files starts fresh and does not create files", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    var recovered = AquariumRecovery.Load(store, new Serializer(), start);
    Check(!recovered.load.restored && recovered.savingAllowed && recovered.notice == null && recovered.warning == null);
    Check(recovered.load.state.pearls == 30 && Directory.GetFiles(directory).Length == 0);
}));
Test("Recovery preserves unsupported newer version primary byte for byte", () => InTempDirectory(directory => {
    var store = new AquariumFileStore(Path.Combine(directory, "aquarium.json"));
    var serializer = new Serializer(); var future = Fresh(); future.version = AquariumSimulation.SaveVersion + 1;
    future.pearls = 777; var original = serializer.Serialize(future); File.WriteAllText(store.Path, original);
    var recovered = AquariumRecovery.Load(store, serializer, start);
    Check(!recovered.load.restored && recovered.savingAllowed && recovered.load.state.pearls == 30);
    var preserved = Directory.GetFiles(directory, "aquarium.json.preserved-*");
    Check(preserved.Length == 1 && File.ReadAllText(preserved[0]) == original);
    Check(!File.Exists(store.Path) && !File.Exists(store.BackupPath));
}));

var failed = 0;
foreach (var test in tests) {
    try { test.run(); Console.WriteLine("PASS " + test.name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.name + ": " + e.Message); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} checks passed.");
return failed == 0 ? 0 : 1;

sealed class Serializer : IAquariumSerializer {
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    public string Serialize(AquariumState state) => JsonSerializer.Serialize(state, Options);
    public AquariumState Deserialize(string contents) => JsonSerializer.Deserialize<AquariumState>(contents, Options);
}
sealed class MemoryStore : IAquariumSaveStore {
    public string text; public int writes; public bool fail;
    public bool TryRead(out string contents) {
        if (fail) throw new IOException("Simulated failure"); contents = text; return text != null;
    }
    public void Write(string contents) {
        if (fail) throw new IOException("Simulated failure"); text = contents; writes++;
    }
}
