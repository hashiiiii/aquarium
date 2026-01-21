# HD-2D 水中環境構築ガイド

オクトパストラベラー風のHD-2Dスタイルで水中環境を構築する手順。

## 目標イメージ

参考: `images/wf_001.png`

構成要素:
- 水面から差し込むゴッドレイ（光の筋）
- 砂地の海底
- 沈没船、古代遺跡などの大型装飾
- サンゴ礁、海藻などの植生
- 多層背景による奥行き感

---

## 実装進捗

### Phase 1: 基盤（最優先）
- [ ] カメラ設定（Step 1）
- [ ] 多層背景システム（Step 2）
- [ ] 砂地テクスチャ（Step 3）

### Phase 2: ライティング
- [ ] Directional Light（Step 5）
- [ ] ゴッドレイ（Step 5）
- [ ] コースティクス（Step 7）

### Phase 3: 装飾物（Step 4）
- [ ] 沈没船
- [ ] 古代遺跡（左）
- [ ] 古代遺跡（右）
- [ ] サンゴ（ピンク）
- [ ] サンゴ（紫）
- [ ] サンゴ（青）
- [ ] サンゴ（黄/オレンジ）
- [ ] 海藻（緑/ケルプ）

### Phase 4: Post Processing（Step 6）
- [ ] Bloom
- [ ] Color Adjustments
- [ ] Vignette
- [ ] Depth of Field
- [ ] Film Grain

### Phase 5: パーティクル（Step 8）
- [ ] 泡 (Bubbles)
- [ ] 浮遊物 (FloatingDebris)
- [ ] 光の粒子 (LightShafts)

**全体進捗: 0/23 項目完了**

---

## 実装優先順位

効果が高い順:

1. **多層背景** - 奥行き感に最も影響
2. **ゴッドレイ** - 水中感の核心
3. **Post Processing** - 全体の雰囲気
4. **砂地テクスチャ** - 海底のリアリティ
5. **大型装飾** - 沈没船、遺跡
6. **コースティクス** - 光の揺らぎ
7. **サンゴ・海藻** - 生態系の表現
8. **パーティクル** - 仕上げ

---

## シーン構成

```
Scene Hierarchy:
├── Main Camera
├── Lighting
│   ├── Directional Light (メイン光源)
│   ├── Point Lights (アクセント)
│   └── GodRays (ゴッドレイ用)
├── Environment (3D)
│   ├── Background
│   │   ├── FarLayer (遠景: 水中の霧)
│   │   ├── MidLayer (中景: 遺跡シルエット)
│   │   └── NearLayer (近景: 詳細背景)
│   ├── Floor (砂地)
│   ├── Props
│   │   ├── Shipwreck (沈没船)
│   │   ├── Ruins (遺跡)
│   │   ├── Corals (サンゴ)
│   │   └── Seaweed (海藻)
│   └── Boundaries
├── PostProcessing Volume
├── Particles
│   ├── Bubbles (泡)
│   ├── FloatingDebris (浮遊物)
│   └── LightShafts (光の粒子)
└── Characters (2D Sprites)
```

---

## Step 1: カメラ設定

| 項目 | 値 | 備考 |
|------|-----|------|
| Projection | Perspective | 奥行き感のため |
| Field of View | 35〜45 | 狭めで圧縮効果 |
| Position | (0, 3, -12) | 少し上から見下ろす |
| Rotation | X=15〜25, Y=0, Z=0 | 軽い俯瞰 |
| Clear Flags | Solid Color | |
| Background | #0a1a2e | 深海の暗い青 |

---

## Step 2: 多層背景システム

HD-2Dの奥行き感を出すため、複数のQuadを異なる距離に配置。

### 遠景 (FarLayer) - Z=30
```
GameObject: Quad
Position: (0, 5, 30)
Scale: (60, 30, 1)
Material: Unlit/Transparent
Texture: 水中の霧・ぼんやりした建造物シルエット
Color: #1a3050 (半透明)
```

### 中景 (MidLayer) - Z=15
```
GameObject: Quad
Position: (0, 3, 15)
Scale: (40, 20, 1)
Material: Unlit/Transparent
Texture: 遺跡や岩のシルエット
```

### 近景 (NearLayer) - Z=8
```
GameObject: Quad
Position: (0, 2, 8)
Scale: (20, 10, 1)
Material: Sprite-Lit-3D
Texture: 詳細な背景（サンゴ、岩など）
```

---

## Step 3: 海底 (Floor)

### 砂地テクスチャ
```
GameObject: Plane
Position: (0, 0, 0)
Scale: (4, 1, 2)
Material: Sprite-Lit-3D
Texture設定:
  - Diffuse: 砂地テクスチャ (Filter: Point)
  - Normal: 砂の凹凸マップ
  - Tiling: (4, 4)
```

既存の Terrain Layers を活用可能:
- `Assets/Terrains/Layers/1_Layer/` (Diffuse + Normal)

---

## Step 4: 装飾物 (Props)

### 沈没船 (Shipwreck)
```
配置: 画面左側、斜めに傾ける
Position: (-3, 0.5, 2)
Rotation: (0, 25, 15)
Scale: 適宜調整
Material: Sprite-Lit-3D + ピクセルアートテクスチャ
```

### 古代遺跡 (Ruins)
```
配置: 画面右側
Position: (4, 0, 3)
構成: 柱、神殿の一部など複数パーツ
```

