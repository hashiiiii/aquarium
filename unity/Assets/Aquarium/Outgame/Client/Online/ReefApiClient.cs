using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Aquarium.Online
{
    public sealed class OnlineRpcException : Exception
    {
        public string Code { get; }
        public int StatusCode { get; }
        public OnlineRpcException(string code, int statusCode) : base("Aquarium RPC failed: " + code)
        { Code = code; StatusCode = statusCode; }
    }

    /// <summary>Thin Connect unary JSON client for aquarium.v1, with no gameplay simulation.</summary>
    public sealed class ReefApiClient
    {
        public DevServerOptions Options { get; }
        public IOnlineJsonCodec Codec { get; }
        private readonly IOnlineTransport transport;
        private const string Service = "/aquarium.v1.AquariumService/";

        public ReefApiClient(DevServerOptions options, IOnlineJsonCodec codec, IOnlineTransport transport)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<ServerAquariumState> GetAquariumAsync(CancellationToken cancellationToken = default)
        {
            var response = await PostAsync<ServerGetAquariumResponse>("GetAquarium", "{}", cancellationToken);
            OnlineValidation.ValidateState(response.state);
            return response.state;
        }

        public async Task<ServerCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            var response = await PostAsync<ServerCatalog>("GetCatalog", "{}", cancellationToken);
            OnlineValidation.ValidateCatalog(response);
            return response;
        }

        public async Task<ServerApplyCommandResponse> ApplyRawCommandAsync(string exactJson, CancellationToken cancellationToken = default)
        {
            var response = await PostAsync<ServerApplyCommandResponse>("ApplyCommand", exactJson, cancellationToken);
            OnlineValidation.ValidateState(response.state);
            response.amount = OnlineValidation.NonnegativeInteger(response.amount, "amount");
            if (response.advance == null) throw new InvalidDataException("Missing command advancement metadata.");
            OnlineValidation.Nonnegative(response.advance.elapsedSeconds);
            OnlineValidation.Nonnegative(response.advance.simulatedSeconds);
            return response;
        }

        private async Task<T> PostAsync<T>(string method, string json, CancellationToken cancellationToken) where T : class
        {
            var response = await transport.PostAsync(new Uri(Options.BaseUri, Service + method), Options.DevPlayer, json, cancellationToken);
            if (response == null || string.IsNullOrEmpty(response.Body) || response.Body.Length > HttpClientOnlineTransport.MaximumResponseBytes)
                throw new InvalidDataException("Missing or oversized server response.");
            if (response.StatusCode != 200)
            {
                ServerRpcError error = null;
                try { error = Codec.Deserialize<ServerRpcError>(response.Body); } catch (Exception) { }
                // An unrecognized body is ambiguous, so callers must retain pending intent.
                var code = error?.code;
                if (!MatchesStatus(code, response.StatusCode)) code = "unknown";
                throw new OnlineRpcException(code, response.StatusCode);
            }
            T decoded;
            try { decoded = Codec.Deserialize<T>(response.Body); }
            catch (Exception exception) { throw new InvalidDataException("Invalid server JSON.", exception); }
            if (decoded == null) throw new InvalidDataException("Missing server response.");
            return decoded;
        }

        private static bool MatchesStatus(string code, int status)
        {
            switch (code)
            {
                case "canceled": return status == 499;
                case "unknown": case "internal": case "data_loss": return status == 500;
                case "invalid_argument": case "failed_precondition": case "out_of_range": return status == 400;
                case "deadline_exceeded": return status == 504;
                case "not_found": return status == 404;
                case "already_exists": case "aborted": return status == 409;
                case "permission_denied": return status == 403;
                case "resource_exhausted": return status == 429;
                case "unimplemented": return status == 501;
                case "unavailable": return status == 503;
                case "unauthenticated": return status == 401;
                default: return false;
            }
        }
    }
}
