# Live online integration checks

This license-free .NET 8 console suite compiles the **actual** Unity-independent
`Assets/Aquarium/Online/*.cs` session, Connect/JSON API client, `HttpClient`
transport, field DTOs, validation and durable pending-command store. It starts a
separate, real Go `reef-local` executable and sends HTTP requests to it. No NuGet
packages or Unity license are required.

## Run

Requirements: Go version from `server/go.mod`, .NET 8 SDK, Linux or macOS/BSD
supported by the development server's file lock, and **free 127.0.0.1:8081**.

From the repository root:

```sh
sh tools/run-online-tests.sh
```

The script builds `server/cmd/reef-local` into a fresh temporary directory and
runs the suite in Release configuration. Nonzero exit means failure. It requires
a free fixed port because it exercises the shipping development entry point and
its exact numeric Host guard. If occupied, it fails with instructions. It never
reuses, stops, or kills an existing process.

To use an already built executable:

```sh
cd server
go build -o /tmp/aquarium-reef-local ./cmd/reef-local
cd ..
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
  dotnet run --project tools/Aquarium.Online.Tests/Aquarium.Online.Tests.csproj \
  --configuration Release -- --server /tmp/aquarium-reef-local
```

For a read-only home, set `DOTNET_CLI_HOME` and `NUGET_PACKAGES` to writable
scratch directories. Each run uses its own absolute server data directory and
client journal paths, including through real process restarts. The fixture kills
only the child process it created and removes its temporary files on completion.

## Coverage

The 21 named checks cover:

- Live catalog, initial state, protobuf-JSON string int64/uint64 fields, and
  intent-only requests using the actual .NET `HttpClient` transport
- Feed, clean, adoption, insufficient funds, duplicate adoption, collection
  no-op, and positive whole-pearl collection preserving fractions
- Lost successful adoption and positive-collection replies after the real
  server applied them, exact journal bytes, new client/session and actual Go
  process restart, persisted receipt replay, and no double debit or credit
- Two clients producing a real revision conflict; refreshed snapshot and a new
  request ID only after an explicit fresh action
- Receipt replay returning an older genuine server snapshot without observable
  revision rollback, followed by a fresh current snapshot
- Disconnection/reconnection without local reward simulation or wallet changes
- Cancellation both before transport send and after actual server commit;
  identical safe retry in both cases
- In-flight double clicks rejected without queuing extra commands
- Empty, corrupt and oversized journals preserved without mutation; real filesystem
  write and acknowledgement-clear failures; recovery through exact retry
- Journal endpoint/player binding and exclusive writer lease/reopening
- Invalid endpoint/identity validation, real network identity rejection, and
  browser-origin rejection
- Malformed successful response after real commit retaining uncertain intent
- Fresh lower revision after an offline reset of the fixture server's own save
  failing closed while preserving the client's newer snapshot

`FaultTransport` is a decorator around the real transport. It withholds or
corrupts a response **after** receiving it from the Go server, and gates sends for
repeat-click/cancellation checks. It does not substitute a fake gameplay server.

For positive collection without a minutes-long test delay, two fixtures are
checksummed, valid returning-player saves dated one hour before the run. They
are created only in the isolated data directory before the server starts.
Production repository validation and the unmodified server `time.Now` path then
advance them. There is no production clock switch, hidden RPC or test backdoor.
The fresh-revision regression test similarly archives only its own fixture save
while the test's server is stopped, then restarts the real server.

## What this does not prove

The test-only codec uses `System.Text.Json` with `IncludeFields = true` to match
the public-field DTO shape. This does **not** exercise Unity `JsonUtility`, Unity
assembly import, Mono/IL2CPP, input/UI wiring, rendering, or a player build.
Those require the Unity EditMode/PlayMode and manual acceptance checks in the
online development guide. This server remains insecure local development only,
with no public authentication or deployment guarantee.

CI: `.github/workflows/aquarium-online.yml` runs the same shell command on an
isolated Ubuntu runner whenever client, server, schema or test sources change.
