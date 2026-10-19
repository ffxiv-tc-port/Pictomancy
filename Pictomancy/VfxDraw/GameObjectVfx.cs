using Dalamud.Game.ClientState.Objects.Types;
using SourOmen.Structs;
using System.Numerics;

namespace Pictomancy.VfxDraw;
public unsafe class GameObjectVfx : IDisposable
{
    internal IGameObject target;
    internal IGameObject source;
    internal Vector3 scale;
    internal Vector4 color;
    internal VfxData* data;
    internal VfxLifetimeWatch.State watch;

    public static GameObjectVfx Create(string path, IGameObject target, IGameObject source, Vector3 scale, Vector4 color)
    {
        if (!VfxFunctions.CheckAvailable()) throw new InvalidOperationException(VfxFunctions.UnavailableMessage);
        return TryCreate(path, target, source, scale, color) ?? throw new InvalidOperationException("[Pictomancy] The game did not create the VFX.");
    }

    // 不擲例外：遊戲沒建出來就回 null（VfxRenderer 靠它限制重試次數）。
    internal static GameObjectVfx? TryCreate(string path, IGameObject target, IGameObject source, Vector3 scale, Vector4 color)
    {
        var data = CreateGameObjectVfxInternal(path, target, source, scale, color, out var instance);
        return data == null ? null : new(target, source, scale, color, data, instance);
    }

    private GameObjectVfx(IGameObject target, IGameObject source, Vector3 scale, Vector4 color, VfxData* data, ulong instance)
    {
        this.target = target;
        this.source = source;
        this.scale = scale;
        this.color = color;
        this.data = data;
        if (!VfxLifetimeWatch.Begin(ref watch, instance, known: true, nameof(GameObjectVfx))) this.data = null;
    }

    // 刻意不重建：遊戲收掉的就讓它消失。交還前讀一次實例（就是 DestroyVfx 第一條指令讀的同一個位址），
    // 讀到原值才交給 DestroyVfx；不見／被換掉就照忘掉處理。
    public void Dispose()
    {
        if (VfxLifetimeWatch.IsForgotten(watch))
            VfxLifetimeWatch.NoteSkipped(ref watch, "DestroyVfx");
        else if (VfxFunctions.Available && ReadInstance() != null)
            VfxFunctions.DestroyVfx(data);
        VfxLifetimeWatch.End(ref watch);
        data = null;
    }

    // 已忘記就不讀；讀到不見／被換掉就從這一刻起忘掉（丟掉指標）。
    private VfxResourceInstance* ReadInstance()
    {
        if (VfxLifetimeWatch.IsForgotten(watch))
        {
            VfxLifetimeWatch.NoteSkipped(ref watch, "read");
            return null;
        }
        if (!VfxFunctions.TryReadInstance(data, out var raw)) return null;
        if (VfxLifetimeWatch.Observe(ref watch, raw)) return VfxFunctions.ToInstance(raw);
        data = null;
        return null;
    }

    private static VfxData* CreateGameObjectVfxInternal(string path, IGameObject target, IGameObject source, Vector3 scale, Vector4 color, out ulong instance)
    {
        instance = 0;
        if (!VfxFunctions.Available) return null;
        var pathBytes = System.Text.Encoding.UTF8.GetBytes(path);
        fixed (byte* pathPtr = pathBytes)
        {
            var vfx = VfxFunctions.CreateGameObjectVfxInternal(pathPtr, target.Address, source.Address, 1, 0, 0, 1);
            if (vfx == null) return null;
            VfxFunctions.TryReadInstance(vfx, out instance);
            var created = VfxFunctions.ToInstance(instance);
            VfxFunctions.WriteScale(created, scale);
            VfxFunctions.WriteColor(created, color);

            return vfx;
        }
    }
    public void UpdateColor(Vector4 color)
    {
        if (!VfxFunctions.Available) return;
        if (this.color == color)
            return;
        var instance = ReadInstance();
        if (instance == null) return;

        this.color = color;
        VfxFunctions.UpdateVfxColor(instance, color.X, color.Y, color.Z, color.W);
    }
}
