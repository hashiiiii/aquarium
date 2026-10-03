# Standalone core and persistence checks

Requires the official .NET 8 SDK. No NuGet packages are used.

```sh
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet run --project tools/Aquarium.Core.Tests/Aquarium.Core.Tests.csproj
```

The console runner compiles the actual `unity/Assets/Aquarium/Core/*.cs` sources and the actual engine-independent `Runtime/AquariumFileStore.cs` and `Runtime/AquariumRecovery.cs`, then checks deterministic time partitions, offline cap and rollback, gentle care recovery, currency/acquisition bounds, validation, JSON roundtrip and storage failures. File-store checks use fresh temporary directories and cover UTF-8 IO, atomic replacement and previous-save backup, bounded reads, byte-preserving quarantine, and failure/recovery when temporary or backup destinations are inaccessible. Nonzero exit means failure. Recovery regressions cover a missing or corrupt primary with a valid backup, corrupt or future-version primary preservation, primary preference, and fresh initialization. Recovery checks assert that loading never writes a replacement save. Temporary directories are cleaned after each check.

In a restricted environment with a read-only home, additionally set `DOTNET_CLI_HOME` to a writable temporary directory.

This does **not** run Unity or validate scene rendering, input, Player builds, or Unity's JSON serializer. Run the separate EditMode tests in Unity 6000.3.2f1 for `JsonUtility` compatibility.
