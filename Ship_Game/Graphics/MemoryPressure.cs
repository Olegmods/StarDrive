using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.DXGI;
using D3D11Device = SharpDX.Direct3D11.Device;
using DxgiDevice = SharpDX.DXGI.Device;

namespace Ship_Game.Graphics;

/// <summary>
/// Best-effort live memory-pressure probes used to decide whether an expensive
/// content unload/reload is worth doing (e.g. when leaving a game to the main menu).
/// Every probe is conservative: if it cannot positively confirm there is headroom it
/// reports "no headroom" / "under pressure", so the caller takes the safe path.
/// </summary>
public static class MemoryPressure
{
    /// <summary>
    /// Fraction of the system-RAM / VRAM budget below which the resource is considered
    /// to have comfortable headroom. We keep the in-game content cache resident (and let
    /// the next screen allocate on top of it) only when BOTH are below this.
    /// </summary>
    public const float HealthyHeadroomRatio = 0.75f;

    /// <summary>
    /// TRUE only when we are confident BOTH system RAM and VRAM have enough headroom that
    /// keeping the in-game content cache resident will not risk an out-of-memory. Returns
    /// FALSE (= please free memory first) whenever either resource is tight OR a probe
    /// could not be evaluated on this machine/runtime.
    /// </summary>
    public static bool HasHeadroomToKeepContent(GraphicsDevice device)
    {
        bool ramOk  = TryGetSystemRamLoad(out float ramLoad)     && ramLoad  < HealthyHeadroomRatio;
        bool vramOk = TryGetVramLoad(device, out float vramLoad) && vramLoad < HealthyHeadroomRatio;
        bool keep = ramOk && vramOk;
        Log.Info($"ExitToMain memory check: RAM={ramLoad:P0} (ok={ramOk}) VRAM={vramLoad:P0} (ok={vramOk}) "
                 + $"=> {(keep ? "keep cache (fast exit)" : "free cache (safe exit)")}");
        return keep;
    }

    /// <summary>
    /// System-wide physical memory load as a fraction of the GC's high-memory-load
    /// threshold - the point past which the GC stops growing the heap and large
    /// allocations start throwing OOM even when the managed heap itself is healthy.
    /// </summary>
    public static bool TryGetSystemRamLoad(out float load)
    {
        load = 1f; // assume worst case until proven otherwise
        try
        {
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            long threshold = info.HighMemoryLoadThresholdBytes;
            if (threshold <= 0)
                return false;
            load = (float)((double)info.MemoryLoadBytes / threshold);
            return true;
        }
        catch
        {
            return false; // GCMemoryInfo unsupported on this runtime
        }
    }

    /// <summary>
    /// This process's local VRAM usage as a fraction of its DXGI budget. Reflectively
    /// pulls the MonoGame WindowsDX backend's SharpDX D3D11 device (the same field the
    /// device-removed diagnostic uses) and queries IDXGIAdapter3::QueryVideoMemoryInfo.
    /// </summary>
    public static bool TryGetVramLoad(GraphicsDevice device, out float load)
    {
        load = 1f; // assume worst case until proven otherwise
        if (device == null)
            return false;
        try
        {
            FieldInfo fld = typeof(GraphicsDevice).GetField("_d3dDevice",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (fld?.GetValue(device) is not D3D11Device d3d)
                return false;

            // Each QueryInterface / Adapter hands back a separately ref-counted COM wrapper;
            // disposing them releases only that extra ref, never the live D3D11 device.
            using DxgiDevice dxgiDevice = d3d.QueryInterface<DxgiDevice>();
            using Adapter adapter = dxgiDevice.Adapter;
            using Adapter3 adapter3 = adapter.QueryInterface<Adapter3>();
            QueryVideoMemoryInformation mem = adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
            if (mem.Budget <= 0)
                return false;
            load = (float)((double)mem.CurrentUsage / mem.Budget);
            return true;
        }
        catch
        {
            return false; // pre-DXGI-1.4 GPU/driver, or the MonoGame field shape changed
        }
    }
}
