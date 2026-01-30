# ConnectRPC エラー設計まとめ (200/400/500 割り切りモデル)

このドキュメントは、ConnectRPC を用いたシステムにおいて、ビジネスロジックで HTTP ステータスや gRPC Code を意識せずに、ドメイン固有のエラーコードを型安全に扱うための設計指針です。

1. Proto 定義 (共通基盤)まず、エラーコードに「400系か500系か」のメタデータを付与できるようにします。

```
syntax = "proto3";
package common.v1;

import "google/protobuf/descriptor.proto";

// Enum値に拡張オプションを追加するための定義
extend google.protobuf.EnumValueOptions {
  int32 http_code = 50001; 
}

// ドメイン横断の共通エラーコード
enum ErrorCode {
  ERROR_CODE_UNSPECIFIED = 0;

  // 400系 (Client Error)
  USER_NOT_FOUND = 1001 [(http_code) = 400];
  BAD_PARAMETER  = 1002 [(http_code) = 400];

  // 500系 (Server Error)
  DATABASE_ERROR = 5001 [(http_code) = 500];
  INTERNAL_UNKNOWN = 5002 [(http_code) = 500];
}

// 実際に Details にパックされるメッセージ
message ErrorDetail {
  ErrorCode code = 1;
  string message = 2;
}
```

2. Server 側ヘルパー (Go)ビジネスロジックで利用するエラー生成関数です。リフレクションを使用して .proto に定義した http_code を自動判別します。

```
package connect_helpers

import (
	"errors"
	"[github.com/bufbuild/connect-go](https://github.com/bufbuild/connect-go)"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/reflect/protoreflect"
	"google.golang.org/protobuf/types/descriptorpb"
	commonv1 "path/to/gen/common/v1"
)

// NewCustomError は ErrorCode から適切な Connect Error を生成します
func NewCustomError(ec commonv1.ErrorCode, msg string) error {
	// 1. Enum オプションから http_code を取得
	httpCode := getHttpCode(ec)

	// 2. HTTPコードを Connect Code にマッピング
	var connectCode connect.Code
	if httpCode == 400 {
		connectCode = connect.CodeInvalidArgument
	} else {
		connectCode = connect.CodeInternal
	}

	// 3. Connect Error の作成と Details の付与
	err := connect.NewError(connectCode, errors.New(msg))
	if detail, detailErr := connect.NewErrorDetail(&commonv1.ErrorDetail{
		Code:    ec,
		Message: msg,
	}); detailErr == nil {
		err.AddDetail(detail)
	}

	return err
}

// プロトタイプのリフレクションを使用してオプションを取得する補助関数
func getHttpCode(ec commonv1.ErrorCode) int32 {
	desc := ec.Descriptor().Values().ByNumber(ec)
	if desc == nil {
		return 500
	}
	opts := desc.Options().(*descriptorpb.EnumValueOptions)
	if opts == nil {
		return 500
	}
	// 拡張フィールドから http_code を取得
	if proto.HasExtension(opts, commonv1.E_HttpCode) {
		val := proto.GetExtension(opts, commonv1.E_HttpCode)
		if n, ok := val.(int32); ok {
			return n
		}
	}
	return 500
}
```

3. Client 側ヘルパー (C#)C# 側では拡張メソッドを使い、Details からのエラーコード抽出を隠蔽します。

```
using System.Linq;
using Connect;
using Common.V1; // 生成されたNamespace

namespace MyApp.Extensions
{
    public static class ConnectExceptionExtensions
    {
        /// <summary>
        /// ConnectException からカスタム ErrorCode を抽出します。
        /// 存在しない場合は Unspecified を返します。
        /// </summary>
        public static ErrorCode GetCustomCode(this ConnectException ex)
        {
            // Details 内を走査して ErrorDetail メッセージを探す
            var detail = ex.Details.FirstOrDefault(d => d.TypeUrl == ErrorDetail.Descriptor.FullName);
            
            if (detail != null && detail.Value is ErrorDetail errorDetail)
            {
                return errorDetail.Code;
            }

            return ErrorCode.Unspecified;
        }
    }
}
```

4. 利用イメージ

Server (Go)

```
func (s *UserServer) GetUser(...) {
    if notFound {
        // HTTPコードを意識せず、ドメインエラーだけを指定
        return nil, connect_helpers.NewCustomError(commonv1.ErrorCode_USER_NOT_FOUND, "User not found")
    }
}
```
Client (C#)

```
try 
{
    var res = await client.GetUserAsync(req);
}
catch (ConnectException ex) 
{
    // 拡張メソッドでスッキリとエラーコードを判定
    var code = ex.GetCustomCode();
    
    switch (code)
    {
        case ErrorCode.UserNotFound:
            // ユーザー不在時のUI処理
            break;
        case ErrorCode.DatabaseError:
            // サーバーエラー時のUI処理
            break;
        case ErrorCode.Unspecified:
            // DNS からでたエラーなど
            break;
        default:
            // 予期せぬエラーはバグである
            ExceptionDispatchInfo.Capture(ex).Throw();
    }
}
```
