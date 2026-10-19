using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin.Services;

namespace SpherePlayer;

public sealed record HostState(bool Ready, string Status, int? Error, string Title, int Ends, int Errors, double Time = 0, double Dur = 0);

// Runs SpherePlayer.PlayerHost.exe (CEF, audio only) as a child process and talks to it over stdin/stdout.
public sealed class PlayerProcess : IDisposable
{
    private readonly HostInstaller installer;
    private readonly string cacheDir;
    private readonly IPluginLog log;
    private Process? proc;

    public ConcurrentQueue<HostState> Events { get; } = new();
    public HostState State { get; private set; } = new(false, "not started", null, "", 0, 0);
    public string Problem { get; private set; } = "";
    public bool Running => proc is { HasExited: false };
    public bool HostInstalled => installer.Ready;
    public HostInstaller Installer => installer;
    public int Generation { get; private set; }
    private bool videoOn;

    public PlayerProcess(HostInstaller installer, string cacheDir, IPluginLog log)
    {
        this.installer = installer;
        this.cacheDir = cacheDir;
        this.log = log;
    }

    public void Start()
    {
        if (Running) return;
        if (!HostInstalled) { Problem = "The player component is not downloaded yet."; return; }
        var exePath = installer.ExePath;
        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
            };
            psi.ArgumentList.Add(cacheDir);
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => OnLine(e.Data);
            proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log.Debug("[host err] {Line}", e.Data); };
            proc.Exited += (_, _) => { State = State with { Ready = false, Status = "host exited" }; };
            proc.Start();
            Generation++;
            videoOn = false;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            Problem = "";
            State = State with { Status = "starting" };
        }
        catch (Exception ex)
        {
            Problem = "Could not start the player host: " + ex.Message;
            log.Error(ex, "Player host start failed");
        }
    }

    private void OnLine(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (!line.StartsWith("state ")) { log.Information("[host] {Line}", line); return; }
        try
        {
            using var doc = JsonDocument.Parse(line[6..]);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ready", out _)) return;
            int? err = r.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null;
            var s = new HostState(r.GetProperty("ready").GetBoolean(), r.GetProperty("status").GetString() ?? "", err,
                r.GetProperty("title").GetString() ?? "", r.GetProperty("ends").GetInt32(), r.GetProperty("errors").GetInt32(),
                r.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : 0,
                r.TryGetProperty("dur", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0);
            State = s;
            Events.Enqueue(s);
        }
        catch (Exception ex) { log.Warning(ex, "Bad host line {Line}", line); }
    }

    public void Send(string cmd)
    {
        if (!Running) return;
        try { proc!.StandardInput.WriteLine(cmd); proc.StandardInput.Flush(); }
        catch (Exception ex) { log.Warning(ex, "Send failed"); }
    }

    public void Play(string videoId, int volume, double startSeconds = 0, int fadeOutMs = 0, int fadeInMs = 0)
    {
        Send($"volume {volume}");
        Send(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"play {videoId} {startSeconds:0.#} {fadeOutMs} {fadeInMs}"));
    }
    public void Pause() => Send("pause");
    public void Resume() => Send("resume");
    public void SetVideo(bool on) { if (on == videoOn) return; videoOn = on; Send("video " + (on ? "on" : "off")); }
    public void Stop() => Send("stop");
    public void Mouse(string kind, int x, int y, int wheel = 0) => Send($"mouse {kind} {x} {y} {wheel}");
    public void SetVolume(int v) => Send($"volume {v}");

    public void Dispose()
    {
        try { Send("quit"); if (proc is { HasExited: false }) { if (!proc.WaitForExit(1500)) proc.Kill(true); } }
        catch { }
        proc?.Dispose();
    }
}
