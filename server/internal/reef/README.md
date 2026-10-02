# Server-authoritative reef domain

This package ports the deterministic catalog and care rules from Unity's
`Assets/Aquarium/Core/AquariumSimulation.cs` and `AquariumState.cs`.
It accepts intent (`feed`, `clean`, `collect`, `adopt`), never client-created state
or timestamps. Player identity must be authenticated by a production transport;
a caller-provided player ID is only suitable for the explicitly local demo.

## Transaction and retry contract

- `Get` creates a new player at revision 1. A later server time is simulated and
  persisted, incrementing the revision. An unchanged/backwards time does neither.
- `Apply` requires a prior `Get` and the current `ExpectedRevision`. CAS is checked
  before advancing time. A stale command changes nothing, including timestamps.
- A valid accepted command advances time, executes the action, and increments the
  revision exactly once. Gameplay no-ops are accepted commands with `Success=false`.
- The client generates a unique request ID for each logical command. Retry all
  command fields unchanged. Never reuse an ID or "retry" by rewriting its expected
  revision: after a conflict, fetch current state and decide on a new command.
- The last 256 accepted command results per player are atomically stored alongside
  state. Replays return the **original** snapshot/result with `Replayed=true`, before
  consulting the clock or checking current revision. Fetch again for current state.
- After receipt eviction, an unchanged older retry fails the monotonic revision
  check; it cannot execute again. Exact response replay and ID collision detection
  are only promised within the last 256 accepted commands. There is no TTL.
- Bad arguments, ID collisions and revision conflicts are errors. Unknown catalog
  species are a gameplay failure, matching Unity. IDs are 1–128 ASCII letters,
  digits, underscores or hyphens; only adoption accepts a species ID.
- Each offline gap simulates at most eight hours, then consumes the entire gap.
  Server clock rollback preserves the timestamp high-water mark. Care is integrated
  exactly across the fullness/cleanliness crossing and stops at the care threshold.

## Local persistence boundaries

`NewFileRepository` uses SHA-256-derived player filenames and a checksummed,
versioned JSON envelope. Every load validates the schema, identity, gameplay
bounds, finite numbers, receipt ordering, and a 1 MiB maximum record size.
Corrupt or unsupported records fail closed and remain untouched. The checksum is
an accidental-corruption check, **not** protection against an attacker with disk
write access.

A single mutex serializes local transactions. A nonblocking lifetime advisory
`flock` prevents two repositories/processes from using the same directory, also
through symlink aliases. The lock is released automatically at process exit; its
file must not be deleted while any server is running. Supported hosts are Linux,
macOS and the BSD targets listed in `lock_unix.go`. Other platforms fail clearly.
Use a private directory on a local filesystem that supports atomic rename and
file/directory fsync. This is not a distributed repository and must not be placed
on a network filesystem or shared between replicas. No online migrations,
backup/restore procedure, per-player deletion, auth, or high-volume scaling is
provided here. The replay limit bounds each file; total player count and disk
usage still require an operator-controlled environment.

Writes use a mode-0600 same-directory temporary file, file fsync, atomic rename,
then directory fsync. Both state and receipts commit together. A failure before
rename leaves the previous record intact. A post-rename sync failure has an
uncertain outcome: retry the exact command to resolve it via the receipt. No
write-behind cache exists. Orphaned temporary files from an abrupt crash are never
loaded as player data; operators may clean them while the server is stopped.
A newly created data directory should be provisioned durably by the operator.

## Verification

Run `go test -race ./internal/reef`. Tests cover Unity behavior parity, partition
invariance, caps, rollback, wallet/reward/growth bounds, adoption, concurrent CAS,
concurrent duplicate requests, restart replay, bounded history, cross-player
isolation, corrupt/truncated/oversized files, schema validation, injected failures
before/after commit, cancelled transactions, cross-process locks, abrupt process
exit, and temporary-file handling.
