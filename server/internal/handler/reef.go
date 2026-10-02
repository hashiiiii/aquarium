package handler

import (
	"context"
	"errors"

	"connectrpc.com/connect"
	aquariumv1 "github.com/hashiiiii/aquarium/gen/aquarium/v1"
	"github.com/hashiiiii/aquarium/gen/aquarium/v1/aquariumv1connect"
	"github.com/hashiiiii/aquarium/internal/reef"
)

// DevelopmentPlayerHeader is a caller-selected local identity, NOT an
// authenticated player credential. Only reef-local --dev enables this header.
const DevelopmentPlayerHeader = "X-Aquarium-Dev-Player"

type reefIdentityKey struct{}

// NewDevelopmentIdentityInterceptor enables INSECURE DEVELOPMENT-ONLY player
// selection. Never install it on a public listener or behind a public proxy.
func NewDevelopmentIdentityInterceptor() connect.Interceptor {
	return connect.UnaryInterceptorFunc(func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			values := req.Header().Values(DevelopmentPlayerHeader)
			if len(values) != 1 || !validDevelopmentPlayer(values[0]) {
				return nil, connect.NewError(connect.CodeUnauthenticated, errors.New("one valid development player header is required"))
			}
			return next(context.WithValue(ctx, reefIdentityKey{}, values[0]), req)
		}
	})
}

func validDevelopmentPlayer(player string) bool {
	if len(player) < 1 || len(player) > 64 {
		return false
	}
	for _, char := range []byte(player) {
		if !(char >= 'a' && char <= 'z' || char >= 'A' && char <= 'Z' || char >= '0' && char <= '9' || char == '_' || char == '-') {
			return false
		}
	}
	return true
}

func reefPlayer(ctx context.Context) (string, error) {
	player, ok := ctx.Value(reefIdentityKey{}).(string)
	if !ok || !validDevelopmentPlayer(player) {
		return "", connect.NewError(connect.CodeUnauthenticated, errors.New("player identity is required"))
	}
	return player, nil
}

// ReefService is the authoritative domain boundary used by the RPC adapter.
type ReefService interface {
	Get(context.Context, string) (reef.State, error)
	Apply(context.Context, string, reef.Command) (reef.Result, error)
}

// ReefHandler adapts typed intent to the domain. It is inert without an
// explicitly installed identity provider; it never trusts protobuf identity.
type ReefHandler struct {
	service ReefService
}

var _ aquariumv1connect.AquariumServiceHandler = (*ReefHandler)(nil)

// NewReefHandler wraps the authoritative service without enabling identity.
func NewReefHandler(service ReefService) *ReefHandler {
	return &ReefHandler{service: service}
}

func (h *ReefHandler) GetAquarium(ctx context.Context, _ *aquariumv1.GetAquariumRequest) (*aquariumv1.GetAquariumResponse, error) {
	player, err := reefPlayer(ctx)
	if err != nil {
		return nil, err
	}
	state, err := h.service.Get(ctx, player)
	if err != nil {
		return nil, reefError(err)
	}
	return &aquariumv1.GetAquariumResponse{State: protoState(state)}, nil
}

func (h *ReefHandler) ApplyCommand(ctx context.Context, req *aquariumv1.ApplyCommandRequest) (*aquariumv1.ApplyCommandResponse, error) {
	player, err := reefPlayer(ctx)
	if err != nil {
		return nil, err
	}
	if req == nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, errors.New("command is required"))
	}
	var action reef.Action
	switch req.Action {
	case aquariumv1.Action_ACTION_FEED:
		action = reef.ActionFeed
	case aquariumv1.Action_ACTION_CLEAN:
		action = reef.ActionClean
	case aquariumv1.Action_ACTION_COLLECT:
		action = reef.ActionCollect
	case aquariumv1.Action_ACTION_ADOPT:
		action = reef.ActionAdopt
	default:
		return nil, connect.NewError(connect.CodeInvalidArgument, errors.New("a supported action is required"))
	}
	result, err := h.service.Apply(ctx, player, reef.Command{
		RequestID:        req.RequestId,
		ExpectedRevision: req.ExpectedRevision,
		Action:           action,
		SpeciesID:        req.SpeciesId,
	})
	if err != nil {
		return nil, reefError(err)
	}
	return &aquariumv1.ApplyCommandResponse{
		State:    protoState(result.State),
		Success:  result.Success,
		Message:  result.Message,
		Amount:   int64(result.Amount),
		Replayed: result.Replayed,
		Advance: &aquariumv1.AdvanceResult{
			ElapsedSeconds:   result.Advance.ElapsedSeconds,
			SimulatedSeconds: result.Advance.SimulatedSeconds,
			ClockRolledBack:  result.Advance.ClockRolledBack,
			WasCapped:        result.Advance.WasCapped,
		},
	}, nil
}

