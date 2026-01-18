# HD-2D 水中環境構築ガイド

オクトパストラベラー風のHD-2Dスタイルで水中環境を構築する手順。

## シーン構成

```
Scene Hierarchy:
├── Main Camera
├── Lighting
│   ├── Directional Light
│   └── Point Lights
├── Environment (3D)
│   ├── Floor
│   ├── BackWall
│   ├── Props
│   └── Boundaries
├── PostProcessing Volume
├── Particles
└── Characters (2D Sprites)
```

## Step 1: カメラ設定

| 項目 | 値 |
|------|-----|
| Projection | Perspective |
| Field of View | 35〜45 |
| Rotation | X=45〜55, Y=0, Z=0 |
| Clear Flags | Solid Color |
| Background | #0a1a2e |

## Step 2: 3D背景

### 床 (Floor)
- `3D Object > Plane` を作成
- ピクセルアートテクスチャ（Filter Mode: Point）

### 奥の壁 (BackWall)
- `3D Object > Quad` を床の奥に垂直配置

### 装飾物 (Props)
- ローポリ3Dモデル + ピクセルテクスチャ
- 岩、珊瑚、海藻など

## Step 3: ライティング

### Directional Light
| 項目 | 値 |
|------|-----|
| Rotation | X=50, Y=-30, Z=0 |
| Color | #a0c0ff |
| Intensity | 0.8〜1.2 |
| Shadow | Soft Shadows |

## Step 4: Post Processing

URP Volume で水中感を演出。

### Bloom
- Intensity: 0.3〜0.5
- Threshold: 0.9

### Color Adjustments
- Post Exposure: -0.2
- Color Filter: #80a0c0
- Saturation: -10

### Vignette
- Intensity: 0.3

### Depth of Field
- Mode: Bokeh
- Aperture: 5.6

## Step 5: コースティクス

床に投影する水面の光の揺らぎ。

- Decal または Light Cookie で投影
- アニメーションテクスチャを使用

## Step 6: パーティクル

### 泡
- Shape: Box
- Start Size: 0.02〜0.1
- Direction: 上向き

### 浮遊物
- Noise モジュールで揺らぎ
- 半透明

## Step 7: 2Dスプライト配置

- SpriteRenderer + Sprite-Lit マテリアル
- Billboard スクリプトでカメラ向き固定
