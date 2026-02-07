# JWT 実装ガイド（Go 標準パッケージ）

HS256 による JWT の発行・検証を Go 標準パッケージのみで実装する。

---

## 1. パッケージ設計

`pkg/jwt` は汎用パッケージとして、JWT の構造・署名・検証のみを担う。ドメイン固有のロジックは呼び出し側（`internal/`）に持たせる。

### 1.1 責務の分離

| 層 | 責務 | 知っていること | 知らないこと |
|----|------|---------------|-------------|
| `pkg/jwt` | JWT の署名・検証 | JWT の構造、HS256 署名、Base64url、`exp` 検証、鍵ローテーション | `device_id` の意味、ユーザーモデル、Redis |
| `internal/handler/` (login) | 認証・トークン発行 | ペイロードに何を入れるか、ユーザー特定ロジック | HMAC の計算方法 |
| `internal/handler/interceptor/` | 認可 | `device_id` の照合ロジック、Redis/DB アクセス | JWT の内部構造 |

### 1.2 インターフェース

```go
// pkg/jwt — ドメイン知識を持たない
package jwt

type Claims map[string]any

func Sign(claims Claims, secret []byte) (string, error)
func Verify(token string, secrets [][]byte) (Claims, error)
```

### 1.3 呼び出し側の使い方

```go
// internal/handler/login.go（認証 — トークン発行）
claims := jwt.Claims{
    "sub":       userID,
    "device_id": deviceID,
    "iat":       time.Now().Unix(),
    "exp":       time.Now().Add(7 * 24 * time.Hour).Unix(),
}
token, err := jwt.Sign(claims, secret)
```

```go
// internal/handler/interceptor/authorization/（認可 — トークン検証）
claims, err := jwt.Verify(token, secretKeys)
// claims から device_id を取り出し、Redis/DB の値と照合
```

> **ポイント**: `pkg/jwt` はペイロードの中身を関知しない。`sub` や `device_id` といったフィールドの意味を知るのは `internal/` の責務。

---

## 2. JWT の発行（Sign）

- ヘッダー `{"alg":"HS256","typ":"JWT"}` を `encoding/json` でマーシャル
- ペイロード（任意の `Claims`）を `encoding/json` でマーシャル
- それぞれを `encoding/base64` の `RawURLEncoding` でエンコード
- `header.payload` を `.` で結合
- `crypto/hmac` に共通鍵と `crypto/sha256` を渡して HMAC-SHA256 署名を生成
- 署名も `RawURLEncoding` でエンコード
- `header.payload.signature` を返す

## 3. JWT の検証（Verify）

- `.` で 3 パートに分割（`strings.SplitN`）
- `header.payload` に対して、秘密鍵で HMAC-SHA256 を再計算
- `hmac.Equal` で署名と比較（タイミング攻撃耐性あり）
- ペイロードを Base64url デコード → JSON デコード
- `exp` を `time.Now().Unix()` と比較して期限切れチェック

## 4. 鍵ローテーション対応

- 秘密鍵を複数保持できる構造にする（`[][]byte`）
- **発行**: 常に最新の鍵（スライスの先頭）で署名
- **検証**: 全鍵で順に検証し、いずれかで通れば OK
- 鍵の追加・削除は環境変数やシークレット管理で行う

## 5. 使用するパッケージ一覧

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
