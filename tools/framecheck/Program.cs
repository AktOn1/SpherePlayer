using System.Diagnostics;
using System.IO.MemoryMappedFiles;

// Test tool: starts the player host like the plugin does, plays a video, and dumps the shared-memory frame as raw BGRA.
// Usage: FrameCheck.exe <hostExe> <cacheDir> <videoId> <outPrefix> <seconds> [extra commands separated by ;  with @N meaning wait N seconds]
var host = args[0]; var cache = args[1]; var vid = args[2]; var outp = args[3]; var secs = int.Parse(args[4]);
var psi = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(host)! };
psi.ArgumentList.Add(cache); psi.ArgumentList.Add(Environment.ProcessId.ToString());
var p = Process.Start(psi)!;
var started = new ManualResetEventSlim();
p.OutputDataReceived += (_, e) => { if (e.Data is null) return; if (e.Data.StartsWith("started")) started.Set(); Console.WriteLine("[host] " + (e.Data.Length > 200 ? e.Data[..200] : e.Data)); };
p.BeginOutputReadLine();
if (!started.Wait(60000)) { Console.WriteLine("host did not start"); p.Kill(); return 2; }
void Send(string c) { Console.WriteLine("> " + c); p.StandardInput.WriteLine(c); p.StandardInput.Flush(); }
Send("volume 30"); Send("video on"); Send($"play {vid} 0 0 800");
void Dump(string tag)
{
    var name = $"sphereplayer-frame-{Environment.ProcessId}";
    try
    {
        using var mmf = MemoryMappedFile.OpenExisting(name);
        using var v = mmf.CreateViewAccessor();
        var magic = v.ReadInt32(0); var w = v.ReadInt32(4); var h = v.ReadInt32(8); var a = v.ReadInt64(16); var b = v.ReadInt64(24);
        Console.WriteLine($"[{tag}] magic={magic:X} {w}x{h} counters {a}/{b}");
        if (w > 0 && h > 0) { var buf = new byte[w * h * 4]; v.ReadArray(32, buf, 0, buf.Length); File.WriteAllBytes($"{outp}-{tag}.bgra", buf); File.WriteAllText($"{outp}-{tag}.txt", $"{w} {h}"); }
    }
    catch (Exception ex) { Console.WriteLine($"[{tag}] mmf read failed: {ex.Message}"); }
}
Thread.Sleep(secs * 1000 / 2); Dump("a");
for (var i = 5; i < args.Length; i++)
{
    foreach (var c in args[i].Split(';')) { if (c.StartsWith("@")) Thread.Sleep(int.Parse(c[1..]) * 1000); else Send(c); }
}
Thread.Sleep(secs * 1000 / 2); Dump("b");
Send("quit"); p.WaitForExit(5000);
return 0;
