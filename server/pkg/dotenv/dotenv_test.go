package dotenv

import (
	"testing"

	"github.com/google/go-cmp/cmp"
)

func Test_Require(t *testing.T) {
	tests := []struct {
		name string
		key  string
		want string
	}{
		{
			name: "環境変数が設定されている場合_設定値を返す",
			key:  "key",
			want: "value",
		},
		{
			name: "環境変数が設定されていない (空文字) の場合_空文字を返す",
			key:  "key",
			want: "",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			t.Setenv("TEST_ENV_KEY", tt.want)
			e := New()

			got := e.Require("TEST_ENV_KEY")

			if diff := cmp.Diff(tt.want, got); diff != "" {
				t.Errorf("Require(): diff(-want +got)=%s", diff)
			}
		})
	}
}
