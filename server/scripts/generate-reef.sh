#!/bin/sh
# Generate only First Reef. No registry access is needed after the three local
# tools have been installed: buf, protoc-gen-go, and protoc-gen-connect-go.
set -eu

server_dir=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
schema_dir=$(mktemp -d "${TMPDIR:-/tmp}/aquarium-proto.XXXXXX")
trap 'rm -rf "$schema_dir"' EXIT HUP INT TERM

# Keep the source-relative path stable in generated descriptors, while avoiding
# api/buf.yaml AND api/buf.lock (Buf can load locked deps even with a config override).
mkdir -p "$schema_dir/aquarium/v1"
cp "$server_dir/../api/proto/aquarium/v1/aquarium.proto" "$schema_dir/aquarium/v1/aquarium.proto"

cd "$server_dir"
buf lint "$schema_dir" --config buf.reef.yaml
buf format "$schema_dir" --diff --exit-code
buf generate "$schema_dir" --config buf.reef.yaml --template buf.reef.gen.yaml
