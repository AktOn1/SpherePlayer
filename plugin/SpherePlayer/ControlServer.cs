using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace SpherePlayer;

// Loopback control port: a helper outside the game (the Linux media-key bridge) sends one command per line and receives the playback status as JSON lines.
public sealed class ControlServer : IDisposable
{
    private static readonly HashSet<string> Allowed = new() { "next", "prev", "playpause", "play", "pause", "stop" };
    private readonly string portFile;
    private readonly IPluginLog log;
    private readonly TcpListener listener;
    private readonly List<TcpClient> clients = new();
    private string lastStatus = "";
    private volatile bool disposed;

    public ConcurrentQueue<string> Commands { get; } = new();

    public ControlServer(string portFile, IPluginLog log)
    {
        this.portFile = portFile;
        this.log = log;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try { File.WriteAllText(portFile, port.ToString()); } catch (Exception ex) { log.Warning(ex, "Could not write control port file"); }
        log.Information("Control port {Port}", port);
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!disposed)
        {
            TcpClient c;
            try { c = await listener.AcceptTcpClientAsync(); } catch { return; }
            lock (clients) clients.Add(c);
            _ = Task.Run(() => ReadLoop(c));
            Send(c, lastStatus);
        }
    }

    private async Task ReadLoop(TcpClient c)
    {
        try
        {
            using var reader = new StreamReader(c.GetStream(), Encoding.UTF8);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var cmd = line.Trim().ToLowerInvariant();
                if (Allowed.Contains(cmd)) Commands.Enqueue(cmd);
            }
        }
        catch { }
        lock (clients) clients.Remove(c);
        try { c.Dispose(); } catch { }
    }

    private void Send(TcpClient c, string line)
    {
        if (line == "") return;
        try { var b = Encoding.UTF8.GetBytes(line + "\n"); lock (c) c.GetStream().Write(b, 0, b.Length); }
        catch { try { c.Dispose(); } catch { } }
    }

    public bool HasClients { get { lock (clients) return clients.Count > 0; } }

    public void Publish(string status)
    {
        lastStatus = status;
        List<TcpClient> copy;
        lock (clients) copy = new List<TcpClient>(clients);
        foreach (var c in copy) Send(c, status);
    }

    public void Dispose()
    {
        disposed = true;
        try { listener.Stop(); } catch { }
        lock (clients) { foreach (var c in clients) { try { c.Dispose(); } catch { } } clients.Clear(); }
        try { File.Delete(portFile); } catch { }
    }
}
