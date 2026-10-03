# Online First Reef: ローカル開発用 Unity 接続

Unity の HD-2D 水槽を、`reef-local` のサーバー権威型ゲーム API に接続する独立したシーンです。
**ネイティブの Unity Editor / デスクトップ向け開発試作**です。実プレイヤー用の認証、公開サーバー、
WebGL、スマートフォン実機、クラウド同期は対象外です。

## 起動

1. Go 1.26.x と Unity **6000.3.2f1** を用意する。Unity パッケージの解決を待つ。
2. リポジトリルートから別ターミナルで開発サーバーを起動する。

   ```sh
   cd server
   go mod download
   go run ./cmd/reef-local --dev --data-dir "$PWD/.reef-data"
   ```

   サーバーを再起動するときも同じ絶対 `--data-dir` を使う。終了は Ctrl+C。
   初回取得以外に有料サービス、データベース、ログイン、制作ツールは不要。
3. Unity で `unity/` を開き、**Aquarium → Open Online First Reef** を選択する。
   対象シーンは `Assets/Aquarium/Scenes/AquariumOnline.unity`。
4. Hierarchy の **Online First Reef** を選び、Inspector で次を確認してから Play。
   - **Server Endpoint**: `http://127.0.0.1:8081`
   - **Development Player**: `unity-dev`（1〜64文字の半角英数字、`_`、`-`）
   - **Refresh Interval Seconds**: 15（実行時の最小値は5秒）
5. Game View は **1280 × 720 / 横向き**から確認する。接続後、server catalogue と水槽が表示される。

設定変更は Play を停止してから行う。Inspector の変更を実行中のセッションへ付け替える機能はない。
HTTP、数値ループバック **127.0.0.1** のみを許可する。`localhost`、IPv6、パス付き URL、
ユーザー情報付き URL、HTTPS、外部ホストは受け付けない。これは開発範囲を限定するための明示的な制約。
HTTP redirect とシステム proxy も使用しない。

Development Player は **認証ではない**。同じコンピューター上のプロセスは他の開発プレイヤーを選べる。
実在の個人情報を値にせず、ポート転送、公開 reverse proxy、外部公開、共有テストサービスには使わない。
Unity とサーバーは同じコンピューター上で実行する。サーバーは browser Origin を拒否するため WebGL では動作しない。
[サーバーの詳細](../server/docs/first-reef.md)も参照。

## 操作とサーバー権威

- **FEED / CLEAN / COLLECT** と未所持の仲間のボタンはサーバーへ「操作したい」という intent を送る。
- 所持済みの仲間のボタンや水槽のクリックは、表示上の選択だけを変える。
- pearls、fullness、cleanliness、成長、未回収報酬、revision は、最後に確認できたサーバーの snapshot を表示する。
- 価格、名前、成長に必要な care hours、報酬率、care threshold は **サーバー catalogue** の値を使う。
  表示の成長バーは server growth / server maturity の比率。種別 ID と3種の既存イラストとの対応だけがクライアントの表示用設定。
  未知の種別には汎用の最初のイラストを使うが、経済・成長値をローカルの既定値へ置き換えない。
- 表示の遊泳・泡・給餌エフェクトはクライアントの演出。給餌エフェクトは成功を受信してから再生する。
- 接続中は設定間隔で再取得する。ウィンドウに戻ったときやアプリ休止からの復帰時も再取得する。
  時間経過の計算は、その要求を受けたサーバーが行う。クライアントは時計を送らず、ローカルで報酬や成長を補間しない。
- 要求処理中は新しい操作と再接続・再送を無効化し、コード側でも同時実行を防ぐ。
  サーバーのゲーム上の不成立（残高不足など）はそのまま表示し、確認できるまで残高や仲間を先に変えない。

## 切断、競合、未確定操作

| 表示 / 状況 | 動作と次の操作 |
|---|---|
| CONTACTING SERVER | 要求処理中。クリックを重ねても新しい操作を作らない。 |
| RECONNECT REQUIRED | 最後に確認できた snapshot を保持。**RECONNECT / REFRESH** で再取得する。初回接続に失敗した場合は架空の初期水槽を作らない。 |
| COMMAND RESULT UNKNOWN | サーバーに届いた可能性がある操作の結果をまだ確認できない。新しいゲーム操作をロックし、**RETRY SAME COMMAND** で同一要求を再送する。 |
| revision conflict | 最新 snapshot を取得して通知する。元の操作は自動再送しない。内容を確認してから利用者が改めてボタンを押すと新しい意図とIDで送る。 |
| 再取得した revision が後退 | 古い snapshot へ巻き戻さず操作をロックする。同じサーバーデータディレクトリを使用しているか確認する。サーバーを意図的にリセットした場合は、未確定要求を調査・保全してから Play を再起動する。 |
| ジャーナル読み書きエラー | ゲーム操作をロックし警告する。既存ファイルを保全し、権限・空き容量・別プロセスを確認する。設定や読み取りが不正なら Play を止めて修正する。 |
| ONLINE SETUP BLOCKED | 設定不正や同一プレイヤーのジャーナルが使用中など。Play を停止し Inspector / ファイル状況を確認する。オフラインに自動切り替えない。 |

**RECONNECT / REFRESH は未確定操作を再送しない。** snapshot を再取得しても、どの要求が成立したかの
確認には同一要求の receipt が必要になる。未確定のままなら RETRY SAME COMMAND を使う。
リプレイ結果が古い snapshot でも、既に確認済みの revision を巻き戻さず、必要に応じて最新状態を取得する。

