# server

## folder structure

https://github.com/golang-standards/project-layout/blob/master/README.md

## generate protobuf

```
buf generate
```

## install packages

```
go mod tidy
```

## make requests

e.g.

```
$ cd server
$ buf curl --schema ../api/proto/session/v1/session.proto --data '{"device_id": "mock_device_id", "public_key": "mock_public_key", "signed_device_id": "mock_signed_device_id"}' http://localhost:8080/session.v1.SessionService/Login

->
{
  "sessionToken": "dummy_token",
  "playerId": "player_,mock_device_id",
  "isNewPlayer": true
}
```
# Link

|Link|Comment|
|-|-|
|https://buf.build/docs/configuration/v2/buf-gen-yaml/|buf.gen.yaml|
|https://buf.build/docs/reference/cli/buf/#subcommands|Buf CLI Subcommands|
|https://connectrpc.com/docs/go/getting-started#make-requests|Make requests with Connect protocol|