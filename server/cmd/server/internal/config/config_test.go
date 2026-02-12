package config

import (
	"testing"

	"github.com/google/go-cmp/cmp"
	"github.com/hashiiiii/aquarium/pkg/application"
	"github.com/hashiiiii/aquarium/pkg/dotenv"
)

func Test_Config_JWTCommonKey(t *testing.T) {
	tests := []struct {
		name    string
		want    string
		wantErr bool
	}{
		{
			name:    "環境変数が設定されている場合_設定値を返す",
			want:    "key",
			wantErr: false,
		},
		{
			name:    "環境変数が設定されていない (空文字) の場合_空文字を返し Err() がエラーを返す",
			want:    "",
			wantErr: true,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			t.Setenv("AQUA_JWT_COMMON_KEY", tt.want)
			app, err := application.New(application.Test.String(), application.Local.String())
			if err != nil {
				t.Fatal(err)
			}

			e := dotenv.New()

			c, err := New(app, e)
			if err != nil {
				t.Fatal(err)
			}

			got := c.JWTCommonKey()

			if diff := cmp.Diff(tt.want, got); diff != "" {
				t.Errorf("JWTCommonKey(): diff(-want +got)=%s", diff)
			}

			gotErr := e.Err()
			if (gotErr != nil) != tt.wantErr {
				t.Errorf("Err(): wantErr %v, got %v", tt.wantErr, gotErr)
			}
		})
	}
}
