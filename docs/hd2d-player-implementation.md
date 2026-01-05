# HD-2D プレイヤー実装ガイド

## 概要

3D Terrain 上で 2D スプライトキャラクターを物理ベース（Rigidbody）で歩かせる HD-2D スタイルの実装ガイドです。

### 要件
- 移動方式: Rigidbody（3D 物理）
- スプライト: 固定（方向切り替えなし）
- アニメーション: なし（今回は無視）

---

## 1. スプライトのインポート設定

### 対象ファイル
`Assets/Sprites/Buddy/001/rotations/south.png`

### Unity Editor での設定

| 設定項目 | 値 |
|---------|-----|
| Texture Type | Sprite (2D and UI) |
| Sprite Mode | Single |
| Pixels Per Unit | 80 |
| Filter Mode | Point (no filter) |
| Compression | None |
| Generate Mip Maps | OFF |
| Alpha Is Transparency | ON |

### 設定手順
1. Project ウィンドウで `south.png` を選択
2. Inspector で上記設定を適用
3. Apply ボタンをクリック

---

## 2. Player GameObject の構成

### 階層構造

```
Player (Empty GameObject)
├── Transform: Position (0, 1, 0)
├── Rigidbody
├── CapsuleCollider
├── PlayerController.cs
│
└── SpriteHolder (子オブジェクト)
    ├── Transform: Position (0, 0.5, 0)
    ├── SpriteRenderer
    └── Billboard.cs
```

### Rigidbody 設定

| 項目 | 値 |
|-----|-----|
| Mass | 1 |
| Drag | 4 |
| Angular Drag | 0.05 |
| Use Gravity | ON |
| Is Kinematic | OFF |
| Interpolate | Interpolate |
| Collision Detection | Continuous |
| Freeze Rotation X | ON |
| Freeze Rotation Y | OFF |
| Freeze Rotation Z | ON |

### CapsuleCollider 設定

| 項目 | 値 |
|-----|-----|
| Center | (0, 0.5, 0) |
| Radius | 0.3 |
| Height | 1.0 |
| Direction | Y-Axis |

### SpriteRenderer 設定

| 項目 | 値 |
|-----|-----|
| Sprite | south.png |
| Material | Sprites-Default または Sprite-Lit-Default (URP) |
| Sorting Layer | Default |
| Order in Layer | 0 |

---

## 3. スクリプト

### 3.1 PlayerController.cs

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private float jumpForce = 5f;

    [Header("Ground Detection")]
    [SerializeField] private float groundCheckDistance = 0.1f;
    [SerializeField] private LayerMask groundLayer;

    private Rigidbody rb;
    private Vector2 moveInput;
    private bool isGrounded;
    private bool isSprinting;

    private InputSystem_Actions inputActions;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        inputActions = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
        inputActions.Player.Move.performed += OnMove;
        inputActions.Player.Move.canceled += OnMove;
        inputActions.Player.Jump.performed += OnJump;
        inputActions.Player.Sprint.performed += ctx => isSprinting = true;
        inputActions.Player.Sprint.canceled += ctx => isSprinting = false;
    }

    private void OnDisable()
    {
        inputActions.Player.Disable();
    }

    private void FixedUpdate()
    {
        CheckGrounded();
        Move();
    }

    private void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        if (isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }

    private void Move()
    {
        Vector3 cameraForward = Camera.main.transform.forward;
        Vector3 cameraRight = Camera.main.transform.right;

        cameraForward.y = 0f;
        cameraRight.y = 0f;
        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 moveDirection = cameraForward * moveInput.y + cameraRight * moveInput.x;
        float currentSpeed = isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;

        Vector3 velocity = moveDirection * currentSpeed;
        velocity.y = rb.velocity.y;
        rb.velocity = velocity;
    }

    private void CheckGrounded()
    {
        isGrounded = Physics.SphereCast(
            transform.position + Vector3.up * 0.5f,
            0.3f,
            Vector3.down,
            out RaycastHit hit,
            groundCheckDistance + 0.5f,
            groundLayer
        );
    }
}
```

### 3.2 Billboard.cs

```csharp
using UnityEngine;

public class Billboard : MonoBehaviour
{
    public enum BillboardMode
    {
        LookAtCamera,
        LockYAxis
    }

    [SerializeField] private BillboardMode mode = BillboardMode.LockYAxis;

    private Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (mainCamera == null) return;

        switch (mode)
        {
            case BillboardMode.LookAtCamera:
                transform.LookAt(mainCamera.transform);
                transform.Rotate(0, 180, 0);
                break;

            case BillboardMode.LockYAxis:
                Vector3 direction = mainCamera.transform.position - transform.position;
                direction.y = 0;
                if (direction != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(-direction);
                }
                break;
        }
    }
}
```

### 3.3 CameraFollow.cs

```csharp
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0, 8, -10);
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private float lookDownAngle = 30f;

    private void Start()
    {
        transform.rotation = Quaternion.Euler(lookDownAngle, 0, 0);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}
```

---

## 4. レイヤー設定

### Ground レイヤーの作成
1. Edit > Project Settings > Tags and Layers
2. Layers セクションで空いているレイヤー（例: Layer 6）に "Ground" を追加
3. Terrain オブジェクトのレイヤーを "Ground" に変更
4. PlayerController の Ground Layer に "Ground" を設定

---

## 5. カメラ設定

### Main Camera の設定

| 項目 | 値 |
|-----|-----|
| Position | (0, 8, -10) |
| Rotation | (30, 0, 0) |
| Projection | Perspective |
| Field of View | 50 |

### CameraFollow の設定
1. Main Camera に CameraFollow.cs をアタッチ
2. Target に Player オブジェクトをアサイン
3. Offset: (0, 8, -10)
4. Smooth Speed: 5
5. Look Down Angle: 30

---

## 6. セットアップ手順まとめ

1. **スプライト設定**: `south.png` のインポート設定を変更
2. **Ground レイヤー作成**: Tags and Layers で新規レイヤー作成
3. **Terrain 設定**: Ground レイヤーを適用
4. **Player 作成**:
   - 空の GameObject を作成し "Player" と命名
   - Rigidbody, CapsuleCollider, PlayerController を追加
   - 子オブジェクト "SpriteHolder" を作成
   - SpriteRenderer, Billboard を追加
5. **カメラ設定**:
   - Main Camera に CameraFollow を追加
   - Target に Player をアサイン

---

## 7. パラメータリファレンス

```
[Movement]
Move Speed: 5
Sprint Multiplier: 1.5
Jump Force: 5
Ground Check Distance: 0.1

[Rigidbody]
Mass: 1
Drag: 4
Freeze Rotation: X=true, Z=true

[Collider]
Type: Capsule
Center: (0, 0.5, 0)
Radius: 0.3
Height: 1.0

[Camera]
Offset: (0, 8, -10)
Look Down Angle: 30
Smooth Speed: 5
FOV: 50
```

---

## 8. トラブルシューティング

| 問題 | 解決策 |
|------|--------|
| スプライトが地形に埋まる | SpriteHolder の Y オフセットを調整 |
| 斜面で滑る | Rigidbody の Drag を増加 |
| 段差で引っかかる | CapsuleCollider の Radius を小さく |
| スプライトがちらつく | Sorting Layer を適切に設定 |
| 入力が効かない | InputSystem_Actions の Enable() 確認 |
| ジャンプできない | Ground Layer の設定を確認 |
