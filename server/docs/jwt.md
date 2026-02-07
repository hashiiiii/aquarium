# JWT 実装ガイド（Go 標準パッケージ）

HS256 による JWT の発行・検証を Go 標準パッケージのみで実装する。

---

## 1. JWT の発行（Sign）

- ヘッダー `{"alg":"HS256","typ":"JWT"}` を `encoding/json` でマーシャル
- ペイロード `{"sub","device_id","iat","exp"}` を `encoding/json` でマーシャル
- それぞれを `encoding/base64` の `RawURLEncoding` でエンコード
- `header.payload` を `.` で結合
- `crypto/hmac` に共通鍵と `crypto/sha256` を渡して HMAC-SHA256 署名を生成
- 署名も `RawURLEncoding` でエンコード
- `header.payload.signature` を返す

## 2. JWT の検証（Verify）

- `.` で 3 パートに分割（`strings.SplitN`）
- `header.payload` に対して、秘密鍵で HMAC-SHA256 を再計算
- `hmac.Equal` で署名と比較（タイミング攻撃耐性あり）
- ペイロードを Base64url デコード → JSON デコード
- `exp` を `time.Now().Unix()` と比較して期限切れチェック

## 3. 鍵ローテーション対応

- 秘密鍵を複数保持できる構造にする（例: `[][]byte`）
- **発行**: 常に最新の鍵で署名
- **検証**: 全鍵で順に検証し、いずれかで通れば OK
- 鍵の追加・削除は環境変数やシークレット管理で行う

## 4. 使用するパッケージ一覧

| パッケージ | 用途 |
|-----------|------|
| `encoding/json` | ヘッダー・ペイロードのシリアライズ |
| `encoding/base64` | Base64url エンコード/デコード |
| `crypto/hmac` | HMAC 署名の生成・比較 |
| `crypto/sha256` | SHA-256 ハッシュ |
| `time` | `iat` / `exp` の生成・検証 |
| `strings` | トークンの結合・分割 |
| `errors` | エラー定義 |

外部パッケージ・準公式パッケージ（`golang.org/x/...`）は不要。すべて標準パッケージで完結する。
