# 署名検証実装メモ

クライアント（Unity + Native Plugin）から送られる `signed_device_id` の署名検証実装についてのメモ。

---

## アルゴリズム仕様

| 項目 | 値 |
|------|-----|
| アルゴリズム | ECDSA |
| 曲線 | P-256 |
| ハッシュ | SHA-256 |
| 署名形式 | DER/ASN.1 |
| 公開鍵形式 | Uncompressed Point `04\|\|X(32)\|\|Y(32)` = 65 bytes, Base64 エンコード |

---

## クライアント（Unity + Native Plugin）

秘密鍵は Secure Enclave（iOS）/ Android Keystore（Android）に保管する。
**秘密鍵はハードウェア外に取り出せない設計**になっているため、アプリが呼べるのは「署名してくれ」という命令のみ。

### iOS（Secure Enclave）

- `SecKeyCreateRandomKey` で `kSecAttrTokenIDSecureEnclave` を指定してキーペア生成
- `SecKeyCopyExternalRepresentation` で公開鍵を `04||X||Y`（65 bytes）形式でエクスポート
- `SecKeyCreateSignature` with `kSecKeyAlgorithmECDSASignatureMessageX962SHA256` で署名（DER 出力）

### Android（Android Keystore）

- `KeyPairGenerator` with `KeyStore.getInstance("AndroidKeyStore")` でキーペア生成
- `ECPublicKey.getW().getAffineX/Y` から `04||X||Y` を組み立ててエクスポート
- `Signature.getInstance("SHA256withECDSA")` で署名（DER 出力）

### 共通の値生成ルール

```
公開鍵 : 04||X||Y (65 bytes) → Base64 → proto.public_key
device_id : SHA256(公開鍵の 65 bytes) → hex string → proto.device_id
signed_device_id : sign(秘密鍵, device_id) → DER/ASN.1 → Base64 → proto.signed_device_id
```

---

## サーバー（Go）

### 検証の流れ

```
1. base64decode(public_key) → 04||X||Y (65 bytes)
2. PKIX ヘッダー付与 → x509.ParsePKIXPublicKey → *ecdsa.PublicKey
3. SHA256(公開鍵) == device_id を確認（整合性チェック）
4. base64decode(signed_device_id) → DER bytes
5. ecdsa.VerifyASN1(pubKey, SHA256(device_id), sig) → 一致で認証成功
```

### 実装

```go
// P-256 公開鍵の固定 PKIX ヘッダー（26 bytes）
// SEQUENCE { SEQUENCE { OID ecPublicKey, OID P-256 }, BIT STRING }
var p256PKIXHeader = []byte{
    0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01,
    0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00,
}

func (*SessionHandler) Login(ctx context.Context, req *connect.Request[sessionv1.LoginRequest]) (*connect.Response[sessionv1.LoginResponse], error) {
    // Step 1: 公開鍵をパース（base64 → 04||X||Y → *ecdsa.PublicKey）
    pubRaw, err := base64.StdEncoding.DecodeString(req.Msg.PublicKey)
    if err != nil || len(pubRaw) != 65 || pubRaw[0] != 0x04 {
        return nil, connect.NewError(connect.CodeInvalidArgument, errors.New("invalid public key"))
    }
    pubKey, err := parseP256PublicKey(pubRaw)
    if err != nil {
        return nil, connect.NewError(connect.CodeInvalidArgument, fmt.Errorf("parse public key: %w", err))
    }

    // Step 2: device_id = SHA256(公開鍵) の整合性チェック
    expected := fmt.Sprintf("%x", sha256.Sum256(pubRaw))
    if req.Msg.DeviceId != expected {
        return nil, connect.NewError(connect.CodeInvalidArgument, errors.New("device_id mismatch"))
    }

    // Step 3: 署名検証（DER/ASN.1 形式）
    sig, err := base64.StdEncoding.DecodeString(req.Msg.SignedDeviceId)
    if err != nil {
        return nil, connect.NewError(connect.CodeInvalidArgument, fmt.Errorf("invalid signature: %w", err))
    }
    hashed := sha256.Sum256([]byte(req.Msg.DeviceId))
    if !ecdsa.VerifyASN1(pubKey, hashed[:], sig) {
        return nil, connect.NewError(connect.CodeUnauthenticated, errors.New("signature verification failed"))
    }

    // Step 4: DB でのユーザー特定ロジックへ（login.md §2.1 参照）
    // ...
}

func parseP256PublicKey(raw []byte) (*ecdsa.PublicKey, error) {
    // 04||X||Y に固定 PKIX ヘッダーを付与して x509 でパース
    // elliptic.Unmarshal は Go 1.22 で deprecated のため x509 経由を使用
    pkix := make([]byte, 0, len(p256PKIXHeader)+len(raw))
    pkix = append(pkix, p256PKIXHeader...)
    pkix = append(pkix, raw...)

    pub, err := x509.ParsePKIXPublicKey(pkix)
    if err != nil {
        return nil, err
    }
    ecPub, ok := pub.(*ecdsa.PublicKey)
    if !ok {
        return nil, errors.New("not an ECDSA public key")
    }
    return ecPub, nil
}
```

### 使用パッケージ（すべて標準ライブラリ）

| パッケージ | 用途 |
|---|---|
| `crypto/ecdsa` | 署名検証（`VerifyASN1`） |
| `crypto/x509` | 公開鍵パース（`ParsePKIXPublicKey`） |
| `crypto/sha256` | ハッシュ計算 |
| `encoding/base64` | フィールドのデコード |

---

## なぜ `x509.ParsePKIXPublicKey` が必要か

Base64 デコードで得られるのは `04||X||Y` という生座標バイト列であり、Go の `*ecdsa.PublicKey` 構造体ではない。
`x509.ParsePKIXPublicKey` が ASN.1 構造を解析し、`Curve`・`X`・`Y` を持つ構造体に変換する。

P-256 の PKIX ヘッダーは固定 26 bytes であるため、`04||X||Y` に付与するだけで PKIX 形式になる。
（`elliptic.Unmarshal` でも同様のことができるが Go 1.22 で deprecated）

---

## 署名形式に関する注意

| クライアント実装 | 署名形式 | サーバーの検証関数 |
|---|---|---|
| Secure Enclave / Android Keystore（Native Plugin） | **DER/ASN.1** | `ecdsa.VerifyASN1` ✓ |
| .NET `ECDsa.SignData`（Unity ソフトウェアキー） | **IEEE P1363**（r\|\|s, 64 bytes） | `ecdsa.Verify(r, s)` |

Native Plugin を使う場合は DER 形式で統一されるため `ecdsa.VerifyASN1` を使う。
