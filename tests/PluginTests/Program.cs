using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace SpherePlayer.Tests;

// Plain console checks (no test framework): run with tests/run.sh. Exit code 1 if anything failed.
internal static class Program
{
    private static int failed, passed;

    private static void Check(bool ok, string what)
    {
        if (ok) { passed++; Console.WriteLine("  ok   " + what); }
        else { failed++; Console.WriteLine("  FAIL " + what); }
    }

    public class LogProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? m, object?[]? args) => m is { ReturnType.IsValueType: true } && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
    }

    private static readonly IPluginLog Log = DispatchProxy.Create<IPluginLog, LogProxy>();

    private static string Tmp(string name)
    {
        var d = Path.Combine(Path.GetTempPath(), "sphereplayer-tests", name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(d);
        return d;
    }

    // A tiny HTTP server: each request is answered by the handler, which gets the request number.
    private sealed class Server : IDisposable
    {
        private HttpListener l = new();
        public int Requests;
        public string Url { get; }
        public Server(Func<int, HttpListenerResponse, Task> handler)
        {
            var rnd = new Random();
            int port;
            while (true)
            {
                port = rnd.Next(20000, 60000);
                try { l = new HttpListener(); l.Prefixes.Add($"http://127.0.0.1:{port}/"); l.Start(); break; } catch (HttpListenerException) { }
            }
            Url = $"http://127.0.0.1:{port}/host.zip";
            _ = Task.Run(async () =>
            {
                while (l.IsListening)
                {
                    HttpListenerContext c;
                    try { c = await l.GetContextAsync(); } catch { return; }
                    var n = Interlocked.Increment(ref Requests);
                    try { await handler(n, c.Response); } catch { }
                    try { c.Response.Close(); } catch { }
                }
            });
        }
        public void Dispose() { l.Stop(); l.Close(); }
    }

    private static byte[] MakeZip(Dictionary<string, string> files)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, body) in files)
            {
                var e = z.CreateEntry(name);
                using var w = new StreamWriter(e.Open());
                w.Write(body);
            }
        return ms.ToArray();
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    private static async Task Serve(HttpListenerResponse r, byte[] body, int status = 200)
    {
        r.StatusCode = status;
        r.ContentLength64 = body.Length;
        await r.OutputStream.WriteAsync(body);
    }

    private static async Task WaitDone(HostInstaller h)
    {
        await h.WaitAsync();
    }

    private static async Task InstallerTests()
    {
        Console.WriteLine("HostInstaller");
        var good = MakeZip(new() { ["SpherePlayer.PlayerHost.exe"] = "exe", ["locales/en-US.pak"] = "x", ["licenses/a.txt"] = "l" });
        var noDelay = new[] { TimeSpan.Zero, TimeSpan.Zero };

        {   // success
            var dir = Tmp("ok"); var host = Path.Combine(dir, "host");
            using var srv = new Server((n, r) => Serve(r, good));
            using var h = new HostInstaller(host, new HostManifest("v1", srv.Url, Sha(good), good.Length), Log, retryDelays: noDelay);
            Check(!h.Ready, "not ready before the download");
            h.Start();
            await WaitDone(h);
            Check(h.Phase == HostPhase.Ready && h.Ready, "downloads, verifies and unpacks");
            Check(File.Exists(Path.Combine(host, "SpherePlayer.PlayerHost.exe")) && File.Exists(Path.Combine(host, "locales", "en-US.pak")), "files are in the host folder");
            Check(h.BytesDone == good.Length && h.BytesTotal == good.Length, "progress counts the whole file");
            Check(!File.Exists(host + ".zip.part") && !Directory.Exists(host + ".new"), "no temporary leftovers");
            using var h2 = new HostInstaller(host, new HostManifest("v1", "http://127.0.0.1:1/none", Sha(good), good.Length), Log, retryDelays: noDelay);
            Check(h2.Ready, "a second start finds it installed and downloads nothing");
            using var h3 = new HostInstaller(host, new HostManifest("v2", srv.Url, Sha(good), good.Length), Log, retryDelays: noDelay);
            Check(!h3.Ready, "a new host version is downloaded again");
        }
        {   // wrong checksum
            var dir = Tmp("bad"); var host = Path.Combine(dir, "host");
            var other = MakeZip(new() { ["SpherePlayer.PlayerHost.exe"] = "evil" });
            using var srv = new Server((n, r) => Serve(r, other));
            using var h = new HostInstaller(host, new HostManifest("v1", srv.Url, Sha(good), good.Length), Log, retryDelays: noDelay);
            h.Start();
            await WaitDone(h);
            Check(h.Phase == HostPhase.Failed && h.Error.Contains("checksum"), "checksum mismatch fails with a clear message");
            Check(srv.Requests == 3, "tried 3 times (" + srv.Requests + ")");
            Check(!Directory.Exists(host) && !File.Exists(host + ".zip.part"), "nothing was unpacked or left behind");
        }
        {   // transient server errors, then fine
            var dir = Tmp("flaky"); var host = Path.Combine(dir, "host");
            using var srv = new Server((n, r) => n < 3 ? Serve(r, Array.Empty<byte>(), 503) : Serve(r, good));
            using var h = new HostInstaller(host, new HostManifest("v1", srv.Url, Sha(good), good.Length), Log, retryDelays: noDelay);
            h.Start();
            await WaitDone(h);
            Check(h.Ready && h.Attempt == 3, "recovers after two 503 answers (attempt " + h.Attempt + ")");
        }
        {   // gives up, then Retry() works once the server is back
            var dir = Tmp("retry"); var host = Path.Combine(dir, "host");
            var up = false;
            using var srv = new Server((n, r) => up ? Serve(r, good) : Serve(r, Array.Empty<byte>(), 404));
            using var h = new HostInstaller(host, new HostManifest("v1", srv.Url, Sha(good), good.Length), Log, retryDelays: noDelay);
            h.Start();
            await WaitDone(h);
            Check(h.Phase == HostPhase.Failed && h.Error.Contains("404"), "gives up with the server's answer: " + h.Error);
            up = true;
            h.Retry();
            await WaitDone(h);
            Check(h.Ready, "Retry() installs it");
        }
        {   // connection refused
            var dir = Tmp("refused"); var host = Path.Combine(dir, "host");
            using var h = new HostInstaller(host, new HostManifest("v1", "http://127.0.0.1:9/host.zip", Sha(good), good.Length), Log, retryDelays: noDelay);
            h.Start();
            await WaitDone(h);
            Check(h.Phase == HostPhase.Failed && h.Error.StartsWith("Could not connect"), "no connection is reported: " + h.Error);
        }
        if (!OperatingSystem.IsWindows())
        {   // truncated body (HttpListener.Abort is not implemented by Wine)
            var dir = Tmp("cut"); var host = Path.Combine(dir, "host");
            using var srv = new Server(async (n, r) =>
            {
                r.ContentLength64 = good.Length;
                await r.OutputStream.WriteAsync(good.AsMemory(0, good.Length / 2));
                r.Abort();
            });
            using var h = new HostInstaller(host, new HostManifest("v1", srv.Url, Sha(good), good.Length), Log, retryDelays: noDelay);
            h.Start();
            await WaitDone(h);
            Check(h.Phase == HostPhase.Failed && !Directory.Exists(host), "a cut-off download fails and installs nothing");
        }
        {   // zip with a path that escapes the folder
            var dir = Tmp("slip"); var host = Path.Combine(dir, "host");
            var evil = MakeZip(new() { ["SpherePlayer.PlayerHost.exe"] = "exe", ["../escaped.txt"] = "x" });
            using var srv = new Server((n, r) => Serve(r, evil));
            using var h = new HostInstaller(host, new HostManifest("v1", srv.Url, Sha(evil), evil.Length), Log, retryDelays: noDelay);
            h.Start();
            await WaitDone(h);
            Check(h.Phase == HostPhase.Failed && !File.Exists(Path.Combine(dir, "escaped.txt")), "a zip path that leaves the folder is refused");
        }
        {   // zip without the exe
            var dir = Tmp("noexe"); var host = Path.Combine(dir, "host");
            var wrong = MakeZip(new() { ["readme.txt"] = "x" });
            using var srv = new Server((n, r) => Serve(r, wrong));
            using var h = new HostInstaller(host, new HostManifest("v1", srv.Url, Sha(wrong), wrong.Length), Log, retryDelays: noDelay);
            h.Start();
            await WaitDone(h);
            Check(h.Phase == HostPhase.Failed && !Directory.Exists(host), "a zip without the player exe is refused");
        }
        {   // the real packed host, if it was built (tools/pack-host.sh)
            var zip = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "dist");
            var real = Directory.Exists(zip) ? Directory.GetFiles(zip, "PlayerHost-*.zip").FirstOrDefault() : null;
            if (real is not null)
            {
                var bytes = await File.ReadAllBytesAsync(real);
                var dir = Tmp("real"); var host = Path.Combine(dir, "host");
                using var srv = new Server((n, r) => Serve(r, bytes));
                using var h = new HostInstaller(host, new HostManifest("real", srv.Url, Sha(bytes), bytes.Length), Log, retryDelays: noDelay);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                h.Start();
                await WaitDone(h);
                Check(h.Ready && File.Exists(Path.Combine(host, "libcef.dll")), $"real host zip ({bytes.Length / 1048576} MB) installs in {sw.Elapsed.TotalSeconds:0.0} s");
                Directory.Delete(dir, true);
            }
            else Console.WriteLine("  skip real host zip (run tools/pack-host.sh first)");
        }
    }

    // A fake player host: a shell script named like the real exe that logs every command it gets and answers with a state line.
    private static (string HostDir, string CmdLog) FakeHost(string dir)
    {
        var host = Path.Combine(dir, "host");
        Directory.CreateDirectory(host);
        var log = Path.Combine(dir, "commands.log");
        var exe = Path.Combine(host, "SpherePlayer.PlayerHost.exe");
        File.WriteAllText(exe, $"#!/bin/sh\necho started\nwhile read cmd; do echo \"$cmd\" >> '{log}'; case \"$cmd\" in play*) echo 'state {{\"ready\":true,\"status\":\"playing\",\"title\":\"t\",\"ends\":0,\"errors\":0}}';; pause*) echo 'state {{\"ready\":true,\"status\":\"paused\",\"title\":\"t\",\"ends\":0,\"errors\":0}}';; quit*) exit 0;; esac; done\n");
        File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(host, ".host-version"), "fake");
        return (host, log);
    }

    private static string[] Cmds(string log) => File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();

    private sealed class Clock { public DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc); public Func<DateTime> F => () => Now; }

    private static async Task GuardTests()
    {
        Console.WriteLine("VideoGuard and Controller (YouTube terms)");
        foreach (var compact in new[] { false, true })
            foreach (var cfg in new[] { 0, 356, 400, 480, 900 })
            {
                var (w, h) = VideoGuard.VideoSize(compact, cfg);
                if (w < 200 || h < 200) { Check(false, $"video size {w}x{h} (compact={compact}, config={cfg}) is below 200x200"); }
            }
        Check(VideoGuard.VideoSize(true, 480).W == VideoGuard.MinWidth, "battle shrinks to the minimum size");
        Check(VideoGuard.VideoSize(true, 480).H >= 200 && VideoGuard.MinWidth >= 200, "the minimum size is at least 200x200");

        var clock = new Clock();
        var guard = new VideoGuard(clock.F);
        Check(!guard.MayPlay, "nothing may play before the window drew");
        guard.Drawn();
        Check(guard.MayPlay && guard.Visible, "drawn window may play");
        clock.Now += TimeSpan.FromSeconds(1.0);
        Check(guard.Visible, "still visible 1 s after the last draw");
        clock.Now += TimeSpan.FromSeconds(1.0);
        Check(!guard.Visible && !guard.MayPlay, "not drawn for 2 s = hidden");
        guard.Grant();
        Check(guard.MayPlay && !guard.Visible, "a command grants a short grace to open the window");
        clock.Now += VideoGuard.Grace + TimeSpan.FromSeconds(0.1);
        Check(!guard.MayPlay, "grace runs out");
        guard.Tick(); clock.Now += TimeSpan.FromSeconds(3); guard.Tick();
        Check(guard.MayPlay, "a long freeze between two frames does not count as hidden");

        // Controller with a fake host
        var dir = Tmp("ctl");
        var (hostDir, cmdLog) = FakeHost(dir);
        var cfgObj = new Config { Volume = 30, FadeSeconds = 0 };
        var g = new VideoGuard(clock.F);
        using var installer = new HostInstaller(hostDir, new HostManifest("fake", "http://127.0.0.1:1/x", "00", 1), Log);
        Check(installer.Ready, "fake host counts as installed");
        using var player = new PlayerProcess(installer, Path.Combine(dir, "cache"), Log);
        var lib = new Library();
        var ctl = new Controller(lib, cfgObj, player, g, Log);
        var zone = new ZoneInfo(132, "New Gridania", "town", new[] { "forest", "city" });

        // 1. window never drawn: no music starts
        for (var i = 0; i < 5; i++) { ctl.Tick(Situation.Town, true, zone); await Task.Delay(150); }
        Check(!Cmds(cmdLog).Any(c => c.StartsWith("play")) && ctl.Now is null, "no window drawn: no music starts");

        // 2. window drawn: music starts
        g.Drawn();
        for (var i = 0; i < 12 && ctl.Now is null; i++) { g.Drawn(); ctl.Tick(Situation.Town, true, zone); await Task.Delay(150); }
        Check(ctl.Now is not null && Cmds(cmdLog).Any(c => c.StartsWith("play")), "window drawn: music starts");
        var playsBefore = Cmds(cmdLog).Count(c => c.StartsWith("play"));

        // 3. window collapsed/closed (no more draws): music pauses
        clock.Now += TimeSpan.FromSeconds(2);
        ctl.Tick(Situation.Town, true, zone);
        await Task.Delay(300);
        Check(ctl.Paused && ctl.HiddenPause && Cmds(cmdLog).Contains("pause"), "window hidden: music is paused");

        // 4. hidden: battle, zone change, Next, manual play must not start playback
        var zone2 = new ZoneInfo(140, "Western Thanalan", "field", new[] { "desert" });
        for (var i = 0; i < 4; i++) { ctl.Tick(Situation.Battle, true, zone2); await Task.Delay(100); }
        ctl.Next(Situation.Town);
        ctl.PlayManual(lib.Songs.First(), Situation.Town);
        ctl.PlayRandom(Category.Battle);
        ctl.TogglePause();
        await Task.Delay(300);
        Check(Cmds(cmdLog).Count(c => c.StartsWith("play")) == playsBefore && ctl.Paused, "hidden: battle, zone change, next, play and resume start nothing");
        Check(ctl.Note.Contains("Open the music window"), "hidden: the user is told to open the window");

        // 5. window comes back: music continues by itself
        g.Drawn();
        for (var i = 0; i < 4; i++) { g.Drawn(); ctl.Tick(Situation.Town, true, zone); await Task.Delay(100); }
        Check(!ctl.Paused && !ctl.HiddenPause && Cmds(cmdLog).Contains("resume"), "window back: the song continues by itself");

        // 6. a command (grant) starts a new song while the window is still opening
        clock.Now += TimeSpan.FromSeconds(2);
        ctl.Tick(Situation.Town, true, zone);
        await Task.Delay(200);
        Check(ctl.HiddenPause, "hidden again");
        var before = Cmds(cmdLog).Count(c => c.StartsWith("play"));
        g.Grant();
        ctl.Next(Situation.Town);
        await Task.Delay(300);
        Check(Cmds(cmdLog).Count(c => c.StartsWith("play")) == before + 1 && !ctl.Paused, "Next after the window was asked to open plays");

        // 7. battle while visible: song of the battle kind, nothing hidden
        g.Drawn();
        for (var i = 0; i < 6; i++) { g.Drawn(); ctl.Tick(Situation.Battle, true, zone); await Task.Delay(150); }
        Check(ctl.Now?.Category == Category.Battle && !ctl.Paused, "visible window in battle: battle music plays");

        // 8. a stall of a few seconds between frames is not hidden
        var ngl = new VideoGuard(clock.F);
        ngl.Tick(); clock.Now += TimeSpan.FromSeconds(5); ngl.Tick();
        Check(ngl.MayPlay, "stall: no pause");

        // 9. main menu: music plays before login, a place song replaces it after login
        var dir2 = Tmp("ctl2");
        var (hostDir2, cmdLog2) = FakeHost(dir2);
        var cfg2 = new Config { Volume = 30, FadeSeconds = 0 };
        Check(cfg2.Mode == PlayMode.Omakase, "omakase is the default mode");
        var g2 = new VideoGuard(clock.F);
        using var installer2 = new HostInstaller(hostDir2, new HostManifest("fake", "http://127.0.0.1:1/x", "00", 1), Log);
        using var player2 = new PlayerProcess(installer2, Path.Combine(dir2, "cache"), Log);
        var ctl2 = new Controller(lib, cfg2, player2, g2, Log);
        for (var i = 0; i < 8 && ctl2.Now is null; i++) { g2.Drawn(); ctl2.Tick(Situation.Idle, false, ZoneInfo.None); await Task.Delay(150); }
        Check(ctl2.Now is not null && Cmds(cmdLog2).Any(c => c.StartsWith("play")), "main menu: music plays before login");
        var menuSong = ctl2.Now;
        var menuPlays = Cmds(cmdLog2).Count(c => c.StartsWith("play"));
        for (var i = 0; i < 6; i++) { g2.Drawn(); ctl2.Tick(Situation.Idle, false, ZoneInfo.None); await Task.Delay(50); }
        Check(Cmds(cmdLog2).Count(c => c.StartsWith("play")) == menuPlays, "main menu: one song, not restarted every frame");
        for (var i = 0; i < 12 && Cmds(cmdLog2).Count(c => c.StartsWith("play")) == menuPlays; i++) { g2.Drawn(); ctl2.Tick(Situation.Town, true, zone); await Task.Delay(150); }
        Check(Cmds(cmdLog2).Count(c => c.StartsWith("play")) == menuPlays + 1 && ctl2.Now?.Id != menuSong?.Id, "login: a song for the place replaces the menu song");

        // 10. Stop stays stopped until Next
        ctl2.StopNow();
        var stopPlays = Cmds(cmdLog2).Count(c => c.StartsWith("play"));
        for (var i = 0; i < 8; i++) { g2.Drawn(); ctl2.Tick(Situation.Town, true, zone); await Task.Delay(150); }
        Check(ctl2.Now is null && Cmds(cmdLog2).Count(c => c.StartsWith("play")) == stopPlays, "Stop: nothing starts by itself");
        ctl2.Next(Situation.Town);
        await Task.Delay(300);
        Check(ctl2.Now is not null && !ctl2.Stopped, "Next after Stop plays again");

        // 11. keep-playing choice: the window may be collapsed or closed, the music goes on
        var dir3 = Tmp("ctl3");
        var (hostDir3, cmdLog3) = FakeHost(dir3);
        var g3 = new VideoGuard(clock.F) { RequireVisible = false };
        Check(new Config().KeepPlayingHidden, "keep playing when hidden is the default");
        Check(g3.MayPlay, "keep playing: allowed without a window");
        using var installer3 = new HostInstaller(hostDir3, new HostManifest("fake", "http://127.0.0.1:1/x", "00", 1), Log);
        using var player3 = new PlayerProcess(installer3, Path.Combine(dir3, "cache"), Log);
        var ctl3 = new Controller(lib, new Config { Volume = 30, FadeSeconds = 0 }, player3, g3, Log);
        for (var i = 0; i < 12 && ctl3.Now is null; i++) { ctl3.Tick(Situation.Town, true, zone); await Task.Delay(150); }
        Check(ctl3.Now is not null && Cmds(cmdLog3).Any(c => c.StartsWith("play")), "keep playing: music starts with no window drawn");
        clock.Now += TimeSpan.FromSeconds(5);
        for (var i = 0; i < 4; i++) { ctl3.Tick(Situation.Town, true, zone); await Task.Delay(100); }
        Check(!ctl3.Paused && !ctl3.HiddenPause && !Cmds(cmdLog3).Contains("pause"), "keep playing: collapsing or closing does not pause");
        g3.RequireVisible = true;
        ctl3.Tick(Situation.Town, true, zone);
        await Task.Delay(300);
        Check(ctl3.Paused && ctl3.HiddenPause, "setting switched off: hidden window pauses");
        g3.RequireVisible = false;
        for (var i = 0; i < 4; i++) { ctl3.Tick(Situation.Town, true, zone); await Task.Delay(100); }
        Check(!ctl3.Paused, "setting switched on again: music continues");
    }

    // Under Wine the fake host (a shell script) cannot run, so "installer" runs only the download checks; "tls" also fetches a https page.
    private static async Task ControlServerTests()
    {
        Console.WriteLine("ControlServer");
        var dir = Tmp("control");
        using var srv = new ControlServer(Path.Combine(dir, "control.port"), Log);
        var port = int.Parse(File.ReadAllText(Path.Combine(dir, "control.port")));

        async Task<bool> Dropped(byte[] payload)
        {
            using var c = new System.Net.Sockets.TcpClient();
            await c.ConnectAsync(IPAddress.Loopback, port);
            var st = c.GetStream();
            await st.WriteAsync(payload);
            var b = new byte[16];
            try { return await st.ReadAsync(b, 0, 16).WaitAsync(TimeSpan.FromSeconds(2)) == 0; } catch (TimeoutException) { return false; } catch { return true; }
        }

        using (var good = new System.Net.Sockets.TcpClient())
        {
            await good.ConnectAsync(IPAddress.Loopback, port);
            await good.GetStream().WriteAsync(System.Text.Encoding.ASCII.GetBytes("next\r\n\nPAUSE\nbogus-never-sent"));
            await Task.Delay(300);
            srv.Commands.TryDequeue(out var c1); srv.Commands.TryDequeue(out var c2);
            Check(c1 == "next" && c2 == "pause", "fixed command words are accepted, case and CRLF tolerated");
        }
        while (srv.Commands.TryDequeue(out _)) { }
        Check(await Dropped(System.Text.Encoding.ASCII.GetBytes("POST / HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\nnext\n")), "an HTTP request closes the connection");
        Check(await Dropped(System.Text.Encoding.ASCII.GetBytes(new string('a', 5000))), "a long line without newline closes the connection");
        Check(await Dropped(new byte[] { 0x16, 0x03, 0x01, 0x02, 0x00 }), "binary junk closes the connection");
        Check(srv.Commands.IsEmpty, "rejected clients queued no command");
    }

    public static async Task<int> Main(string[] args)
    {
        await InstallerTests();
        if (args.Contains("tls"))
        {
            Console.WriteLine("HTTPS");
            try
            {
                using var http = new System.Net.Http.HttpClient();
                var r = await http.GetAsync("https://github.com/", System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                Check(r.IsSuccessStatusCode, "https://github.com/ answers " + (int)r.StatusCode);
            }
            catch (Exception ex) { Check(false, "https request failed: " + ex.Message); }
        }
        if (args.Contains("installer")) { Console.WriteLine($"\n{passed} passed, {failed} failed"); return failed == 0 ? 0 : 1; }
        await GuardTests();
        await ControlServerTests();
        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
