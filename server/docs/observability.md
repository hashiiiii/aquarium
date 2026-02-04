# Observability（監視基盤）

## 概要

サーバーで何が起きたかを後から確認できるようにするための仕組み。

```
Unity (クライアント) ──リクエスト──▶ Go サーバー ──▶ DB
                                        │
                                        ▼
                                   監視基盤で記録
```

## 3つの柱

| 種類 | 何がわかる | ツール |
|------|-----------|--------|
| **ログ** | 何が起きたか | slog → Loki |
| **トレース** | 処理にどれだけ時間がかかったか | otel → Tempo |
| **メトリクス** | リクエスト数、エラー率 | otel → Prometheus |

全て **Grafana** で確認できる。

## データの流れ

```
┌─────────────────────────────────────────────────────────┐
│ Go サーバー                                              │
│                                                         │
│   slog.Info("ログ") ──────────────▶ stdout (JSON)       │
│   otelconnect (自動) ─────────────▶ OTLP                │
│                                                         │
└─────────────────────────────────────────────────────────┘
                    │                       │
                    ▼                       ▼
              ┌──────────┐         ┌──────────────────┐
              │  Loki    │         │ OTel Collector   │
              │  (ログ)  │         │                  │
              └────┬─────┘         └────────┬─────────┘
                   │                        │
                   │               ┌────────┴────────┐
                   │               ▼                 ▼
                   │        ┌──────────┐     ┌────────────┐
                   │        │  Tempo   │     │ Prometheus │
                   │        │(トレース)│     │(メトリクス)│
                   │        └────┬─────┘     └──────┬─────┘
                   │             │                  │
                   └─────────────┼──────────────────┘
                                 ▼
                          ┌────────────┐
                          │  Grafana   │
                          │  (可視化)  │
                          └────────────┘
```

## 技術スタック

### アプリケーション層

| 技術 | 役割 | 説明 |
|------|------|------|
| slog | ログ出力 | Go 1.21+ 標準の構造化ログ |
| otelconnect | 計装 | Connect 用の OpenTelemetry Interceptor |

### 観測基盤（Grafana スタック）

| 技術 | 役割 | 説明 |
|------|------|------|
| OpenTelemetry Collector | データ収集 | アプリから受け取り、各バックエンドに振り分け |
| Loki | ログ保存 | stdout から収集、検索可能に |
| Tempo | トレース保存 | リクエストの流れを記録 |
| Prometheus | メトリクス保存 | CPU、メモリ、リクエスト数など |
| Grafana | 可視化 | 上記すべてを1画面で表示 |

## 実装方法

### 自動 vs 手動

| やりたいこと | 実装方法 |
|-------------|---------|
| API 全体の計測 | **何もしない**（otelconnect が自動でやる） |
| API 内の処理を細かく計測 | `otel.Tracer().Start()` を手動で書く |
| ログを出す | `slog.Info()` を手動で書く |

### Interceptor の設定

```go
// mux.go
opts := connect.WithInterceptors(
    otelconnect.NewInterceptor(),      // トレース・メトリクス（自動）
    validate.NewInterceptor(),          // バリデーション
    interceptor.NewLoggingInterceptor(), // ログ出力
)
```

### ログ出力（slog）

```go
// 構造化ログ（JSON で出力される）
slog.Info("request completed",
    slog.String("procedure", "/session.v1.SessionService/Login"),
    slog.Duration("duration", 150*time.Millisecond),
)
```

### 手動トレース（必要な場合のみ）

```go
func (*SessionHandler) Login(ctx context.Context, req *sessionv1.LoginRequest) (*sessionv1.LoginResponse, error) {
    // API 全体は自動でトレースされる

    // DB クエリの時間を個別に計測したい場合
    ctx, span := otel.Tracer("session").Start(ctx, "query-player")
    player, err := db.FindPlayer(ctx, req.GetDeviceId())
    span.End()

    return &sessionv1.LoginResponse{...}, nil
}
```

## 自動で記録される情報

### トレース（Tempo）

```
Login API を叩くと自動で記録される：

├── procedure: /session.v1.SessionService/Login
├── 開始時刻: 2024-01-15 10:00:00.000
├── 終了時刻: 2024-01-15 10:00:00.150
├── 所要時間: 150ms
├── ステータス: OK (or ERROR)
└── エラー内容: (あれば)
```

### メトリクス（Prometheus）

```
自動で記録される数値：

- rpc_server_duration_seconds   # API ごとのレイテンシ
- rpc_server_requests_total     # API ごとのリクエスト数
- rpc_server_request_size_bytes # リクエストサイズ
```

## 環境構成

ローカルとクラウドで同じ構成を使う。

| 項目 | ローカル | クラウド |
|------|---------|---------|
| Go アプリ | `go run` or Docker | Kubernetes / Cloud Run 等 |
| Grafana スタック | docker-compose | マネージド or 自前構築 |
| コード | **同じ** | **同じ** |
| 設定 | 環境変数で切り替え | 環境変数で切り替え |

### 環境変数

```bash
# OTLP 送信先
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_SERVICE_NAME=aquarium
```

## 選定理由

| 理由 | 説明 |
|------|------|
| 全て OSS | 無料、ベンダーロックインなし |
| Grafana 統一 | Loki, Tempo, Prometheus は全て Grafana Labs 製で連携が良い |
| OpenTelemetry | 業界標準、将来別のツールに変えても対応可能 |
| slog | 外部依存なし（Go 標準）、JSON 出力で Loki と相性良い |
