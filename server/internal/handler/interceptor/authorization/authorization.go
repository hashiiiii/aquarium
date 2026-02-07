package authorization

import (
	"context"
	"errors"
	"strings"

	"connectrpc.com/connect"
)

func NewInterceptor() connect.UnaryInterceptorFunc {
	return func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			// 1. スキーマ由来のデータから認可が必要な API かどうかを判定する

			// 2. 対象のヘッダーの値を取得する
			header := req.Header().Get("Authorization")
			if header == "" {
				return nil, connect.NewError(connect.CodeUnauthenticated, errors.New("missing token"))
			}

			// 3. JWT を取り出す
			jwt := strings.TrimPrefix(header, "Bearer ")
			if jwt == header {
				return nil, connect.NewError(connect.CodeUnauthenticated, errors.New("invalid token format"))
			}

			// 4. jwt パッケージを使用して検証する
			// 5. verify メソッドの返り値を受け取り、必要なデータは context.WithValue(ctx, "playerID", playerID) みたいな感じで入れておく

			return next(ctx, req)
		}
	}
}
