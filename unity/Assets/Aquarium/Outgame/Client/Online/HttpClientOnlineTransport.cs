using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aquarium.Online
{
    /// <summary>Native desktop Connect/JSON transport. WebGL is deliberately unsupported.</summary>
    public sealed class HttpClientOnlineTransport : IOnlineTransport, IDisposable
    {
        public const int MaximumResponseBytes = 1024 * 1024;
        private readonly HttpClient client;
        private readonly TimeSpan requestTimeout;

        public HttpClientOnlineTransport(TimeSpan? timeout = null)
        {
            // Do not send the dev identity to redirects or a configured system proxy.
            requestTimeout = timeout ?? TimeSpan.FromSeconds(15);
            if (requestTimeout <= TimeSpan.Zero || requestTimeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public async Task<OnlineHttpResponse> PostAsync(Uri endpoint, string devPlayer, string json,
            CancellationToken cancellationToken)
        {
            if (endpoint == null || endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" ||
                !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new ArgumentException("Only the local development server is supported.", nameof(endpoint));
            if (!OnlineValidation.IsIdentifier(devPlayer, 64)) throw new ArgumentException("Invalid development player.");
            // HttpClient.Timeout alone ends at headers with ResponseHeadersRead. Keep a
            // deadline token alive through every body read, including a stalled server.
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                deadline.CancelAfter(requestTimeout);
                var requestToken = deadline.Token;
                request.Headers.Add("Connect-Protocol-Version", "1");
                request.Headers.Add("X-Aquarium-Dev-Player", devPlayer);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false))
                {
                    if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                        throw new InvalidDataException("Server response exceeds the size limit.");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        var bytes = new byte[8192];
                        int count;
                        while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, requestToken).ConfigureAwait(false)) != 0)
                        {
                            if (buffer.Length + count > MaximumResponseBytes)
                                throw new InvalidDataException("Server response exceeds the size limit.");
                            buffer.Write(bytes, 0, count);
                        }
                        return new OnlineHttpResponse((int)response.StatusCode,
                            new UTF8Encoding(false, true).GetString(buffer.ToArray()));
                    }
                }
            }
        }

        public void Dispose() { client.Dispose(); }
    }
}
