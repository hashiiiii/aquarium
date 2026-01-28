# ゲームアプリ認証設計ガイド

1ユーザー1端末・キーペア署名方式によるモバイルゲームアプリの認証・認可フロー設計。

---

## 1. 基本構成

### 1.1 識別子の役割

| 識別子 | 役割 | 備考 |
|--------|------|------|
| **Platform Account ID** (Apple / Google) | ユーザー本人の不変な証明 | 機種変更・アンインストール時のデータ復旧用 |
| **Device ID** | 端末の識別子 | 公開鍵の SHA256 ハッシュ |
| **Public Key** | 署名検証用 | サーバーに登録 |
| **Private Key** | 署名生成用 | Secure Enclave / Keystore に保存（エクスポート不可） |
| **Access Token** (JWT) | API実行時の通行証 | ペイロードに `user_id` と `device_id` を含む |

### 1.2 クライアント側の保存先

| OS | 秘密鍵 | JWT |
|----|--------|-----|
| **iOS** | Secure Enclave（エクスポート不可） | Keychain |
| **Android** | Android Keystore（エクスポート不可） | EncryptedSharedPreferences |

> **ポイント**: 秘密鍵は端末外に取り出せない。だから device_id を知っていても、別端末からは署名を作れない。

### 1.3 ストレージ構成

| ストレージ | 役割 |
|-----------|------|
| **MySQL (Writer)** | ユーザーマスターデータ（`device_id`, `public_key` を含む） |
| **MySQL (Reader)** | 参照用レプリカ |
| **Redis** | User モデルのキャッシュ。API実行時の高速な認可チェックに使用 |

---

## 2. 認証フロー

### 2.1 ログイン (`POST /login`)

トークン初回発行、および期限切れ時の復旧に使用。

#### リクエスト

```json
{
  "device_id": "string",       // 必須 - 公開鍵の SHA256
  "public_key": "string",      // 初回登録時のみ必須
  "signature": "string",       // 必須 - sign(秘密鍵, device_id)
  "platform_id": "string",     // 任意 - Apple/Google 連携時
  "transfer_id": "string"      // 任意 - 端末移行時
}
```

#### 署名の生成と検証

```
【クライアント】
1. signature = sign(秘密鍵, device_id)
2. サーバーに送信

【サーバー】
1. DB から公開鍵を取得（新規の場合はリクエストから）
2. 公開鍵で signature を検証
3. 復号結果が device_id と一致すれば OK
```

#### ユーザー特定ロジック（優先順位順）

```
1. transfer_id != nil
   └─→ transfer_id で User を検索
       └─→ 見つかった → 署名検証 → device_id と public_key を更新、transfer_id をクリア
       └─→ 見つからない → エラー

2. platform_id != nil
   └─→ platform_id で User を検索
       └─→ 見つかった → 署名検証 → device_id と public_key を更新
       └─→ 見つからない → 3 へ fallthrough

3. device_id のみ
   └─→ device_id で User を検索
       └─→ 見つかった → 署名検証 → ログイン成功
       └─→ 見つからない → public_key 必須、新規 User 作成
```

> **transfer_id の用途**: Platform 未連携のユーザーが機種変更する際のセーフティネット。Platform 連携済みなら `platform_id` で復旧できるため不要。

#### ユースケース別の動作

| ケース | 送信パラメータ | 動作 |
|--------|---------------|------|
| 初回起動 | `device_id` + `public_key` + `signature` | 新規ユーザー作成 |
| 通常ログイン | `device_id` + `signature` | 既存ユーザーでログイン |
| JWT 期限切れ | `device_id` + `signature` | 新しい JWT を発行 |
| Platform 連携後 | `device_id` + `platform_id` + `signature` | platform_id で特定、device_id を更新 |
| 機種変更（連携済み） | 新 `device_id` + 新 `public_key` + `platform_id` + `signature` | device_id を更新 |
| 機種変更（未連携） | 新 `device_id` + 新 `public_key` + `transfer_id` + `signature` | device_id を更新、transfer_id をクリア |

#### シーケンス

