# インフラ構成

## 概要

Unity 製モバイル & Steam ゲーム（アクアリウム系）のバックエンドインフラ構成。

## サービス特性

| 項目 | 内容 |
|------|------|
| 通信頻度 | 低〜中（リアルタイム対戦ではない） |
| 書き込み | やや多い（プレイヤーデータの保存） |
| キャッシュ効果 | 低い（プレイヤー固有データが中心） |
| 対象地域 | 日本メイン、海外にも広がると嬉しい |
| クライアント | Unity（モバイル + Steam） |

## ホスティング

Vultr 東京リージョン（$12/月 - 2vCPU / 2GB RAM）

1 台の VPS に Docker Compose でアプリ・DB・キャッシュを全部載せる構成。

```yaml
# docker-compose.yml
services:
  app:
    build: .
    ports:
      - "8080:8080"
  mysql:
    image: mysql:8
    volumes:
      - db_data:/var/lib/mysql
  redis:
    image: redis:7
    volumes:
      - redis_data:/data
```

### 選定理由

- 東京リージョンあり（ゲーム × 日本ユーザーではレイテンシが重要）
- コスパが良い（$12/月で小規模サービスには十分）
- 手動管理が可能（マネージドに頼らず自分でコントロールできる）

## データストア

### MySQL

- プレイヤーデータの永続化
- write-heavy を想定し `innodb_buffer_pool_size` をメモリに合わせて適切に設定
- 定期バックアップ必須（プレイヤーデータのロストはゲームでは致命的）

### Redis

キャッシュよりもセッションストア・一時データ用途がメイン。

- セッション管理
- レートリミット（チート対策にも）
- プレイ中の一時的なプレイヤー状態の保持

## セキュリティ

- SSH 鍵認証（パスワード認証無効化）
- ファイアウォール設定（必要ポートのみ開放）
- リバースプロキシ（Caddy で SSL 自動化）
- API 認証（チート対策として最低限のトークン認証は初期から導入）

## デプロイ

GitHub Actions → SSH で `docker compose up` を想定。

## スケールパス

```
Phase 1: 小規模（初期）
  Vultr 1台 → app + MySQL + Redis 全部載せ
  月 $12

Phase 2: ユーザー増加
  Vultr スケールアップ（4GB〜8GB に変更）
  月 $20〜50

Phase 3: 海外展開で本格化
  アプリ → Cloud Run や ECS に移行
  DB → PlanetScale や Aurora
  CDN → Cloudflare
```
