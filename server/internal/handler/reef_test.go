package handler

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"connectrpc.com/connect"
	aquariumv1 "github.com/hashiiiii/aquarium/gen/aquarium/v1"
	"github.com/hashiiiii/aquarium/gen/aquarium/v1/aquariumv1connect"
	"github.com/hashiiiii/aquarium/internal/reef"
	"google.golang.org/protobuf/proto"
)

var testReefTime = time.Date(2026, 10, 2, 12, 0, 0, 0, time.UTC)

func testReefService(t *testing.T) *reef.Service {
	t.Helper()
	repo, err := reef.NewFileRepository(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = repo.Close() })
	return reef.NewService(repo, func() time.Time { return testReefTime })
}

func testReefServer(t *testing.T, service ReefService, developmentIdentity bool) *httptest.Server {
	t.Helper()
	opts := []connect.HandlerOption{connect.WithReadMaxBytes(8 << 10)}
	if developmentIdentity {
		opts = append(opts, connect.WithInterceptors(NewDevelopmentIdentityInterceptor()))
	}
	mux := http.NewServeMux()
	mux.Handle(aquariumv1connect.NewAquariumServiceHandler(NewReefHandler(service), opts...))
	server := httptest.NewUnstartedServer(mux)
	server.EnableHTTP2 = true
	server.StartTLS()
	t.Cleanup(server.Close)
	return server
}

func testReefClient(server *httptest.Server, players []string, options ...connect.ClientOption) aquariumv1connect.AquariumServiceClient {
	identity := connect.UnaryInterceptorFunc(func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			for _, player := range players {
				req.Header().Add(DevelopmentPlayerHeader, player)
			}
			return next(ctx, req)
		}
	})
	options = append(options, connect.WithInterceptors(identity))
	return aquariumv1connect.NewAquariumServiceClient(server.Client(), server.URL, options...)
}

func TestReefRPCProtocols(t *testing.T) {
	for _, test := range []struct {
		name string
		opts []connect.ClientOption
	}{
		{"connect-protobuf", nil},
		{"connect-json", []connect.ClientOption{connect.WithProtoJSON()}},
		{"grpc-protobuf", []connect.ClientOption{connect.WithGRPC()}},
		{"grpc-json", []connect.ClientOption{connect.WithGRPC(), connect.WithProtoJSON()}},
		{"grpc-web-protobuf", []connect.ClientOption{connect.WithGRPCWeb()}},
		{"grpc-web-json", []connect.ClientOption{connect.WithGRPCWeb(), connect.WithProtoJSON()}},
	} {
		t.Run(test.name, func(t *testing.T) {
			server := testReefServer(t, testReefService(t), true)
			client := testReefClient(server, []string{"player_1"}, test.opts...)
			response, err := client.GetAquarium(context.Background(), &aquariumv1.GetAquariumRequest{})
			if err != nil {
				t.Fatal(err)
			}
			if response.State.Revision != 1 || response.State.Pearls != 30 || response.State.LastUpdatedAtUnixMs != testReefTime.UnixMilli() {
				t.Fatalf("unexpected initial state: %v", response.State)
			}
			command := &aquariumv1.ApplyCommandRequest{
				RequestId: "feed-1", ExpectedRevision: response.State.Revision, Action: aquariumv1.Action_ACTION_FEED,
			}
			result, err := client.ApplyCommand(context.Background(), command)
			if err != nil || !result.GetSuccess() || result.State.Fullness != 100 || result.State.Revision != 2 {
				t.Fatalf("feed: result=%v err=%v", result, err)
			}
			replayed, err := client.ApplyCommand(context.Background(), command)
			if err != nil || !replayed.GetReplayed() || !proto.Equal(result.State, replayed.State) {
				t.Fatalf("replay: result=%v err=%v", replayed, err)
			}
			catalog, err := client.GetCatalog(context.Background(), &aquariumv1.GetCatalogRequest{})
			if err != nil || len(catalog.GetSpecies()) != 3 || catalog.Rules.Capacity != reef.Capacity || catalog.Rules.ReplayCapacity != reef.ReplayCapacity {
				t.Fatalf("catalog: result=%v err=%v", catalog, err)
			}
		})
	}
}

