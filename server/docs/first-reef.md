# First Reef サーバー開発ガイド

## このマイルストーンの境界

既存の Go + Protocol Buffers + Connect RPC にゲーム処理を追加した、**ローカル開発用**サーバーです。
新しい entry point は `cmd/reef-local`。以前の `cmd/server` のダミーログイン、未検証 JWT、
MySQL 接続試作とは切り離しています。有料サービスやクラウドへの deploy は不要です。

サーバーが authoritative な値:

- fullness / cleanliness、成長量、未回収 pearls、所持 pearls、所持する仲間
- 進行に利用する時刻、8時間のオフライン上限、ゲームルールと種族カタログ
- 状態 revision と、再送された操作の結果

クライアントは「給餌したい」「掃除したい」「回収したい」「この種を迎えたい」という intent のみ送信します。
残高、報酬、成長量、時刻、player ID を RPC body で指定することはできません。

## 起動

Go 1.26.x が必要です。リポジトリルートから:

```sh
cd server
go mod download
go run ./cmd/reef-local --dev --data-dir .reef-data
```

`--dev` なしでは起動しません。127.0.0.1:8081 にのみ bind します。
`X-Aquarium-Dev-Player` ヘッダーでローカルテスト用のプレイヤーを選択します。
これは本人認証ではありません。同じコンピューター上のプロセスは任意の開発プレイヤーを選べます。
外部公開、SSH port forwarding、公開 reverse proxy、実プレイヤーのデータには使用しないでください。
ブラウザー Origin と不正な Host は拒否し、CORS は許可しません。

停止は Ctrl+C。保存先は process のカレントディレクトリ基準です。
起動ごとに同じ絶対 `--data-dir` を指定すると、作業ディレクトリの違いによる別セーブの誤作成を避けられます。

## API

スキーマ: [`api/proto/aquarium/v1/aquarium.proto`](../../api/proto/aquarium/v1/aquarium.proto)。
生成済み Go client/server は `server/gen/aquarium/v1/` に含まれます。
Connect protobuf / JSON、および gRPC / gRPC-Web に対応する unary RPC です。

### 現在状態

```sh
curl --fail-with-body http://127.0.0.1:8081/aquarium.v1.AquariumService/GetAquarium \
  -H 'Content-Type: application/json' \
  -H 'Connect-Protocol-Version: 1' \
  -H 'X-Aquarium-Dev-Player: alice' \
  -d '{}'
```

初回は Tide Sprite 1体、30 pearls、fullness 75、cleanliness 85 から始まります。
返された `state.revision` を次の操作の `expectedRevision` に使用します。
protobuf JSON の uint64 は精度を失わないよう文字列です。JavaScript の浮動小数点 Number に変換しないでください。

### 操作と再送

GetAquarium で得た revision を指定して給餌する例:

```sh
curl --fail-with-body http://127.0.0.1:8081/aquarium.v1.AquariumService/ApplyCommand \
  -H 'Content-Type: application/json' \
  -H 'Connect-Protocol-Version: 1' \
  -H 'X-Aquarium-Dev-Player: alice' \
  -d '{"requestId":"feed-001","expectedRevision":"1","action":"ACTION_FEED"}'
```

`expectedRevision` の `"1"` は例です。直前のレスポンスの値に置き換えてください。
操作は `ACTION_FEED` / `ACTION_CLEAN` / `ACTION_COLLECT` / `ACTION_ADOPT`。
`ACTION_ADOPT` のみ `"speciesId":"moon_jelly"` 等が必要です。
カタログ取得は同じヘッダーと `{}` を `/aquarium.v1.AquariumService/GetCatalog` に POST します。

新しい操作ごとに UUID 等の一意な `requestId` を生成します。
ネットワークエラーによる再送では、**requestId / expectedRevision / action / speciesId を全部そのまま**送ります。
revision 不一致では最新状態を Get し、ユーザーの意図を確認して新しい操作 ID で送ります。
同じ ID を別の内容に再利用してはいけません。

直近256件の操作結果は状態と一緒に保存され、再起動後も同じ結果を返します。
この範囲を超える古い再送は、以前の revision のままなら競合として拒否されるため、二重の購入/報酬にはなりません。
リプレイは元の snapshot を返すので、クライアントは新しい revision を古い snapshot で巻き戻さず、必要に応じて Get してください。

ゲーム上の不成立（満腹、残高不足、既に所持、回収可能な整数 pearls なし等）は通常の操作結果です。
スキーマ/識別子の不正、revision 競合、同一 ID の内容変更、保存失敗は RPC error と区別します。