### サンゴ (Corals)
```
配置: 画面全体に散りばめる
種類:
  - 枝状サンゴ (高さ0.3〜1.0)
  - 脳サンゴ (地面に密着)
  - ピンク/オレンジ/紫の色違い
実装: Quad + 透過テクスチャ または ローポリメッシュ
```

### 海藻 (Seaweed)
```
配置: 前景と中景に
実装方法:
  1. 複数のQuadを重ねて立体感
  2. Shader で揺れアニメーション (sin波)
  3. 半透明で奥行き感
```

---

## Step 5: ライティング

### Directional Light (メイン)
| 項目 | 値 | 備考 |
|------|-----|------|
| Rotation | X=50, Y=-30, Z=0 | 斜め上から |
| Color | #fffaf0 | 温白色（水面からの光） |
| Intensity | 1.0〜1.5 | |
| Shadow | Soft Shadows | Resolution: 2048 |

### ゴッドレイ用 Spot Light (複数)
```
配置: 3〜5本、角度を変えて
Position: 上方 (0, 15, 0)
Rotation: 斜め下向き
Color: #e0f0ff
Intensity: 0.3〜0.5
Spot Angle: 15〜25
Range: 30
Volumetric: 有効化（URP対応時）
```

---

## Step 6: Post Processing

URP Volume で水中感を演出。

### Bloom (光の拡散)
| 項目 | 値 |
|------|-----|
| Intensity | 0.5〜0.8 |
| Threshold | 0.8 |
| Scatter | 0.7 |

### Color Adjustments (色調補正)
| 項目 | 値 |
|------|-----|
| Post Exposure | -0.3 |
| Color Filter | #7090b0 |
| Saturation | -15 |
| Contrast | 10 |

### Vignette (周辺減光)
| 項目 | 値 |
|------|-----|
| Intensity | 0.35 |
| Smoothness | 0.4 |

### Depth of Field (被写界深度)
| 項目 | 値 |
|------|-----|
| Mode | Bokeh |
| Focus Distance | 8 |
| Aperture | 5.6 |

### Film Grain (ノイズ)
| 項目 | 値 |
|------|-----|
| Intensity | 0.1 |
| Response | 0.5 |

---

## Step 7: コースティクス（水面の光）

床や装飾物に投影する水面の光の揺らぎ。

### 方法1: Light Cookie
```
Directional Light > Cookie に設定
Texture: コースティクスパターン (ループアニメーション)
スクリプトで UV オフセットをアニメーション
```

### 方法2: Projector / Decal
```
URP Decal Projector を使用
Material: コースティクステクスチャ (Additive)
アニメーション: シェーダーで UV スクロール
```

### 方法3: シェーダー
```hlsl
// コースティクス UV アニメーション例
float2 causticUV = worldPos.xz * 0.5;
causticUV += _Time.y * float2(0.02, 0.01);
float caustic = tex2D(_CausticTex, causticUV).r;
```

---

## Step 8: パーティクル

### 泡 (Bubbles)
| 項目 | 値 |
|------|-----|
| Shape | Box (床全体) |
| Emission Rate | 10〜30 |
| Start Size | 0.02〜0.1 |
| Start Speed | 0.5〜1.5 |
| Gravity Modifier | -0.1 (上昇) |
| Color over Lifetime | 白→透明 |
| Noise | Strength 0.3, Frequency 1 |

### 浮遊物 (FloatingDebris)
| 項目 | 値 |
|------|-----|
| Shape | Box |
| Emission Rate | 5〜10 |
| Start Size | 0.01〜0.05 |
| Start Speed | 0.1 |
| Noise | Strength 0.5 |
| Render Mode | Billboard |
| Material | 半透明の点や小さな破片 |

### 光の粒子 (LightShafts)
| 項目 | 値 |
|------|-----|
| Shape | Cone (光の方向) |
| Emission Rate | 20〜50 |
| Start Size | 0.05〜0.2 |
| Start Color | #ffffcc (薄い黄色) |
| Color over Lifetime | 明→暗 |
| Size over Lifetime | 小→大 |

---

## Step 9: 2Dスプライト配置

### モンスター/キャラクター
```
SpriteRenderer:
  Material: Sprite-Lit-3D
  Sorting Layer: Characters
  Order in Layer: 0

Billboard.cs でカメラ向き固定
影の設定:
  Cast Shadows: On
  Receive Shadows: On
```

---

## 必要なアセット一覧

### テクスチャ
- [ ] 遠景背景（水中霧）
- [ ] 中景背景（遺跡シルエット）
- [ ] 近景背景（詳細）
- [ ] 砂地テクスチャ（Diffuse + Normal）
- [ ] 沈没船スプライト
- [ ] 遺跡・柱スプライト（複数パーツ）
- [ ] サンゴスプライト（各色 5-6種類）
- [ ] 海藻スプライト（2-3種類）
- [ ] コースティクステクスチャ（ループ可能）

### シェーダー/マテリアル
- [ ] 海藻揺れシェーダー（sin波アニメーション）
- [ ] 水中カラーグレーディング（URP Volume Profile）

---

## アセット作成ワークフロー

```
1. 参考画像から要素を抽出
2. PixelLab で各要素のテクスチャ生成
3. Pixel Snapper でピクセルパーフェクト化
4. Laigter で Normal Map 生成（3D装飾用）
5. Unity にインポート (Filter: Point, Compression: None)
6. シーンに配置・調整
```