func TestReefRPCRequiresDevelopmentIdentity(t *testing.T) {
	server := testReefServer(t, testReefService(t), true)
	for _, test := range []struct {
		name    string
		players []string
	}{
		{"missing", nil},
		{"empty", []string{""}},
		{"traversal", []string{"../player"}},
		{"unicode", []string{"player-é"}},
		{"overlong", []string{strings.Repeat("a", 65)}},
		{"multiple", []string{"alice", "bob"}},
		{"comma", []string{"alice,bob"}},
		{"space", []string{"alice bob"}},
	} {
		t.Run(test.name, func(t *testing.T) {
			client := testReefClient(server, test.players)
			_, err := client.GetAquarium(context.Background(), &aquariumv1.GetAquariumRequest{})
			if connect.CodeOf(err) != connect.CodeUnauthenticated {
				t.Fatalf("want unauthenticated, got %v", err)
			}
		})
	}
	if _, err := testReefClient(server, []string{strings.Repeat("A", 64)}).GetAquarium(context.Background(), &aquariumv1.GetAquariumRequest{}); err != nil {
		t.Fatalf("64-character identity must work: %v", err)
	}

	// Supplying the header alone is inert unless the host explicitly opts in.
	disabled := testReefServer(t, testReefService(t), false)
	client := testReefClient(disabled, []string{"alice"})
	if _, err := client.GetAquarium(context.Background(), &aquariumv1.GetAquariumRequest{}); connect.CodeOf(err) != connect.CodeUnauthenticated {
		t.Fatalf("disabled identity: %v", err)
	}
	if _, err := client.GetCatalog(context.Background(), &aquariumv1.GetCatalogRequest{}); connect.CodeOf(err) != connect.CodeUnauthenticated {
		t.Fatalf("disabled catalog identity: %v", err)
	}
}

func TestReefRPCIsolationAndConflicts(t *testing.T) {
	server := testReefServer(t, testReefService(t), true)
	alice := testReefClient(server, []string{"alice"})
	bob := testReefClient(server, []string{"bob"})
	ctx := context.Background()
	a, err := alice.GetAquarium(ctx, &aquariumv1.GetAquariumRequest{})
	if err != nil {
		t.Fatal(err)
	}
	b, err := bob.GetAquarium(ctx, &aquariumv1.GetAquariumRequest{})
	if err != nil {
		t.Fatal(err)
	}
	adopt := &aquariumv1.ApplyCommandRequest{
		RequestId: "adopt-moon-1", ExpectedRevision: a.State.Revision, Action: aquariumv1.Action_ACTION_ADOPT, SpeciesId: "moon_jelly",
	}
	adopted, err := alice.ApplyCommand(ctx, adopt)
	if err != nil || !adopted.GetSuccess() || adopted.GetAmount() != 30 || adopted.State.Pearls != 0 || len(adopted.State.Creatures) != 2 {
		t.Fatalf("adopt: %v, %v", adopted, err)
	}
	cleaned, err := alice.ApplyCommand(ctx, &aquariumv1.ApplyCommandRequest{
		RequestId: "clean-1", ExpectedRevision: adopted.State.Revision, Action: aquariumv1.Action_ACTION_CLEAN,
	})
	if err != nil {
		t.Fatal(err)
	}
	replayed, err := alice.ApplyCommand(ctx, adopt)
	if err != nil || !replayed.GetReplayed() || !proto.Equal(adopted.State, replayed.State) || replayed.State.Revision == cleaned.State.Revision {
		t.Fatalf("replay must preserve original snapshot: %v, %v", replayed, err)
	}
	conflict := proto.Clone(adopt).(*aquariumv1.ApplyCommandRequest)
	conflict.SpeciesId = "coral_drake"
	if _, err := alice.ApplyCommand(ctx, conflict); connect.CodeOf(err) != connect.CodeAlreadyExists {
		t.Fatalf("reused ID: %v", err)
	}
	stale := proto.Clone(adopt).(*aquariumv1.ApplyCommandRequest)
	stale.RequestId = "stale-1"
	if _, err := alice.ApplyCommand(ctx, stale); connect.CodeOf(err) != connect.CodeAborted {
		t.Fatalf("stale revision: %v", err)
	}
	duplicate, err := alice.ApplyCommand(ctx, &aquariumv1.ApplyCommandRequest{
		RequestId: "duplicate-1", ExpectedRevision: cleaned.State.Revision, Action: aquariumv1.Action_ACTION_ADOPT, SpeciesId: "moon_jelly",
	})
	if err != nil || duplicate.GetSuccess() || duplicate.State.Revision != cleaned.State.Revision+1 {
		t.Fatalf("gameplay no-op must commit: %v, %v", duplicate, err)
	}
	unchanged, err := bob.GetAquarium(ctx, &aquariumv1.GetAquariumRequest{})
	if err != nil || !proto.Equal(b.State, unchanged.GetState()) {
		t.Fatalf("player isolation: %v, %v", unchanged, err)
	}
	// The same request ID belongs to a different player's independent namespace.
	if result, err := bob.ApplyCommand(ctx, adopt); err != nil || result.GetReplayed() || !result.GetSuccess() {
		t.Fatalf("independent idempotency: %v, %v", result, err)
	}
}

