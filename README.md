# aquarium

HD-2D 調の放置型育成ゲームです。

## Unity で遊ぶ: First Reef

Unity **6000.3.2f1** で `unity/` を開き、**Aquarium → Open First Reef** から Play。
給餌・掃除・3種の仲間・成長・pearls 回収・ローカル保存・最大8時間のオフライン進行を実装しています。
新デモはサーバーや有料の画像制作サービスに依存しません。

起動方法、操作、保存/復旧、検証、現在の制約は [First Reef ガイド](docs/first-reef.md) を参照してください。

## ローカルサーバー接続: Online First Reef

Go の開発サーバーを起動し、Unity の **Aquarium → Open Online First Reef** から Play。
成長・所持 pearls・回収・購入はサーバーで計算し、Unity は snapshot を表示します。
上記オフラインデモとは保存を分離し、通信不能時にローカル報酬へ切り替えません。

[オンライン起動・再接続・検証ガイド](docs/online-first-reef.md) / [サーバーガイド](server/docs/first-reef.md)

これは同じ PC 上の開発接続です。本番認証・外部公開・スマートフォン接続には対応していません。

以下は既存のアセット制作メモです。新デモの起動にこの制作フローは必要ありません。

## Init Image を用意する

Init Image は Diffuse map の作成に利用する。この画像では大雑把な色味さえ分かれば良いです。

![img](./images/init_image.png)

例えば、これは土の Init Image です。Gemini の Nano Banana Pro で作成しました。

## Diffuse map を作成する

Diffuse map は [PixelLab](https://www.pixellab.ai/) の [Simple Creator](https://www.pixellab.ai/create) で作成する。

- Tool: Create S-M image (BitForge)
- Prompt: (e.g. Tileable seamless dirt texture, brown earth.)
- View: Low top-down
- Direction: None
- Init Image: 先ほど作成した init image を指定する
- Size: 32x32
- Transparent background: enabled

この設定で Generate ボタンを押すと作成される。

## Diffuse map を pixel perfect にする

[Pixel Snapper](https://www.spritefusion.com/pixel-snapper) を使うことで、pixel perfect な画像の変換することができる。使い方はサイトに行けばわかる。

## Normal map を作成する (WIP)

[Laigter](https://github.com/azagaya/laigter) を利用する。

- Import Image から Diffuse を import する
- Bump を 0 にする
- Export Image を押す
- Maps to Export は Normal にチェックをつける
- Export ボタンを押す

## Terrain に設定する