func (*ReefHandler) GetCatalog(ctx context.Context, _ *aquariumv1.GetCatalogRequest) (*aquariumv1.GetCatalogResponse, error) {
	if _, err := reefPlayer(ctx); err != nil {
		return nil, err
	}
	catalog := reef.Catalog()
	response := &aquariumv1.GetCatalogResponse{
		Species: make([]*aquariumv1.SpeciesDefinition, 0, len(catalog)),
		Rules: &aquariumv1.SimulationRules{
			Capacity:               reef.Capacity,
			WalletCapacity:         reef.WalletCapacity,
			RewardCapacity:         reef.RewardCapacity,
			OfflineCapSeconds:      reef.OfflineCapSeconds,
			FullnessLossPerHour:    reef.FullnessLossPerHour,
			CleanlinessLossPerHour: reef.CleanlinessLossPerHour,
			CareThreshold:          reef.CareThreshold,
			ReplayCapacity:         reef.ReplayCapacity,
		},
	}
	for _, item := range catalog {
		response.Species = append(response.Species, &aquariumv1.SpeciesDefinition{
			Id:                   item.ID,
			DisplayName:          item.DisplayName,
			Price:                int64(item.Price),
			PearlsPerCareHour:    item.PearlsPerCareHour,
			MatureAfterCareHours: item.MatureAfterCareHours,
		})
	}
	return response, nil
}

func protoState(state reef.State) *aquariumv1.AquariumState {
	response := &aquariumv1.AquariumState{
		Version:             uint32(state.Version),
		Revision:            state.Revision,
		LastUpdatedAtUnixMs: state.LastUpdatedAt.UnixMilli(),
		Pearls:              int64(state.Pearls),
		Fullness:            state.Fullness,
		Cleanliness:         state.Cleanliness,
		Creatures:           make([]*aquariumv1.Creature, 0, len(state.Creatures)),
	}
	for _, creature := range state.Creatures {
		response.Creatures = append(response.Creatures, &aquariumv1.Creature{
			SpeciesId:     creature.SpeciesID,
			GrowthHours:   creature.GrowthHours,
			PendingPearls: creature.PendingPearls,
		})
	}
	return response
}

// Keep storage internals and filesystem paths out of public error messages.
func reefError(err error) error {
	code, message := connect.CodeInternal, "aquarium operation failed"
	switch {
	case errors.Is(err, context.Canceled):
		code, message = connect.CodeCanceled, "request canceled"
	case errors.Is(err, context.DeadlineExceeded):
		code, message = connect.CodeDeadlineExceeded, "request deadline exceeded"
	case errors.Is(err, reef.ErrInvalidArgument):
		code, message = connect.CodeInvalidArgument, "invalid aquarium command"
	case errors.Is(err, reef.ErrRevisionConflict):
		code, message = connect.CodeAborted, "aquarium revision changed; fetch a fresh snapshot"
	case errors.Is(err, reef.ErrRequestIDConflict):
		code, message = connect.CodeAlreadyExists, "request ID was already used for a different command"
	case errors.Is(err, reef.ErrCorrupt):
		code, message = connect.CodeDataLoss, "aquarium save is corrupt; restore a valid backup"
	case errors.Is(err, reef.ErrUnavailable):
		code, message = connect.CodeUnavailable, "aquarium storage is unavailable"
	}
	return connect.NewError(code, errors.New(message))
}