クライアントは操作前に `Application.persistentDataPath/online-command-<endpoint/player hash>.json` へ
要求 ID、expected revision、action、species ID と接続先・開発プレイヤーの結び付きを記録する。
これは **未確定操作のジャーナルだけ**で、水槽状態・報酬・成長・認証情報のセーブではない。
アプリを終了しても、同じ設定で再起動すると未確定要求を復元できる。payload と ID は再送時にも変えない。

- `.lock` の排他ロックにより、同一保存先の同じ接続先・プレイヤーを複数のシーン／プロセスで同時に開かない。
  競合を試す場合は、別のローカル API client から同じプレイヤーを操作する。
- 一時ファイルの flush と atomic replace を使うが、親ディレクトリの fsync は行っていない。
  プロセス再起動時の復元と、ホスト電源断時の filesystem 依存の永続性を区別する。電源断に対する完全保証はない。
- 不正・別プレイヤーのジャーナルは自動で削除しない。**結果が未確定の間に削除すると、その操作の結果追跡を失う。**
  障害を調べるときは原本を保全し、サーバー側の状態・receipt と照合する。
- シーン終了では要求をキャンセルし、処理が収束してから接続と排他ロックを解放する。
  キャンセルはサーバーでの操作取消を意味しないため、未確定の要求はジャーナルに残す。
- サーバーの同一データディレクトリを維持する。開発サーバーをリセットした場合、以前のジャーナルを
  そのまま新しい世界へ適用することを前提にしない。

## オフライン版との区別

**Aquarium → Open Offline First Reef** は既存の `AquariumDemo.unity` を開く。
旧メニュー **Open First Reef** もオフライン版への互換ショートカットとして残す。
HUD にも ONLINE / OFFLINE を明示する。

- オフライン版は `AquariumGame`、Core simulation、`aquarium-v1.json` を使用する。
- オンライン版は `OnlineAquariumGame`、`Aquarium.Online`、server snapshot / catalogue を使用する。
- オンライン版は `AquariumSimulation.AdvanceTo` を呼ばず、ローカル水槽セーブを読み書き・インポートしない。
  オフライン版の進行が server player に転送される機能はない。
- Build Settings の起動シーンは引き続きオフライン版。オンライン版は無効状態で登録している。
  ネイティブの開発プレイヤーを作る場合は、Build Profile でオンラインシーンを明示的に有効化して先頭にする。
  公開向けの既定動作は変更していない。

## 構成

- `Assets/Aquarium/Online`: Unity 非依存 DTO、Connect/JSON transport、session、厳密な応答検証、未確定要求ジャーナル
- `Assets/Aquarium/Runtime/OnlineAquariumGame.cs`: 接続設定、ライフサイクル、操作の結線、既存の TankView への表示投影
- `Assets/Aquarium/Runtime/OnlineAquariumHud.cs`: server catalogue / snapshot から作る uGUI
- `Assets/Aquarium/Runtime/UnityOnlineJsonCodec.cs`: protobuf JSON の64bit整数文字列を維持する JsonUtility adapter
- `Assets/Aquarium/Tests/PlayMode/AquariumOnlinePresentationTests.cs`: HUD、接続状態、連打、server catalogue、codec、scene/session 結線
- `tools/Aquarium.Online.Tests`: .NET の protocol/session/journal テストとローカル Go server を使った検証

## 検証

リポジトリルートから、Unity ライセンス不要の構造・ロジック検証:

```sh
python3 tools/validate_unity_assets.py
dotnet run --project tools/Aquarium.Core.Tests
sh tools/run-online-tests.sh
```

オンラインテストの詳細と live server 検証のオプションは [テストガイド](../tools/Aquarium.Online.Tests/README.md) を参照。
Unity Editor の **Test Runner → EditMode / PlayMode** も両方実行する。ライセンス不要テストの成功は
Unity API、URP描画、実入力、native player の検証を代替しない。実施した検証と未実施の検証は PR に区別して記録する。

### 手動の受け入れチェック

1. 新しい開発プレイヤーで catalogue / 1体 / 30 pearls が読み込まれる。オフラインセーブが変更されない。
2. FEED / CLEAN はサーバー確認後に反映。Moon Jelly の迎え入れが30消費し、所持済みボタンは VIEW になる。
3. サーバー停止中の初回起動でエラーと再接続が表示される。再起動後に RECONNECT / REFRESH で回復。
4. 応答待ちの間に各ボタンを連打しても同時要求を増やさない。選択は引き続き可能。
5. 操作応答が失われたとき、新しい操作が無効になり同一要求を再送できる。Play 再開後も同じ未確定要求を復元。
6. 同一プレイヤーを別 API client で進めた後、Unity で操作して revision conflict を確認。
   snapshot が更新され、利用者の次のクリックまで元の操作が再送されない。
7. フォーカス離脱・休止・復帰がサーバー再取得だけを行う。切断中に手元の時計を変えても残高は増えない。
8. 同じプレイヤーの二つ目のシーン／プロセスはジャーナルの排他エラーを表示。先の要求を書き潰さない。
9. 1280×720 / 1920×1080 / 4:3 で状態・価格・復旧ボタンが読め、UIクリックが水槽選択へ抜けない。
10. ネイティブ開発ビルドでも DTO の stripping、URP shader、再起動後のジャーナル復元を確認する。
