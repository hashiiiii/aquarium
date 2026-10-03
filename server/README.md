# server

## First Reef: ローカル開発用のサーバー権威型ゲーム API

新しい実装は `cmd/reef-local` です。Go + Protocol Buffers + Connect RPC という既存の構成を維持し、
育成・所持 pearls・仲間・時間進行をサーバーで計算し、プレイヤー別に永続化します。
有料サービス、MySQL、Redis、Unity Editor は起動に不要です。

起動方法、RPC の例、保存の制約、Unity 接続方針は [First Reef サーバーガイド](docs/first-reef.md) を参照してください。

**開発専用です。** `--dev` が必須で、数値ループバックアドレスだけに bind します。
開発用の player ヘッダーは本人認証ではありません。ポート転送、公開プロキシ、外部公開には使わないでください。
Unity の First Reef は現時点ではオフライン試作のままで、この API への接続は別工程です。

### 既存の認証・インフラ試作について

以下のメモと `cmd/server` は以前の未完成試作です。Login は dummy token を返し、
authorization interceptor は JWT を検証していません。**本番・共有環境には利用できません。**
新しい `reef-local` はこの Login / JWT / MySQL ping を公開しません。
`docs/login.md` 等は将来の設計案であり、実装済みの機能一覧ではありません。

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

|Link| Comment |
|-|-|
|https://buf.build/docs/configuration/v2/buf-gen-yaml/|buf.gen.yaml|
|https://buf.build/docs/reference/cli/buf/#subcommands|Buf CLI Subcommands|
|https://connectrpc.com/docs/go/getting-started#make-requests|Make requests with Connect protocol|
|https://qiita.com/_ken_/items/8292c23a5c3236af14ab|How to ensure a type satisfies a specific interface at compile time|
