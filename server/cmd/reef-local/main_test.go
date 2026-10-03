package main

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"connectrpc.com/connect"
	aquariumv1 "github.com/hashiiiii/aquarium/gen/aquarium/v1"
	"github.com/hashiiiii/aquarium/gen/aquarium/v1/aquariumv1connect"
	"github.com/hashiiiii/aquarium/internal/handler"
	"github.com/hashiiiii/aquarium/internal/reef"
)

func TestRunRequiresExplicitDevelopmentFlag(t *testing.T) {
	for _, args := range [][]string{
		nil,
		{"--dev=false"},
		{"--dev", "--data-dir="},
		{"--dev", "unexpected"},
		{"--dev", "--listen=0.0.0.0:8081"},
	} {
		if err := run(context.Background(), args, io.Discard); err == nil {
			t.Fatalf("expected startup refusal for %q", args)
		}
	}
}

func startLocalTestServer(t *testing.T) *httptest.Server {
	t.Helper()
	repo, err := reef.NewFileRepository(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = repo.Close() })
	server := httptest.NewUnstartedServer(nil)
	server.Config = newLocalServer(reef.NewService(repo, func() time.Time {
		return time.Date(2026, 10, 2, 12, 0, 0, 0, time.UTC)
	}), server.Listener.Addr().String())
	server.Start()
	t.Cleanup(server.Close)
	return server
}

func TestLocalHostGuardsAndHealth(t *testing.T) {
	server := startLocalTestServer(t)
	for _, test := range []struct {
		name   string
		path   string
		host   string
		header http.Header
		status int
	}{
		{name: "health", path: "/healthz", status: http.StatusOK},
		{name: "dns-rebind", path: "/healthz", host: "attacker.example:8081", status: http.StatusForbidden},
		{name: "localhost-name", path: "/healthz", host: "localhost:8081", status: http.StatusForbidden},
		{name: "wrong-port", path: "/healthz", host: "127.0.0.1:1", status: http.StatusForbidden},
		{name: "external-origin", path: "/healthz", header: http.Header{"Origin": {"https://attacker.example"}}, status: http.StatusForbidden},
		{name: "same-origin", path: "/healthz", header: http.Header{"Origin": {server.URL}}, status: http.StatusForbidden},
		{name: "null-origin", path: "/healthz", header: http.Header{"Origin": {"null"}}, status: http.StatusForbidden},
		{name: "empty-origin", path: "/healthz", header: http.Header{"Origin": {""}}, status: http.StatusForbidden},
		{name: "cross-site", path: "/healthz", header: http.Header{"Sec-Fetch-Site": {"cross-site"}}, status: http.StatusForbidden},
		{name: "old-login-absent", path: "/session.v1.SessionService/Login", status: http.StatusNotFound},
	} {
		t.Run(test.name, func(t *testing.T) {
			request, err := http.NewRequest(http.MethodGet, server.URL+test.path, nil)
			if err != nil {
				t.Fatal(err)
			}
			request.Host = test.host
			request.Header = test.header
			response, err := server.Client().Do(request)
			if err != nil {
				t.Fatal(err)
			}
			defer response.Body.Close()
			body, err := io.ReadAll(response.Body)
			if err != nil {
				t.Fatal(err)
			}
			if response.StatusCode != test.status {
				t.Fatalf("status=%d want=%d body=%s", response.StatusCode, test.status, body)
			}
			if response.Header.Get("Access-Control-Allow-Origin") != "" || response.Header.Get("Cache-Control") != "no-store" {
				t.Fatalf("unsafe response headers: %v", response.Header)
			}
			if test.name == "health" && !strings.Contains(string(body), "development-only") {
				t.Fatalf("health should identify dev host: %s", body)
			}
		})
	}
}

func TestLocalHostSupportsNativeCleartextHTTP2(t *testing.T) {
	server := startLocalTestServer(t)
	protocols := new(http.Protocols)
	protocols.SetUnencryptedHTTP2(true)
	transport := &http.Transport{Protocols: protocols}
	t.Cleanup(transport.CloseIdleConnections)
	client := &http.Client{Transport: transport, Timeout: 5 * time.Second}
	identity := connect.UnaryInterceptorFunc(func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			req.Header().Set(handler.DevelopmentPlayerHeader, "local-grpc-player")
			return next(ctx, req)
		}
	})
	rpc := aquariumv1connect.NewAquariumServiceClient(client, server.URL, connect.WithGRPC(), connect.WithInterceptors(identity))
	response, err := rpc.GetAquarium(context.Background(), &aquariumv1.GetAquariumRequest{})
	if err != nil || response.GetState().GetRevision() != 1 {
		t.Fatalf("cleartext gRPC: %v, %v", response, err)
	}
	result, err := rpc.ApplyCommand(context.Background(), &aquariumv1.ApplyCommandRequest{
		RequestId: "local-feed", ExpectedRevision: response.State.Revision, Action: aquariumv1.Action_ACTION_FEED,
	})
	if err != nil || !result.GetSuccess() {
		t.Fatalf("cleartext gRPC apply: %v, %v", result, err)
	}
}

func TestLocalHostBoundsRequestBodies(t *testing.T) {
	server := startLocalTestServer(t)
	request, err := http.NewRequest(http.MethodPost, server.URL+aquariumv1connect.AquariumServiceApplyCommandProcedure,
		strings.NewReader(`{"requestId":"`+strings.Repeat("a", 128<<10)+`","expectedRevision":"1","action":"ACTION_FEED"}`))
	if err != nil {
		t.Fatal(err)
	}
	request.Header.Set("Content-Type", "application/json")
	request.Header.Set(handler.DevelopmentPlayerHeader, "alice")
	response, err := server.Client().Do(request)
	if err != nil {
		t.Fatal(err)
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusTooManyRequests && response.StatusCode != http.StatusRequestEntityTooLarge {
		body, _ := io.ReadAll(response.Body)
		t.Fatalf("oversized request not bounded: %d %s", response.StatusCode, body)
	}
}
