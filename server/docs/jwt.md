# JWT 実装ガイド（Go 標準パッケージ）

HS256 による JWT の発行・検証を Go 標準パッケージのみで実装する。

---

## 1. パッケージ設計

`pkg/jwt` は汎用パッケージとして、JWT の構造・署名・検証のみを担う。ドメイン固有のロジックは呼び出し側（`internal/`）に持たせる。

### 1.1 責務の分離

| 層 | 責務 | 知っていること | 知らないこと |
|----|------|---------------|-------------|
| `pkg/jwt` | JWT の署名・検証 | JWT の構造、HS256 署名、Base64url、`exp` 検証 | `device_id` の意味、ユーザーモデル、Redis |
| `internal/handler/` (login) | 認証・トークン発行 | ペイロードに何を入れるか、ユーザー特定ロジック | HMAC の計算方法 |
| `internal/handler/interceptor/` | 認可 | `device_id` の照合ロジック、Redis/DB アクセス | JWT の内部構造 |

### 1.2 インターフェース

```go
// pkg/jwt — ドメイン知識を持たない
package jwt

type Claims map[string]any

func Sign(claims Claims, key []byte) (string, error)
func Verify(token string, key []byte) (Claims, error)
```

ちなみに Claims は一般的な概念のようです。

https://qiita.com/yoheimuta/items/b17bfbac17c02d410f54

RFC で規定されている registered claims と private claimes というものがある。

iat: issued_at の略。JWT の発行時間。
exp: expiration time の略。JWT の有効期限。
それ以外のもの (e.g. device_id): ユーザー定義の private claims

### 1.3 呼び出し側の使い方

```go
// internal/handler/login.go（認証 — トークン発行）
claims := jwt.Claims{
    "sub":       userID,
    "device_id": deviceID,
    "iat":       time.Now().Unix(),
    "exp":       time.Now().Add(7 * 24 * time.Hour).Unix(),
}
token, err := jwt.Sign(claims, key)
```

```go
// internal/handler/interceptor/authorization/（認可 — トークン検証）
claims, err := jwt.Verify(token, key)
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

- `.` で 3 パートに分割（`strings.Split`）
- `header.payload` に対して、共通鍵で HMAC-SHA256 を再計算
- `hmac.Equal` で署名と比較（タイミング攻撃耐性あり）
- ペイロードを Base64url デコード → JSON デコード
- `exp` を `time.Now().Unix()` と比較して期限切れチェック

## 4. 鍵ローテーション

- 共通鍵は単一で管理する（複数鍵の併用は行わない）
- ローテーション時は鍵を新しいものに切り替えるだけ
- 旧鍵で署名された JWT は検証失敗 → 401
- クライアントの 401 自動リトライ（`/login` 再実行）で新鍵の JWT を取得
- ユーザー操作不要（キーペア署名による自動再ログイン）
- 鍵の保管は環境変数やシークレット管理（AWS Secrets Manager 等）で行う

> **複数鍵を持たない理由**: 本設計ではクライアントに 401 自動リトライが実装されており、再ログインにユーザー操作が不要（キーペア署名で自動）。そのため鍵切り替え時の一時的な 401 は透過的に処理され、複数鍵を併用する必要がない。

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
