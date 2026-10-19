using System.Net;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using CefSharp;
using CefSharp.OffScreen;
using System.IO.MemoryMappedFiles;

// Minimal audio-only browser host. Commands arrive on stdin (one per line), status goes to stdout.
// Usage: SpherePlayer.PlayerHost.exe <cacheDir> [parentPid]
internal static class Program
{
    private static string pageHtml = "";

    // Video frames go to a named shared-memory block the plugin reads: header (32 bytes) + BGRA pixels.
    private const int FrameW = 480, FrameH = 270, FrameMax = 640 * 360 * 4;
    private static MemoryMappedViewAccessor? frameView;
    private static volatile bool videoOn;
    private static long frameCounter;
    private static byte[] frameBuf = new byte[0];

    private static void Trace(string t) { try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "host-trace.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + t + "\n"); } catch { } }

    private static async Task<int> Main(string[] args)
    {
        Trace("main");
        Console.OutputEncoding = new UTF8Encoding(false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject?.ToString() ?? "unknown");
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
        var cacheDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "sphereplayer-cef");
        var parentPid = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 0;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("player.html")!)
        using (var r = new StreamReader(s))
            pageHtml = r.ReadToEnd();

        Trace("listener");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(() => Serve(listener));

        var settings = new CefSettings
        {
            RootCachePath = cacheDir,
            LogSeverity = LogSeverity.Warning,
        };
        settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
        settings.EnableAudio();
        // SetOffScreenRenderingBestPerformanceArgs also adds enable-begin-frame-scheduling, which stops Paint events without external begin frames.
        settings.CefCommandLineArgs["disable-gpu"] = "1";
        settings.CefCommandLineArgs["disable-gpu-compositing"] = "1";
        Trace("cef-init");
        if (!Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null))
        {
            Console.WriteLine("error cef-init-failed");
            return 2;
        }

        Trace("cef-ready");
        using var browser = new ChromiumWebBrowser($"http://127.0.0.1:{port}/player.html?w={FrameW}&h={FrameH}", automaticallyCreateBrowser: false);
        // Must be windowless: a plain WindowInfo makes CEF open a real top-level Chromium window.
        var windowInfo = new WindowInfo { Width = FrameW, Height = FrameH };
        windowInfo.SetAsWindowless(IntPtr.Zero);
        try
        {
            var mmf = MemoryMappedFile.CreateOrOpen($"sphereplayer-frame-{(parentPid > 0 ? parentPid : Environment.ProcessId)}", 32 + FrameMax);
            frameView = mmf.CreateViewAccessor();
        }
        catch (Exception ex) { ReportCrash("frame memory failed: " + ex); }
        browser.Size = new System.Drawing.Size(FrameW, FrameH);
        browser.Paint += (_, e) => OnPaint(e);
        browser.LifeSpanHandler = new PopupHandler();
        browser.CreateBrowser(windowInfo, new BrowserSettings { WindowlessFrameRate = 15 });
        Trace("browser-created");
        // Commands run JS in the page; before the page script has run they are silently lost, so wait until it is there.
        var pageUp = false;
        for (var i = 0; i < 150 && !pageUp; i++)
        {
            await Task.Delay(200);
            if (!browser.IsBrowserInitialized) continue;
            try { var r = await browser.EvaluateScriptAsync("typeof load==='function'&&typeof setVol==='function'"); pageUp = r.Success && r.Result is true; } catch { }
        }
        Trace("page-up=" + pageUp + " paints=" + paints + " size=" + browser.Size);
        try { browser.GetBrowserHost().Invalidate(PaintElementType.View); browser.GetBrowserHost().WasResized(); } catch { }
        Console.WriteLine($"started port={port}");

        if (parentPid > 0)
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(2000);
                    try { System.Diagnostics.Process.GetProcessById(parentPid); }
                    catch { Environment.Exit(0); }
                }
            });
        }

        _ = Task.Run(async () =>
        {
            var last = "";
            while (true)
            {
                await Task.Delay(500);
                if (!browser.IsBrowserInitialized) continue;
                try
                {
                    var res = await browser.EvaluateScriptAsync("JSON.stringify(window.state||{})");
                    var line = res.Success ? res.Result as string ?? "" : "";
                    if (line != "" && line != last) { last = line; Console.WriteLine("state " + line); }
                }
                catch { }
            }
        });

        string? cmd;
        while ((cmd = await Console.In.ReadLineAsync()) != null)
        {
            var sp = cmd.IndexOf(' ');
            var verb = sp < 0 ? cmd : cmd[..sp];
            var arg = sp < 0 ? "" : cmd[(sp + 1)..].Trim();
            Trace("cmd " + cmd);
            try
            {
            switch (verb)
            {
                case "play":
                {
                    // play <id> [startSeconds] [fadeOutMs] [fadeInMs]
                    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0 || !Regex.IsMatch(parts[0], "^[A-Za-z0-9_-]{11}$")) break;
                    var start = parts.Length > 1 && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var st) ? Math.Max(0, st) : 0;
                    var fo = parts.Length > 2 && int.TryParse(parts[2], out var fo1) ? Math.Clamp(fo1, 0, 10000) : 0;
                    var fi = parts.Length > 3 && int.TryParse(parts[3], out var fi1) ? Math.Clamp(fi1, 0, 10000) : 0;
                    browser.ExecuteScriptAsync($"load('{parts[0]}',{start.ToString(System.Globalization.CultureInfo.InvariantCulture)},{fo},{fi})");
                    break;
                }
                case "video": videoOn = arg == "on"; break;
                case "volume" when int.TryParse(arg, out var v):
                    browser.ExecuteScriptAsync($"window.setVol({Math.Clamp(v, 0, 100)})");
                    break;
                case "pause": browser.ExecuteScriptAsync("window.yt&&yt.pauseVideo()"); break;
                case "resume": browser.ExecuteScriptAsync("window.yt&&yt.playVideo()"); break;
                case "stop": browser.ExecuteScriptAsync("window.yt&&yt.stopVideo()"); break;
                case "mouse": Mouse(browser, arg); break;
                case "quit": goto done;
            }
            }
            catch (Exception ex) { ReportCrash("command '" + verb + "' failed: " + ex); }
        }
        done:
        // Cef.Shutdown crashes natively under Wine (access violation) and Wine then shows a crash dialog; a plain exit is clean.
        Environment.Exit(0);
        return 0;
    }

    private static int paints;

    // mouse <move|down|up|wheel|leave> x y wheelDelta, coordinates in browser pixels.
    private static void Mouse(IWebBrowser browser, string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length < 3 || !int.TryParse(a[1], out var x) || !int.TryParse(a[2], out var y)) return;
        var host = browser.GetBrowserHost();
        if (host is null) return;
        var ev = new MouseEvent(x, y, mouseDown ? CefEventFlags.LeftMouseButton : CefEventFlags.None);
        switch (a[0])
        {
            case "move": host.SendMouseMoveEvent(ev, false); break;
            case "leave": host.SendMouseMoveEvent(ev, true); break;
            case "down":
                host.SetFocus(true);
                host.SendMouseMoveEvent(ev, false);
                mouseDown = true;
                host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.LeftMouseButton), MouseButtonType.Left, false, 1);
                break;
            case "up":
                host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.LeftMouseButton), MouseButtonType.Left, true, 1);
                mouseDown = false;
                break;
            case "wheel": host.SendMouseWheelEvent(ev, 0, a.Length > 3 && int.TryParse(a[3], out var d) ? d : 0); break;
        }
    }

    private static bool mouseDown;

    // Links in the player (YouTube logo, title) must not open a Chromium window over the game: cancel them and hand YouTube links to the system browser.
    private sealed class PopupHandler : CefSharp.Handler.LifeSpanHandler
    {
        protected override bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName,
            WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings,
            ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
        {
            newBrowser = null;
            Trace("popup blocked " + targetUrl);
            try
            {
                if (userGesture && Uri.TryCreate(targetUrl, UriKind.Absolute, out var u) && u.Scheme == "https"
                    && (u.Host == "www.youtube.com" || u.Host == "youtube.com" || u.Host == "youtu.be" || u.Host.EndsWith(".youtube.com")))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) { Trace("open link failed " + ex.Message); }
            return true;
        }
    }

    private static void OnPaint(OnPaintEventArgs e)
    {
        if (++paints == 1 || paints % 300 == 0) Trace($"paint #{paints} {e.Width}x{e.Height} videoOn={videoOn}");
        if (!videoOn || frameView is null || e.IsPopup || e.Width <= 0 || e.Height <= 0 || e.Width * e.Height * 4 > FrameMax) return;
        try
        {
            var n = e.Width * e.Height * 4;
            if (frameBuf.Length != n) frameBuf = new byte[n];
            System.Runtime.InteropServices.Marshal.Copy(e.BufferHandle, frameBuf, 0, n);
            var c = ++frameCounter;
            frameView.Write(16, c);
            frameView.Write(0, 0x4D465846);
            frameView.Write(4, e.Width);
            frameView.Write(8, e.Height);
            frameView.WriteArray(32, frameBuf, 0, n);
            frameView.Write(24, c);
        }
        catch { }
    }

    private static void ReportCrash(string text)
    {
        try { Console.WriteLine("error " + text.Replace('\r', ' ').Replace('\n', '|')); } catch { }
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "host-crash.log"), DateTime.Now.ToString("s") + " " + text + "\n"); } catch { }
    }

    private static async Task Serve(TcpListener l)
    {
        while (true)
        {
            var c = await l.AcceptTcpClientAsync();
            _ = Task.Run(async () =>
            {
                using var client = c;
                var ns = c.GetStream();
                var buf = new byte[4096];
                await ns.ReadExactlyAsync(buf, 0, 1);
                var body = Encoding.UTF8.GetBytes(pageHtml);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await ns.WriteAsync(head);
                await ns.WriteAsync(body);
            });
        }
    }
}
