using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aquarium.Online
{
    [Serializable] public sealed class PendingCommandJournal
    {
        public int version;
        public string endpoint;
        public string devPlayer;
        public string requestJson;
        public string checksum;
    }

    /// <summary>
    /// Serialized server-authoritative session. Call from Unity's main thread so Changed
    /// callbacks return to its synchronization context. No timers, local simulation or saves.
    /// Reconnection only reads. A possibly committed command requires explicit exact retry.
    /// </summary>
    public sealed class AquariumOnlineSession
    {
        private readonly ReefApiClient api;
        private readonly IPendingCommandStore store;
        private ServerAquariumState snapshot;
        private ServerCatalog catalog;
        private PendingCommandJournal pending;
        private bool fatalJournalError;
        private int busy;

        // Defensive copies prevent view code from changing the next command's revision,
        // server balances or authoritative catalog in this session.
        public ServerAquariumState Snapshot => Copy(snapshot);
        public ServerCatalog Catalog => Copy(catalog);
        public bool IsBusy => Volatile.Read(ref busy) != 0;
        public bool IsConnected { get; private set; }
        public bool HasPendingCommand => pending != null;
        public bool HasStorageError { get; private set; }
        public bool CanIssueCommands => !IsBusy && IsConnected && snapshot != null && catalog != null && !HasPendingCommand && !HasStorageError;
        public bool CanRetryPending => !IsBusy && pending != null && !fatalJournalError;
        public string PendingRequestId => pending == null ? null : DecodePending().requestId;
        public string PendingRequestJson => pending?.requestJson;
        public string StatusMessage { get; private set; } = "Connect to the local development server.";
        public OnlineOperationResult LastResult { get; private set; }
        public event Action Changed;

        public AquariumOnlineSession(ReefApiClient api, IPendingCommandStore store)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            try
            {
                if (store.TryRead(out var json))
                {
                    if (json == null || json.Length > FilePendingCommandStore.MaximumJournalBytes)
                        throw new InvalidDataException("Invalid pending journal size.");
                    var restored = api.Codec.Deserialize<PendingCommandJournal>(json);
                    if (restored == null || restored.version != 1 || restored.endpoint != api.Options.Endpoint ||
                        restored.devPlayer != api.Options.DevPlayer || string.IsNullOrEmpty(restored.requestJson) ||
                        restored.requestJson.Length > 4096 || restored.checksum != Checksum(restored))
                        throw new InvalidDataException("Pending journal is invalid or belongs to a different endpoint/player.");
                    OnlineValidation.ValidateCommand(api.Codec.Deserialize<ServerApplyCommandRequest>(restored.requestJson));
                    pending = restored;
                    StatusMessage = "A previous command is unresolved. Connect, then retry the same command.";
                }
            }
            catch (Exception)
            {
                HasStorageError = fatalJournalError = true;
                StatusMessage = "Pending command journal cannot be safely read for this server/player. Preserve it and check the file before restarting.";
            }
        }

        public Task<OnlineOperationResult> ConnectAsync(CancellationToken cancellationToken = default) =>
            RunAsync(() => RefreshCoreAsync(true, cancellationToken));

        public Task<OnlineOperationResult> RefreshAsync(CancellationToken cancellationToken = default) =>
            RunAsync(() => RefreshCoreAsync(false, cancellationToken));

        public Task<OnlineOperationResult> FeedAsync(CancellationToken cancellationToken = default) =>
            CommandAsync("ACTION_FEED", "", cancellationToken);
        public Task<OnlineOperationResult> CleanAsync(CancellationToken cancellationToken = default) =>
            CommandAsync("ACTION_CLEAN", "", cancellationToken);
        public Task<OnlineOperationResult> CollectAsync(CancellationToken cancellationToken = default) =>
            CommandAsync("ACTION_COLLECT", "", cancellationToken);
        public Task<OnlineOperationResult> AdoptAsync(string speciesId, CancellationToken cancellationToken = default) =>
            CommandAsync("ACTION_ADOPT", speciesId, cancellationToken);

        public Task<OnlineOperationResult> RetryPendingAsync(CancellationToken cancellationToken = default) => RunAsync(async () =>
        {
            if (fatalJournalError) return StorageFailure();
            if (pending == null) return new OnlineOperationResult(OnlineOperationKind.Failed, "There is no pending command to retry.");
            // Retry a failed local journal write before sending, never assume it was durable.
            if (!PersistPending()) return StorageFailure();
            if (catalog == null) catalog = await api.GetCatalogAsync(cancellationToken);
            return await SendPendingAsync(cancellationToken);
        });

        private Task<OnlineOperationResult> CommandAsync(string action, string speciesId, CancellationToken cancellationToken) => RunAsync(async () =>
        {
            if (HasStorageError) return StorageFailure();
            if (pending != null) return new OnlineOperationResult(OnlineOperationKind.Pending, "Resolve the pending command with Retry Same Command first.");
            if (!IsConnected || snapshot == null || catalog == null)
                return new OnlineOperationResult(OnlineOperationKind.Failed, "Reconnect before sending a new command.");
            if (action == "ACTION_ADOPT")
            {
                bool found = false;
                foreach (var species in catalog.species) if (species.id == speciesId) { found = true; break; }
                if (!found) return new OnlineOperationResult(OnlineOperationKind.Failed, "Select a species from the server catalog.", "invalid_argument");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var request = new ServerApplyCommandRequest
            {
                requestId = Guid.NewGuid().ToString("N"), expectedRevision = snapshot.revision,
                action = action, speciesId = speciesId ?? ""
            };
            OnlineValidation.ValidateCommand(request);
            pending = new PendingCommandJournal
            {
                version = 1, endpoint = api.Options.Endpoint, devPlayer = api.Options.DevPlayer,
                requestJson = api.Codec.Serialize(request)
            };
            pending.checksum = Checksum(pending);
            if (!PersistPending()) return StorageFailure();
            return await SendPendingAsync(cancellationToken);
        });

        private async Task<OnlineOperationResult> RefreshCoreAsync(bool refreshCatalog, CancellationToken cancellationToken)
        {
            if (refreshCatalog || catalog == null) catalog = await api.GetCatalogAsync(cancellationToken);
            AcceptSnapshot(await api.GetAquariumAsync(cancellationToken), true);
            IsConnected = true;
            if (HasStorageError) return StorageFailure();
            if (pending != null) return new OnlineOperationResult(OnlineOperationKind.Pending,
                "Snapshot refreshed. A previous command is unresolved; Retry Same Command to learn its outcome.");
            return new OnlineOperationResult(OnlineOperationKind.Ready, "Connected to the local development server. State is server-authoritative.");
        }

        private async Task<OnlineOperationResult> SendPendingAsync(CancellationToken cancellationToken)
        {
            ServerApplyCommandResponse response;
            try
            {
                response = await api.ApplyRawCommandAsync(pending.requestJson, cancellationToken);
                var expected = OnlineValidation.Revision(DecodePending().expectedRevision);
                if (expected == ulong.MaxValue || OnlineValidation.Revision(response.state.revision) != expected + 1)
                    throw new InvalidDataException("Command result has an unexpected revision.");
                AcceptSnapshot(response.state);
            }
            catch (OnlineRpcException exception) when (exception.Code == "aborted")
            {
                if (!ClearPending()) return StorageFailure();
                IsConnected = false;
                try
                {
                    AcceptSnapshot(await api.GetAquariumAsync(cancellationToken), true);
                    IsConnected = true;
                    return new OnlineOperationResult(OnlineOperationKind.Conflict,
                        "The server state changed. Snapshot refreshed. Review it and choose a new action; nothing was resubmitted.", "aborted");
                }
                catch (ServerStateRegressionException)
                {
                    return RegressionFailure();
                }
                catch (Exception)
                {
                    return new OnlineOperationResult(OnlineOperationKind.Conflict,
                        "The command conflicted and was not resubmitted. Reconnect to refresh before choosing a new action.", "aborted");
                }
            }
            catch (OnlineRpcException exception) when (DefiniteRejection(exception.Code))
            {
                if (!ClearPending()) return StorageFailure();
                IsConnected = false;
                return new OnlineOperationResult(OnlineOperationKind.Failed,
                    "Server rejected the command (" + exception.Code + "). Reconnect before choosing a new action.", exception.Code);
            }
            // Other failures escape to RunAsync. The journal remains unchanged, including
            // cancellation, malformed success, 5xx, disconnected socket and unknown HTTP errors.
            IsConnected = true;
            if (!ClearPending()) return StorageFailure();
            var result = new OnlineOperationResult(response.success ? OnlineOperationKind.Applied : OnlineOperationKind.GameplayRejected,
                SafeGameplayMessage(response.message, response.success), commandAccepted: true,
                gameplaySucceeded: response.success, replayed: response.replayed,
                amount: long.Parse(response.amount, CultureInfo.InvariantCulture));
            if (response.replayed)
            {
                // Receipt snapshots can be old. Never roll back; read current state as well.
                try { AcceptSnapshot(await api.GetAquariumAsync(cancellationToken), true); }
                catch (ServerStateRegressionException)
                {
                    IsConnected = false;
                    return new OnlineOperationResult(result.Kind, result.Message + " " + RegressionFailure().Message,
                        commandAccepted: true, gameplaySucceeded: result.GameplaySucceeded, replayed: true, amount: result.Amount);
                }
                catch (Exception)
                {
                    IsConnected = false;
                    return new OnlineOperationResult(result.Kind, result.Message + " Outcome confirmed; reconnect to refresh the latest state.",
                        commandAccepted: true, gameplaySucceeded: result.GameplaySucceeded, replayed: true, amount: result.Amount);
                }
            }
            return result;
        }

        private async Task<OnlineOperationResult> RunAsync(Func<Task<OnlineOperationResult>> operation)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                return new OnlineOperationResult(OnlineOperationKind.Busy, "Another request is in progress.");
            NotifyChanged();
            try
            {
                OnlineOperationResult result;
                try { result = await operation(); }
                catch (ServerStateRegressionException)
                {
                    IsConnected = false;
                    result = RegressionFailure();
                }
                catch (Exception exception)
                {
                    IsConnected = false;
                    var code = (exception as OnlineRpcException)?.Code ?? (exception is OperationCanceledException ? "canceled" : "connection_error");
                    result = new OnlineOperationResult(pending != null ? OnlineOperationKind.Pending : OnlineOperationKind.Failed,
                        pending != null
                            ? "Command outcome is unknown (" + code + "). Retry Same Command; its ID and payload are preserved."
                            : "Cannot refresh from the server (" + code + "). Last snapshot is stale; reconnect to try again.", code);
                }
                LastResult = result;
                StatusMessage = result.Message;
                return result;
            }
            finally { Interlocked.Exchange(ref busy, 0); NotifyChanged(); }
        }

        private void AcceptSnapshot(ServerAquariumState incoming, bool freshRead = false)
        {
            OnlineValidation.ValidateAgainstCatalog(incoming, catalog);
            var incomingRevision = OnlineValidation.Revision(incoming.revision);
            if (snapshot != null && freshRead)
            {
                var existingRevision = OnlineValidation.Revision(snapshot.revision);
                if (incomingRevision < existingRevision ||
                    incomingRevision == existingRevision && api.Codec.Serialize(incoming) != api.Codec.Serialize(snapshot))
                    throw new ServerStateRegressionException();
            }
            if (snapshot == null || incomingRevision > OnlineValidation.Revision(snapshot.revision))
                snapshot = incoming;
        }

        private sealed class ServerStateRegressionException : Exception { }

        private OnlineOperationResult RegressionFailure() => new OnlineOperationResult(OnlineOperationKind.Failed,
            "The server snapshot regressed or changed without a new revision. Commands are locked. Check the server data before restarting this session." +
            (pending != null ? " The unresolved command journal has been preserved." : ""), "server_state_regressed");

        private bool PersistPending()
        {
            try { store.Write(api.Codec.Serialize(pending)); HasStorageError = false; return true; }
            catch (Exception) { HasStorageError = true; return false; }
        }

        private bool ClearPending()
        {
            try { store.Clear(); pending = null; HasStorageError = false; return true; }
            catch (Exception) { HasStorageError = true; return false; }
        }

        private OnlineOperationResult StorageFailure() => new OnlineOperationResult(OnlineOperationKind.StorageError,
            fatalJournalError
                ? "Pending command journal cannot be safely read for this server/player. Preserve it and check the file before restarting."
                : "Pending command journal could not be saved or cleared. Commands are locked; fix storage and Retry Same Command.", "local_storage");

        private ServerApplyCommandRequest DecodePending() => api.Codec.Deserialize<ServerApplyCommandRequest>(pending.requestJson);

        private static bool DefiniteRejection(string code) => code == "invalid_argument" || code == "already_exists" ||
            code == "unauthenticated" || code == "permission_denied" || code == "unimplemented" || code == "not_found" ||
            code == "failed_precondition" || code == "out_of_range";

        private static string SafeGameplayMessage(string message, bool success)
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > 512)
                return success ? "Command accepted." : "The server accepted the request, but the gameplay action had no effect.";
            // Unity rich text is presentation only; do not allow server text to inject tags.
            return message.Replace("<", "").Replace(">", "");
        }

        private static string Checksum(PendingCommandJournal journal)
        {
            using (var hash = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(journal.version.ToString(CultureInfo.InvariantCulture) + "\n" +
                    journal.endpoint + "\n" + journal.devPlayer + "\n" + journal.requestJson);
                return Convert.ToBase64String(hash.ComputeHash(bytes));
            }
        }

        private T Copy<T>(T value) where T : class => value == null ? null : api.Codec.Deserialize<T>(api.Codec.Serialize(value));

        private void NotifyChanged()
        {
            var callbacks = Changed;
            if (callbacks == null) return;
            // Presentation exceptions must not turn a committed command into a new intent.
            foreach (Action callback in callbacks.GetInvocationList())
                try { callback(); } catch (Exception) { }
        }
    }
}