func TestReefRPCRejectsInvalidCommandsAndOversizeMessages(t *testing.T) {
	server := testReefServer(t, testReefService(t), true)
	client := testReefClient(server, []string{"alice"})
	ctx := context.Background()
	if _, err := client.GetAquarium(ctx, &aquariumv1.GetAquariumRequest{}); err != nil {
		t.Fatal(err)
	}
	for _, request := range []*aquariumv1.ApplyCommandRequest{
		{},
		{RequestId: "a", ExpectedRevision: 1, Action: aquariumv1.Action(99)},
		{RequestId: "a", ExpectedRevision: 0, Action: aquariumv1.Action_ACTION_FEED},
		{RequestId: "../a", ExpectedRevision: 1, Action: aquariumv1.Action_ACTION_FEED},
		{RequestId: "a", ExpectedRevision: 1, Action: aquariumv1.Action_ACTION_FEED, SpeciesId: "moon_jelly"},
		{RequestId: "a", ExpectedRevision: 1, Action: aquariumv1.Action_ACTION_ADOPT},
	} {
		if _, err := client.ApplyCommand(ctx, request); connect.CodeOf(err) != connect.CodeInvalidArgument {
			t.Fatalf("request %v: %v", request, err)
		}
	}
	_, err := client.ApplyCommand(ctx, &aquariumv1.ApplyCommandRequest{
		RequestId: strings.Repeat("a", 16<<10), ExpectedRevision: 1, Action: aquariumv1.Action_ACTION_FEED,
	})
	if connect.CodeOf(err) != connect.CodeResourceExhausted {
		t.Fatalf("oversized body: %v", err)
	}
	state, err := client.GetAquarium(ctx, &aquariumv1.GetAquariumRequest{})
	if err != nil || state.State.Revision != 1 {
		t.Fatalf("invalid commands must not mutate state: %v, %v", state, err)
	}
}

type failingReefService struct{ err error }

func (s failingReefService) Get(context.Context, string) (reef.State, error) {
	return reef.State{}, s.err
}

func (s failingReefService) Apply(context.Context, string, reef.Command) (reef.Result, error) {
	return reef.Result{}, s.err
}

func TestReefRPCErrorMappingDoesNotLeakStorageDetails(t *testing.T) {
	for _, test := range []struct {
		err  error
		code connect.Code
	}{
		{reef.ErrInvalidArgument, connect.CodeInvalidArgument},
		{reef.ErrRevisionConflict, connect.CodeAborted},
		{reef.ErrRequestIDConflict, connect.CodeAlreadyExists},
		{reef.ErrCorrupt, connect.CodeDataLoss},
		{reef.ErrUnavailable, connect.CodeUnavailable},
		{context.Canceled, connect.CodeCanceled},
		{context.DeadlineExceeded, connect.CodeDeadlineExceeded},
		{errors.New("unexpected failure"), connect.CodeInternal},
	} {
		t.Run(test.code.String(), func(t *testing.T) {
			server := testReefServer(t, failingReefService{fmt.Errorf("%w: /private/secrets/player.json", test.err)}, true)
			client := testReefClient(server, []string{"alice"})
			_, err := client.GetAquarium(context.Background(), &aquariumv1.GetAquariumRequest{})
			if connect.CodeOf(err) != test.code || strings.Contains(err.Error(), "/private/") {
				t.Fatalf("unexpected public error: %v", err)
			}
			_, err = client.ApplyCommand(context.Background(), &aquariumv1.ApplyCommandRequest{Action: aquariumv1.Action_ACTION_FEED})
			if connect.CodeOf(err) != test.code || strings.Contains(err.Error(), "/private/") {
				t.Fatalf("unexpected apply error: %v", err)
			}
		})
	}
}

func TestReefRPCDurableReplayAfterRestart(t *testing.T) {
	directory := t.TempDir()
	open := func() (*reef.FileRepository, *httptest.Server, aquariumv1connect.AquariumServiceClient) {
		repo, err := reef.NewFileRepository(directory)
		if err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() { _ = repo.Close() })
		server := testReefServer(t, reef.NewService(repo, func() time.Time { return testReefTime }), true)
		return repo, server, testReefClient(server, []string{"alice"})
	}
	repo, server, client := open()
	state, err := client.GetAquarium(context.Background(), &aquariumv1.GetAquariumRequest{})
	if err != nil {
		t.Fatal(err)
	}
	command := &aquariumv1.ApplyCommandRequest{
		RequestId: "durable-feed", ExpectedRevision: state.State.Revision, Action: aquariumv1.Action_ACTION_FEED,
	}
	first, err := client.ApplyCommand(context.Background(), command)
	if err != nil {
		t.Fatal(err)
	}
	server.Close()
	if err := repo.Close(); err != nil {
		t.Fatal(err)
	}
	_, _, restarted := open()
	replay, err := restarted.ApplyCommand(context.Background(), command)
	if err != nil || !replay.GetReplayed() || !proto.Equal(first.State, replay.State) {
		t.Fatalf("durable replay: %v, %v", replay, err)
	}
}