```
Client                          Server
  |                               |
  |-- POST /login --------------->|
  |   { device_id, public_key?,   |
  |     signature,                |
  |     platform_id?, transfer_id? }
  |                               |
  |                               |-- ユーザー特定ロジック実行
  |                               |-- 署名検証
  |                               |-- device_id, public_key を更新
  |                               |-- Redis キャッシュを更新
  |                               |
  |<-- { access_token, user_id } -|
```

> **Note**: `platform_id` でユーザーを特定した際に `device_id` を更新することで、旧端末は次回 API アクセス時に `401` となり自動的にログアウトされる。

#### レスポンス

##### 成功時（200 OK）

```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIs...",
  "user_id": "uuid",
  "is_new_user": true
}
```

##### エラー時

| ステータス | ケース |
|-----------|--------|
| **400** | 必須パラメータ不足 |
| **401** | 署名検証失敗 |
| **404** | transfer_id が無効 |

### 2.2 汎用 API 実行 (`/foo` 等)

```
Client                          Server
  |                               |
  |-- GET /foo ------------------>|
  |   Authorization: Bearer JWT   |
  |                               |
  |                               |-- Step 1: JWT 署名検証（メモリ内で完結）
  |                               |-- Step 2: Redis から User キャッシュ取得
  |                               |           （なければ MySQL Writer から再構築）
  |                               |-- Step 3: User の device_id と JWT の device_id を比較
  |                               |
  |<-- 200 OK / 401 Unauthorized -|
```

**認可結果**:
- 一致 → 処理続行
- 不一致 → `401 Unauthorized`（別端末でログインされた）

### 2.3 クライアント側の自動リトライ

HTTP クライアントの Interceptor で以下を実装:

1. `401` を検知
2. 自動的に `/login` を叩いてトークン更新
3. 元の API を再試行
4. `/login` も失敗 → 他端末への移行済みとみなしタイトル画面へ

---

## 3. 設計判断の経緯

### 3.1 認証方式の選定

| 検討案 | 採否 | 理由 |
|--------|------|------|
| device_id のみ | 不採用 | 抜き出されると別端末からなりすまし可能 |
| device_id + device_secret | 不採用 | 両方抜き出されると同じ問題 |
| refresh_token 方式 | 不採用 | 抜き出し可能、エンドポイント増加 |
| **キーペア署名方式** | **採用** | 秘密鍵はエクスポート不可能、シンプルな設計 |

### 3.2 device_id だけではダメな理由

```
device_id を知っている ≠ その端末である

攻撃者が device_id を知っていても:
  → 秘密鍵がないから署名を作れない
  → サーバーの検証に失敗
  → ログインできない
```

### 3.3 別端末からログインするには

| 方法 | 用途 |
|------|------|
| `platform_id` | Apple/Google 連携済みなら、どの端末からでも OK |
| `transfer_id` | Platform 未連携時のワンタイム移行コード |

### 3.4 1端末制限と後勝ち制御

| 検討案 | 採否 | 理由 |
|--------|------|------|
| JWT 署名検証のみ | 不採用 | 端末Bログイン後も、端末AのJWTが切れるまで共存を許してしまう |
| 1端末制限の撤廃 | 不採用 | アカウント共有・チート（24時間稼働）のリスク、データ不整合 |
| **毎回 Redis 参照** | **採用** | Redis は十分高速。シンプルな実装で「即座に他端末をキック」が可能 |

#### 後勝ち制御の動作

```
1. 端末B がログイン → DB/Redis の device_id が B に更新
2. 端末A が API を叩く → JWT 内の device_id (A) と DB (B) が不一致
3. 端末A は 401 → 自動的にログアウト
```

**結果**: 完全な「後勝ち」を保証

---

## 4. エッジケースと対策

### 4.1 アプリ削除時

秘密鍵も削除される。復旧方法:

| 状態 | 復旧方法 |
|------|---------|
| Platform 連携済み | `platform_id` で復旧、新しいキーペアを登録 |
| Platform 未連携 | `transfer_id` を事前発行しておく必要あり |

> Platform 連携を促す UI が重要。

### 4.2 レプリケーション遅延への対応