## ルール

Unity の First Reef 試作と同じ初期ルールです:

- fullness は毎時6、cleanliness は毎時3減少。給餌 +25、掃除 +30、どちらも最大100で無料
- 両方が20より大きい間、`min(fullness, cleanliness) / 100` を時間積分して育成・報酬を計算
- Care が低ければ育成停止。死亡や pearls 没収はなし
- Tide Sprite: 0 pearls / 8 pearls per care-hour / 12 care-hours で成熟
- Moon Jelly: 30 / 10 / 16、Coral Drake: 60 / 14 / 24
- 同種は1体、合計3体。各体の未回収 bank は100、wallet は9999
- 回収は各体の bank の整数部分のみ。端数と wallet 上限による未回収分を保持
- 一回の経過計算は最大8時間。長い離席の残りを次の request で再回収することはできない
- サーバー時計が戻った場合、以前の high-water timestamp まで追加進行なし

## 永続化と制限

単一プロセス・ローカル filesystem 用の JSON repository です。プレイヤー状態と操作 receipt を同じ transaction で保存します。
file lock は Linux / macOS / BSD の `flock` を使います。実行検証は Linux で行っています。
それ以外の OS では安全に起動を拒否します。
一時ファイルへの書き込みと rename による置換を使い、保存失敗を成功として応答しません。
rename 後の disk sync に失敗した場合、結果は不確定として返します。同一 request を再送すると receipt で確定済みの結果を照会できます。
ファイルはサイズ上限・schema/state 検証・SHA-256 checksum で破損を検出します。checksum は不正改ざんの認証ではありません。
同じ保存先を複数サーバーで開くことは lock で拒否します。
壊れた保存や非対応 version を、新規データとして上書きしません。元ファイルを保存し、停止して原因を調べてください。

バックアップはサーバー停止後に data directory 全体をコピーしてください。
OS/ディスク障害に対する保証は filesystem の durability に依存します。
network filesystem、複数レプリカ、production 用 DB/バックアップ/マイグレーションは対象外です。
MySQL へ移行する場合も、状態 revision と idempotency receipt を同一 DB transaction で更新する必要があります。

## Unity への接続

Unity の **Aquarium → Open Online First Reef** から、別シーンの開発用オンラインクライアントを起動できます。
[オンラインガイド](../../docs/online-first-reef.md)を参照してください。
Connect unary JSON を使用し、DTO と JSON codec / HTTP transport を分離しています。

- GetCatalog / GetAquarium のサーバー snapshot から表示を作成
- ApplyCommand の request ID・revision・payload を送信前に記録し、通信結果が不明な場合は同じ操作を再送
- revision 競合時は最新状態へ更新して、次の操作をユーザーが選び直す
- 古い replay snapshot による巻き戻しを防止
- オンラインシーンではローカル AdvanceTo / 保存ファイルからの報酬加算を行わない
- 切断時は状態と操作を保留して再接続。オフライン save の upload は行わない

オフラインデモも独立して残しています。オンライン接続は同じマシン上の Editor / desktop 開発ビルド向けです。
実機 / WebGL / 本番サービス向けの transport・認証・公開 endpoint は別途必要です。

開発 player ヘッダーを本番クライアントに組み込まないでください。
公開前に、鍵の所有証明と replay-resistant な login、検証済み session、account recovery、TLS、rate limiting、
DB transaction/backup、運用監視、Unity 結合テストが別途必要です。

## 検証

```sh
cd server
go test -race -count=1 ./...
go vet ./...
go build ./...
```

生成コードの再現性確認（既存の Session API の生成物には触れない）:

```sh
go install github.com/bufbuild/buf/cmd/buf@v1.66.0
go install google.golang.org/protobuf/cmd/protoc-gen-go@v1.36.11
go install connectrpc.com/connect/cmd/protoc-gen-connect-go@v1.19.1
export PATH="$(go env GOPATH)/bin:$PATH"
go run ./cmd/reef-codegen
git diff --exit-code -- gen/aquarium
```

生成スクリプトは First Reef の schema のみを一時ディレクトリへコピーし、旧 API の Buf registry 依存から分離して lint / format / generate します。
上記ツールのインストール後、生成自体に registry 接続は不要です。
CI は server 全体の race tests / vet / build と First Reef 生成コードの一致を検証します。
Unity 実機、production 認証、DB、公開 deployment の検証ではありません。
