using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace SpherePlayer;

// Reads the video frames the player host writes into shared memory and turns them into an ImGui texture.
public sealed class VideoFeed : IDisposable
{
    private const int Magic = 0x4D465846;
    private readonly ITextureProvider textures;
    private readonly IPluginLog log;
    private MemoryMappedFile? mmf;
    private MemoryMappedViewAccessor? view;
    private byte[] buf = Array.Empty<byte>();
    private long lastCounter;
    private int generation = -1;
    private DateTime nextOpen, nextFrame;
    private readonly Queue<(IDalamudTextureWrap Tex, DateTime Due)> dying = new();

    public IDalamudTextureWrap? Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public VideoFeed(ITextureProvider textures, IPluginLog log) { this.textures = textures; this.log = log; }

    public void Update(int hostGeneration)
    {
        while (dying.Count > 0 && dying.Peek().Due < DateTime.UtcNow) dying.Dequeue().Tex.Dispose();
        if (generation != hostGeneration) { CloseView(); generation = hostGeneration; }
        if (view is null)
        {
            if (DateTime.UtcNow < nextOpen) return;
            nextOpen = DateTime.UtcNow.AddSeconds(1);
            try { mmf = MemoryMappedFile.OpenExisting($"sphereplayer-frame-{Environment.ProcessId}"); view = mmf.CreateViewAccessor(); lastCounter = 0; }
            catch { return; }
        }
        if (DateTime.UtcNow < nextFrame) return;
        nextFrame = DateTime.UtcNow.AddMilliseconds(66);
        try
        {
            if (view.ReadInt32(0) != Magic) return;
            var end = view.ReadInt64(24);
            if (end == lastCounter) return;
            int w = view.ReadInt32(4), h = view.ReadInt32(8);
            if (w <= 0 || h <= 0 || w > 640 || h > 360) return;
            if (buf.Length != w * h * 4) buf = new byte[w * h * 4];
            view.ReadArray(32, buf, 0, buf.Length);
            if (view.ReadInt64(16) != end) return;
            lastCounter = end;
            var tex = textures.CreateFromRaw(RawImageSpecification.Bgra32(w, h), buf);
            if (Texture is not null) dying.Enqueue((Texture, DateTime.UtcNow.AddMilliseconds(300)));
            Texture = tex; Width = w; Height = h;
        }
        catch (Exception ex) { log.Warning(ex, "Video frame failed"); nextFrame = DateTime.UtcNow.AddSeconds(5); }
    }

    private void CloseView() { view?.Dispose(); mmf?.Dispose(); view = null; mmf = null; }

    public void Dispose()
    {
        CloseView();
        Texture?.Dispose();
        while (dying.Count > 0) dying.Dequeue().Tex.Dispose();
    }
}
