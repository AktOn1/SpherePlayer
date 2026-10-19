using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace SpherePlayer;

// What the plugin downloads on first start: the Chromium based player host (about 360 MB unpacked).
public sealed record HostManifest(string Version, string Url, string Sha256, long Size)
{
    public static HostManifest LoadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("host-manifest.json")
            ?? throw new InvalidOperationException("host-manifest.json is not embedded");
        var doc = JsonDocument.Parse(s).RootElement;
        return new HostManifest(doc.GetProperty("version").GetString()!, doc.GetProperty("url").GetString()!,
            doc.GetProperty("sha256").GetString()!, doc.GetProperty("size").GetInt64());
    }
}

public enum HostPhase { Idle, Downloading, Verifying, Unpacking, Ready, Failed }

// Downloads the player host zip, checks its SHA-256, and unpacks it next to the plugin settings. Retries a few times, then waits for Retry().
public sealed class HostInstaller : IDisposable
{
    private const string VersionFile = ".host-version";
    private const string ExeName = "SpherePlayer.PlayerHost.exe";

    private readonly string hostDir;
    private readonly HostManifest manifest;
    private readonly IPluginLog log;
    private readonly HttpClient http;
    private readonly TimeSpan[] retryDelays;
    private readonly CancellationTokenSource cts = new();
    private readonly object gate = new();
    private Task? running;

    private volatile HostPhase phase = HostPhase.Idle;
    private long done;
    private long total;
    private volatile string error = "";
    private volatile int attempt;

    public HostInstaller(string hostDir, HostManifest manifest, IPluginLog log, HttpMessageHandler? handler = null, TimeSpan[]? retryDelays = null)
    {
        this.hostDir = hostDir;
        this.manifest = manifest;
        this.log = log;
        http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SpherePlayerPlugin/1");
        this.retryDelays = retryDelays ?? new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10) };
        if (IsInstalled()) phase = HostPhase.Ready;
    }

    public HostPhase Phase => phase;
    public bool Ready => phase == HostPhase.Ready;
    public long BytesDone => Interlocked.Read(ref done);
    public long BytesTotal => Interlocked.Read(ref total);
    public string Error => error;
    public int Attempt => attempt;
    public int Attempts => retryDelays.Length + 1;
    public string ExePath => Path.Combine(hostDir, ExeName);

    private bool IsInstalled() =>
        File.Exists(ExePath) && File.Exists(Path.Combine(hostDir, VersionFile)) && File.ReadAllText(Path.Combine(hostDir, VersionFile)).Trim() == manifest.Version;

    // Starts the download unless it is installed or already running. Safe to call every frame.
    public void Start()
    {
        lock (gate)
        {
            if (phase == HostPhase.Ready || (running is { IsCompleted: false })) return;
            if (IsInstalled()) { phase = HostPhase.Ready; return; }
            error = "";
            phase = HostPhase.Downloading;
            running = Task.Run(() => RunAsync(cts.Token));
        }
    }

    public void Retry() => Start();

    public Task WaitAsync() => running ?? Task.CompletedTask;

    private async Task RunAsync(CancellationToken ct)
    {
        var part = hostDir + ".zip.part";
        var staging = hostDir + ".new";
        try
        {
            for (var i = 0; i <= retryDelays.Length; i++)
            {
                attempt = i + 1;
                try
                {
                    phase = HostPhase.Downloading;
                    await DownloadAsync(part, ct);
                    phase = HostPhase.Unpacking;
                    await Task.Run(() => Unpack(part, staging), ct);
                    phase = HostPhase.Ready;
                    log.Information("Player host {Version} installed in {Dir}", manifest.Version, hostDir);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    error = Describe(ex);
                    log.Warning(ex, "Player host install failed (attempt {N} of {Max})", attempt, Attempts);
                    TryDelete(part);
                    if (i == retryDelays.Length) break;
                    phase = HostPhase.Downloading;
                    await Task.Delay(retryDelays[i], ct);
                }
            }
            phase = HostPhase.Failed;
        }
        finally
        {
            TryDelete(part);
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    private async Task DownloadAsync(string part, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(part)!);
        Interlocked.Exchange(ref done, 0);
        Interlocked.Exchange(ref total, manifest.Size);
        using var resp = await http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is { } len && len > 0) Interlocked.Exchange(ref total, len);
        using var sha = SHA256.Create();
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buf = new byte[81920];
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                sha.TransformBlock(buf, 0, n, null, 0);
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                Interlocked.Add(ref done, n);
            }
        }
        phase = HostPhase.Verifying;
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var actual = Convert.ToHexString(sha.Hash!);
        if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The download is damaged or not the expected file (checksum mismatch).");
    }

    private void Unpack(string zipPath, string staging)
    {
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var e in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(staging, e.FullName));
                if (!target.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("Unsafe path in the download: " + e.FullName);
                if (e.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, true);
            }
        }
        if (!File.Exists(Path.Combine(staging, ExeName))) throw new InvalidDataException("The download does not contain " + ExeName);
        File.WriteAllText(Path.Combine(staging, VersionFile), manifest.Version);
        if (Directory.Exists(hostDir)) Directory.Delete(hostDir, true);
        Directory.Move(staging, hostDir);
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException h when h.StatusCode is { } c => $"The server answered {(int)c} {c}.",
        HttpRequestException h => "Could not connect: " + (h.InnerException?.Message ?? h.Message),
        _ => ex.Message,
    };

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    public void Dispose()
    {
        cts.Cancel();
        try { running?.Wait(2000); } catch { }
        http.Dispose();
        cts.Dispose();
    }
}
