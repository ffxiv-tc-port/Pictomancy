using Dalamud.Game.ClientState.Objects.Types;
using Pictomancy.VfxDraw;
using System.Numerics;

namespace Pictomancy;
public class VfxRenderer : IDisposable
{
    static readonly Vector4 White = Vector4.One;

    private static string OmenPath(string x)
    {
        return $"vfx/omen/eff/{x}.avfx";
    }

    private static string LockonPath(string x)
    {
        return $"vfx/lockon/eff/{x}.avfx";
    }

    private static string ChannelingPath(string x)
    {
        return $"vfx/channeling/eff/{x}.avfx";
    }

    private static string CommonPath(string x)
    {
        return $"vfx/common/eff/{x}.avfx";
    }

    InterframeResourceTracker<Vfx> trackedVfx;
    InterframeResourceTracker<GameObjectVfx> trackedGOVfx;
    // 遊戲沒建出來的鍵 → 重試額度；只留時間窗內的（過了就丟，與留著等價）。只在框架執行緒存取。
    readonly Dictionary<string, VfxLifetimeWatch.RebuildBudget> failedCreates = new();

    public VfxRenderer()
    {
        trackedVfx = new();
        trackedGOVfx = new();
    }

    /// <summary>
    /// Add an omen VFX at a position with scale, rotation, and color.
    /// If the ID is specified in consecutive frames, the VFX is updated to match the new parameters.
    /// If the ID is not specified in consecutive frames, the VFX is destroyed.
    /// </summary>
    /// <param name="id">Unique ID used to track the VFX across frames</param>
    /// <param name="name">Name of the omen VFX (not the full path) "vfx/omen/eff/{name}.avfx"</param>
    /// <param name="origin">World position of the origin of the VFX</param>
    /// <param name="scale">Scale</param>
    /// <param name="rotation">Rotation</param>
    /// <param name="color">Color of the VFX; values beyond 1 are supported</param>
    public void AddOmen(string id, string name, Vector3 origin, Vector3? scale = null, float rotation = 0, Vector4? color = null)
    {
        CreateOrUpdateVfx(id, OmenPath(name), origin, scale ?? Vector3.One, rotation, color ?? White);
    }

    /// <summary>
    /// Add a lockon VFX to a target GameObject with scale and color.
    /// If the ID is specified in consecutive frames, the VFX is updated to match the new parameters.
    /// If the ID is not specified in consecutive frames, the VFX is destroyed.
    /// </summary>
    /// <param name="id">Unique ID used to track the VFX across frames</param>
    /// <param name="name">Name of the lockon VFX (not the full path) "vfx/lockon/eff/{name}.avfx"</param>
    /// <param name="target">Target GameObject to be the parent of the VFX</param>
    /// <param name="scale">Scale; not recommended</param>
    /// <param name="color">Color of the VFX; not recommended; values beyond 1 are supported</param>
    public void AddLockon(string id, string name, IGameObject target, Vector3? scale = null, Vector4? color = null)
    {
        CreateOrUpdateVfx(id, LockonPath(name), target, target, scale ?? Vector3.One, color ?? White);
    }

    /// <summary>
    /// Add a channeling VFX between a source and a target GameObjects with scale and color.
    /// If the ID is specified in consecutive frames, the VFX is updated to match the new parameters.
    /// If the ID is not specified in consecutive frames, the VFX is destroyed.
    /// </summary>
    /// <param name="id">Unique ID used to track the VFX across frames</param>
    /// <param name="name">Name of the channeling VFX (not the full path) "vfx/channeling/eff/{name}.avfx"</param>
    /// <param name="target">Target GameObject to be the end point of the VFX</param>
    /// <param name="source">Source GameObject to be the start point of the VFX</param>
    /// <param name="scale">Scale; not recommended</param>
    /// <param name="color">Color of the VFX; not recommended; values beyond 1 are supported</param>
    public void AddChanneling(string id, string name, IGameObject target, IGameObject source, Vector3? scale = null, Vector4? color = null)
    {
        CreateOrUpdateVfx(id, ChannelingPath(name), target, source, scale ?? Vector3.One, color ?? White);
    }

