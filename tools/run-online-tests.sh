#!/bin/sh
# License-free, package-free .NET client -> real Go development process checks.
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
build_dir=$(mktemp -d "${TMPDIR:-/tmp}/aquarium-online-build.XXXXXX")
trap 'rm -rf "$build_dir"' EXIT HUP INT TERM
(
  cd "$root/server"
  go build -o "$build_dir/reef-local" ./cmd/reef-local
)
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet run \
  --project "$root/tools/Aquarium.Online.Tests/Aquarium.Online.Tests.csproj" \
  --configuration Release -- --server "$build_dir/reef-local"
