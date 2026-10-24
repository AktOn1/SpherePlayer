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
    private const int MaxLine = 16;
    private const int MaxClients = 4;
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
            lock (clients)
            {
                if (clients.Count >= MaxClients) { try { c.Dispose(); } catch { } continue; }
                clients.Add(c);
            }
            _ = Task.Run(() => ReadLoop(c));
            Send(c, lastStatus);
        }
    }

    // Clients may only send the fixed command words. Anything else (an HTTP request from a web page, an over-long line, binary junk) closes the connection.
    private async Task ReadLoop(TcpClient c)
    {
        try
        {
            var stream = c.GetStream();
            var buf = new byte[256];
            var line = new StringBuilder();
            int n;
            while ((n = await stream.ReadAsync(buf, 0, buf.Length)) > 0)
            {
                for (var i = 0; i < n; i++)
                {
                    var b = buf[i];
                    if (b == (byte)'\n')
                    {
                        var cmd = line.ToString().Trim().ToLowerInvariant();
                        line.Clear();
                        if (cmd.Length == 0) continue;
                        if (!Allowed.Contains(cmd)) { log.Warning("Control port: dropped a client that sent something other than a command"); goto done; }
                        Commands.Enqueue(cmd);
                    }
                    else
                    {
                        if (line.Length >= MaxLine || b < 0x09 || b > 0x7e) { log.Warning("Control port: dropped a client that sent an invalid line"); goto done; }
                        line.Append((char)b);
                    }
                }
            }
        }
        catch { }
        done:
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