    /// <summary>
    /// Add a common VFX to a target GameObject with scale and color.
    /// If the ID is specified in consecutive frames, the VFX is updated to match the new parameters.
    /// If the ID is not specified in consecutive frames, the VFX is destroyed.
    /// </summary>
    /// <param name="id">Unique ID used to track the VFX across frames</param>
    /// <param name="name">Name of the common VFX (not the full path) "vfx/common/eff/{name}.avfx"</param>
    /// <param name="target">Target GameObject to be the parent of the VFX</param>
    /// <param name="scale">Scale; not recommended</param>
    /// <param name="color">Color of the VFX; not recommended; values beyond 1 are supported</param>
    public void AddCommon(string id, string name, IGameObject target, Vector3? scale = null, Vector4? color = null)
    {
        CreateOrUpdateVfx(id, CommonPath(name), target, target, scale ?? Vector3.One, color ?? White);
    }

    private void CreateOrUpdateVfx(string id, string path, Vector3 position, Vector3 scale, float rotation, Vector4 color)
    {
        if (!VfxFunctions.CheckAvailable()) return;
        var key = $"{id}##{path}";
        if (trackedVfx.IsTouched(key)) return;

        if (trackedVfx.TryTouchExisting(key, out Vfx vfx))
        {
            vfx.UpdateTransform(position, scale, rotation);
            vfx.UpdateColor(color);
        }
        else if (MayCreate(key, nameof(Vfx)))
        {
            var created = Vfx.TryCreate(path, position, scale, rotation, color);
            NoteCreateResult(key, created != null, nameof(Vfx), path);
            if (created != null) trackedVfx.TouchNew(key, created);
        }
    }

    private void CreateOrUpdateVfx(string id, string path, IGameObject target, IGameObject source, Vector3 scale, Vector4 color)
    {
        if (!VfxFunctions.CheckAvailable()) return;
        var key = $"{id}##{path}";
        if (trackedGOVfx.IsTouched(key)) return;

        if (trackedGOVfx.TryTouchExisting(key, out GameObjectVfx vfx))
        {
            vfx.UpdateColor(color);
        }
        else if (MayCreate(key, nameof(GameObjectVfx)))
        {
            var created = GameObjectVfx.TryCreate(path, target, source, scale, color);
            NoteCreateResult(key, created != null, nameof(GameObjectVfx), path);
            if (created != null) trackedGOVfx.TouchNew(key, created);
        }
    }

    // 遊戲沒建出來時不擲例外：照重建上限重試，被擋的幀不呼叫建立函式、不配置。
    private bool MayCreate(string key, string owner)
    {
        if (!failedCreates.TryGetValue(key, out var budget)) return true;
        var allowed = VfxLifetimeWatch.TryRetryCreate(ref budget, Environment.TickCount64, owner);
        failedCreates[key] = budget;
        return allowed;
    }

    private void NoteCreateResult(string key, bool created, string owner, string path)
    {
        if (created)
        {
            failedCreates.Remove(key);
            return;
        }
        var first = VfxLifetimeWatch.NoteCreateFailed(Environment.TickCount64, owner, path);
        failedCreates.TryAdd(key, first);
    }

    internal void Update()
    {
        trackedVfx.Update();
        trackedGOVfx.Update();
        if (failedCreates.Count == 0) return;
        var now = Environment.TickCount64;
        foreach (var (key, budget) in failedCreates)
            if (VfxLifetimeWatch.WindowExpired(budget, now)) failedCreates.Remove(key);
    }

    public void Dispose()
    {
        trackedVfx.Dispose();
        trackedGOVfx.Dispose();
    }
}
