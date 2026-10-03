using System;

namespace Aquarium.Online
{
    /// <summary>INSECURE local development identity. Never use for real players or public servers.</summary>
    public sealed class DevServerOptions
    {
        public const string DefaultEndpoint = "http://127.0.0.1:8081";
        public Uri BaseUri { get; }
        public string Endpoint => BaseUri.AbsoluteUri.TrimEnd('/');
        public string DevPlayer { get; }

        public DevServerOptions(string endpoint, string devPlayer)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1" ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Development endpoint must be http://127.0.0.1:PORT with no path or credentials.", nameof(endpoint));
            if (!OnlineValidation.IsIdentifier(devPlayer, 64))
                throw new ArgumentException("Development player must contain 1–64 ASCII letters, digits, underscores or hyphens.", nameof(devPlayer));
            BaseUri = uri;
            DevPlayer = devPlayer;
        }
    }
}