`/login` 直後の API 実行を考慮し、Redis キャッシュがない場合は **Reader ではなく Writer** を参照。

### 4.3 Redis 障害への対応

1. **フォールバック**: MySQL Writer を参照
2. **L1 キャッシュ**: アプリサーバーのローカルメモリに数秒間だけ User 情報を保持（負荷軽減）

---

## 5. 技術仕様

### 5.1 認証と認可の境界

| 概念 | 目的 | タイミング | 本設計での位置づけ |
|------|------|-----------|-------------------|
| **認証** (Authentication) | 「誰であるか」を確認 | `/login` 実行時 | キーペア署名で端末を証明 |
| **認可** (Authorization) | 「実行する権利があるか」を確認 | 各 API リクエスト時 | JWT 署名検証 + Redis でのデバイスチェック |

### 5.2 署名アルゴリズム（クライアント → サーバー）

| 項目 | 値 |
|------|-----|
| アルゴリズム | ECDSA |
| 曲線 | P-256 |
| ハッシュ | SHA-256 |

### 5.3 JWT

#### ペイロード

```json
{
  "sub": "user_id",
  "device_id": "公開鍵の SHA256",
  "iat": 1706400000,
  "exp": 1706403600
}
```

#### 設定値

| 項目 | 値 | 備考 |
|------|-----|------|
| 署名方式 | HS256 | サーバーの秘密鍵で署名・検証 |
| 有効期限 | 7日 | アプリ起動時に /login を叩くため長めで OK |

#### JWT を採用する理由

セッション ID ではなく JWT を使う理由:

**1. サーバーメモリでの一次切り分け**

- JWT は自己署名されているため、DB/Redis アクセス前に偽造・期限切れを判定可能
- 不正リクエストをインフラ深層に到達させない → 攻撃耐性向上

**2. 水平スケール時の対応**

API サーバーを複数台にスケールアウトしても、各サーバーが同じ秘密鍵を持っていれば JWT を検証できる。

```
[ロードバランサー]
       ↓
 ┌─────┼─────┐
API-1  API-2  API-3  ← 全サーバーが同じ秘密鍵を保持
```

#### 署名方式の選択

| 方式 | 署名 | 検証 | 用途 |
|------|------|------|------|
| **共通鍵 (HS256)** | 秘密鍵 | 同じ秘密鍵 | 単一サービス構成（本設計） |
| **公開鍵 (RS256)** | 秘密鍵 | 公開鍵 | JWT 発行と検証が別サービスの場合 |

本設計では API サーバーが JWT の発行・検証を両方行うため、**HS256（共通鍵方式）** を採用。

### 5.4 JWT 署名鍵のローテーション

| 項目 | 値 |
|------|-----|
| ローテーション頻度 | 90日 |
| JWT 有効期限 | 7日 |

> **ポイント**: JWT 有効期限 < ローテーション頻度 にすることで、鍵変更時に古い JWT が自然消滅する。

#### ローテーション手順

```
1. 新しい鍵を追加（発行: 新鍵、検証: 新旧両方）
2. 7日間待機（古い JWT が期限切れになる）
3. 古い鍵を削除（検証: 新鍵のみ）
```

これにより強制ログアウトなしでスムーズに移行可能。

#### 鍵の配布方法

環境変数やシークレット管理（AWS Secrets Manager 等）で行う。

---

## 6. 実装チェックリスト

### クライアント

- [ ] Secure Enclave / Keystore でキーペア生成
- [ ] device_id = SHA256(公開鍵)
- [ ] signature = sign(秘密鍵, device_id)
- [ ] JWT を Keychain / EncryptedSharedPreferences に保存
- [ ] アプリ起動時に /login を実行
- [ ] 401 時の自動リトライ

### サーバー

- [ ] 公開鍵での署名検証
- [ ] device_id, public_key の保存
- [ ] JWT 発行（HS256、有効期限 7日）
- [ ] API 実行時の device_id 照合
- [ ] Redis キャッシュの実装（障害時は MySQL Writer にフォールバック）
- [ ] JWT 署名鍵のローテーション（90日ごと、新旧鍵の併用期間 7日）
