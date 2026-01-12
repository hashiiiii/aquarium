# 32x32 ピクセルキャラクターアニメーション学習ガイド

## 概要

AI生成の32x32ピクセルアートキャラクターに、効率的にアニメーションを付けるための学習リソース。

### 32x32が有利な理由

- 小さいので細かい動きが見えない → シンプルな動きで十分
- シェーダー変形との相性が良い（崩れが目立たない）
- ファミコン〜SFC時代のゲームがこのサイズで成立していた

---

## 効果的なアニメーション手法

| 手法 | 実装コスト | 効果 |
|------|-----------|------|
| シェーダーで呼吸・揺れ | ◎ 超低 | 生きてる感が出る |
| 上下バウンス（Tween） | ◎ 超低 | 待機・歩行に |
| 影のスケール連動 | ○ 低 | ジャンプ感 |
| スプライト2-3枚切替 | △ 中 | 攻撃などアクション |
| パーティクル追加 | ○ 低 | 豪華に見える |

### おすすめ構成

```
待機: シェーダー呼吸 + 微揺れ（全キャラ共通）
移動: 上下バウンス + 移動エフェクト（砂埃など）
攻撃: 前に突き出す + スラッシュエフェクト + SE
被弾: 点滅 + ノックバック + 画面シェイク
```

本体のアニメは最小限で、**エフェクトと演出で盛る**戦略。

---

## 学習リソース

### DOTween（Tweenアニメーション）

| リソース | URL |
|----------|-----|
| 公式ドキュメント | http://dotween.demigiant.com/documentation.php |
| GitHub | https://github.com/Demigiant/dotween |

ポイント：`SetLoops`, `SetEase`, `Sequence` を理解すれば大体できる

---

### Unity シェーダー

**ShaderGraph（ノードベース、初心者向け）**

| リソース | 内容 |
|----------|------|
| Unity公式マニュアル | https://docs.unity3d.com/Manual/shader-graph.html |
| Unity Learn | 「Shader Graph」で検索 |

**HLSL（コードベース、細かい制御）**

| リソース | 内容 |
|----------|------|
| Catlike Coding | https://catlikecoding.com/unity/tutorials/ |
| The Book of Shaders | https://thebookofshaders.com/ |

Catlike Codingは特におすすめ。基礎から丁寧に解説されている。

---

### ピクセルアート表現・演出

| リソース | 内容 |
|----------|------|
| Saint11 Pixel Art Tutorials | https://saint11.org/blog/pixel-art-tutorials/ |
| Pedro Medeiros (Patreon/Twitter) | ピクセルアニメのGIF解説が秀逸 |
| GDC Vault「Celeste」 | 少ないフレームでの演出手法 |

Saint11の「動きの原則」系の記事は、32x32でどう動かすかの参考になる。

---

### Juice（ゲームフィール・演出）

| リソース | 内容 |
|----------|------|
| 「Juice it or lose it」 | YouTube検索。画面シェイク、パーティクル等の演出論 |
| Game Maker's Toolkit | 「Game Feel」「Juice」関連動画 |

「本体は動かさず、周りで盛る」考え方の原典。

---

## 学習順序

```
1. DOTween → Tweenの基本を掴む
2. Saint11 → ピクセルアートの動きの原則
3. Juice it or lose it → 演出の考え方
4. ShaderGraph → 必要になったら
```

シェーダーは後回しでOK。DOTween + 演出の考え方だけでかなりのことができる。
