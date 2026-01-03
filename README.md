# habitarium

habitarium = habitable + terrarium (aquarium)

HD-2D 調の放置型育成ゲームです。

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
