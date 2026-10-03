using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

// This launches the shipping development entry point, not an in-process HTTP
// substitute. Only this class's own child process is ever terminated.
internal sealed class LiveServer : IAsyncDisposable
{
    public const string Endpoint = "http://127.0.0.1:8081";
    public readonly string Root;
    public readonly string DataDirectory;
    private readonly string binary;
    private readonly StringBuilder log = new();
    private Process process;

    public LiveServer(string binary)
    {
        this.binary = Path.GetFullPath(binary);
        if (!File.Exists(this.binary)) throw new FileNotFoundException("Build server/cmd/reef-local and pass --server /absolute/path/to/reef-local.", this.binary);
        Root = Path.Combine(Path.GetTempPath(), "aquarium-online-" + Guid.NewGuid().ToString("N"));
        DataDirectory = Path.Combine(Root, "server-data");
        Directory.CreateDirectory(DataDirectory);
    }

    public string ClientPath(string name) => Path.Combine(Root, "client-" + name + ".json");

    public async Task StartAsync()
    {
        if (process != null) throw new InvalidOperationException("Server already started.");
        // reef-local intentionally accepts only its fixed numeric loopback
        // endpoint. Fail rather than reuse, interrupt, or kill another process.
        var probe = new TcpListener(IPAddress.Loopback, 8081);
        try { probe.Start(); }
        catch (SocketException error) { throw new InvalidOperationException("Live integration tests require free 127.0.0.1:8081. Stop your own reef-local instance and rerun; no existing process was touched.", error); }
        finally { probe.Stop(); }

        var info = new ProcessStartInfo(binary) {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--dev");
        info.ArgumentList.Add("--data-dir");
        info.ArgumentList.Add(DataDirectory); // Absolute even after process restart.
        process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, value) => { if (value.Data != null) lock (log) log.AppendLine(value.Data); };
        process.ErrorDataReceived += (_, value) => { if (value.Data != null) lock (log) log.AppendLine(value.Data); };
        if (!process.Start()) throw new InvalidOperationException("Could not start reef-local.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (process.HasExited) throw new InvalidOperationException("reef-local exited during startup: " + Logs);
            try
            {
                using var response = await client.GetAsync(Endpoint + "/healthz");
                if (response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("development-only", StringComparison.Ordinal)
                    && Logs.Contains("DEVELOPMENT ONLY: listening on " + Endpoint, StringComparison.Ordinal))
                {
                    if (process.HasExited) throw new InvalidOperationException("reef-local exited during readiness: " + Logs);
                    return;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(30);
        }
        throw new TimeoutException("reef-local did not become healthy: " + Logs);
    }

    public string Logs { get { lock (log) return log.ToString(); } }

    public async Task StopAsync()
    {
        if (process == null) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        process.Dispose();
        process = null;
    }

    public async Task RestartAsync() { await StopAsync(); await StartAsync(); }

    // An old save is the fixture, not a server clock override. Production code
    // still validates its checksum/schema and uses time.Now for all progression.
    // It is written only before the server starts, into this test's private dir.
    public void SeedReturningPlayer(string player)
    {
        if (process != null) throw new InvalidOperationException("Never modify a live server's files.");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new {
            schema_version = 1,
            player_id = player,
            state = new {
                version = 1,
                revision = 1UL,
                last_updated_at = DateTime.UtcNow.AddHours(-1).ToString("O"),
                pearls = 30,
                fullness = 75.0,
                cleanliness = 85.0,
                creatures = new[] { new { species_id = "tide_sprite", growth_hours = 0.0, pending_pearls = 0.0 } },
            },
            receipts = Array.Empty<object>(),
        });
        using var content = JsonDocument.Parse(payload);
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new {
            format_version = 1,
            sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            payload = content.RootElement,
        });
        var filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(player))).ToLowerInvariant() + ".json";
        File.WriteAllBytes(Path.Combine(DataDirectory, filename), envelope);
    }

    public void ArchivePlayerToSimulateReset(string player)
    {
        if (process != null) throw new InvalidOperationException("Never modify a live server's files.");
        var filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(player))).ToLowerInvariant() + ".json";
        File.Move(Path.Combine(DataDirectory, filename), Path.Combine(Root, "reset-" + filename));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Directory.Delete(Root, recursive: true);
    }
}
