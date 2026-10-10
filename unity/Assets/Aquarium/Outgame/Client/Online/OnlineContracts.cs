using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aquarium.Online
{
    // Deliberately matches Connect's protobuf JSON names. 64-bit integers remain
    // decimal strings: neither Unity JsonUtility nor JavaScript numbers may round revisions.
    [Serializable] public sealed class ServerAquariumState
    {
        public uint version;
        public string revision;
        public string lastUpdatedAtUnixMs;
        public string pearls;
        public double fullness;
        public double cleanliness;
        public ServerCreature[] creatures;
    }
    [Serializable] public sealed class ServerCreature
    {
        public string speciesId;
        public double growthHours;
        public double pendingPearls;
    }
    [Serializable] public sealed class ServerCatalog
    {
        public ServerSpeciesDefinition[] species;
        public ServerSimulationRules rules;
    }
    [Serializable] public sealed class ServerSpeciesDefinition
    {
        public string id;
        public string displayName;
        public string price;
        public double pearlsPerCareHour;
        public double matureAfterCareHours;
    }
    [Serializable] public sealed class ServerSimulationRules
    {
        public uint capacity;
        public string walletCapacity;
        public double rewardCapacity;
        public double offlineCapSeconds;
        public double fullnessLossPerHour;
        public double cleanlinessLossPerHour;
        public double careThreshold;
        public uint replayCapacity;
    }
    [Serializable] public sealed class ServerAdvanceResult
    {
        public double elapsedSeconds;
        public double simulatedSeconds;
        public bool clockRolledBack;
        public bool wasCapped;
    }
    [Serializable] public sealed class ServerGetAquariumResponse { public ServerAquariumState state; }
    [Serializable] public sealed class ServerApplyCommandRequest
    {
        public string requestId;
        public string expectedRevision;
        public string action;
        public string speciesId;
    }
    [Serializable] public sealed class ServerApplyCommandResponse
    {
        public ServerAquariumState state;
        public bool success;
        public string message;
        public string amount;
        public ServerAdvanceResult advance;
        public bool replayed;
    }
    [Serializable] public sealed class ServerRpcError { public string code; public string message; }

    public interface IOnlineJsonCodec
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string json);
    }

    public interface IOnlineTransport
    {
        Task<OnlineHttpResponse> PostAsync(Uri endpoint, string devPlayer, string json,
            CancellationToken cancellationToken);
    }

    public sealed class OnlineHttpResponse
    {
        public int StatusCode { get; }
        public string Body { get; }
        public OnlineHttpResponse(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }
    }

    /// <summary>Stores only a command journal, never aquarium state or local rewards.</summary>
    public interface IPendingCommandStore
    {
        bool TryRead(out string contents);
        void Write(string contents);
        void Clear();
    }

    public enum OnlineOperationKind { Ready, Applied, GameplayRejected, Conflict, Pending, Failed, Busy, StorageError }

    public sealed class OnlineOperationResult
    {
        public OnlineOperationKind Kind { get; }
        public string Message { get; }
        public string ErrorCode { get; }
        public bool CommandAccepted { get; }
        public bool GameplaySucceeded { get; }
        public bool Replayed { get; }
        public long Amount { get; }
        public OnlineOperationResult(OnlineOperationKind kind, string message, string errorCode = null,
            bool commandAccepted = false, bool gameplaySucceeded = false, bool replayed = false, long amount = 0)
        {
            Kind = kind; Message = message; ErrorCode = errorCode;
            CommandAccepted = commandAccepted; GameplaySucceeded = gameplaySucceeded;
            Replayed = replayed; Amount = amount;
        }
    }
}
