using SourOmen.Structs;
using System.Numerics;

namespace Pictomancy.VfxDraw;
public unsafe class Vfx : IDisposable
{
    internal string? path;
    internal Vector3 position;
    internal Vector3 size;
    internal float rotation;
    internal Vector4 color;
    internal VfxData* data;
    internal bool pctOwned = false;
    internal VfxLifetimeWatch.State watch;
    internal VfxLifetimeWatch.RebuildBudget rebuild;

    public static Vfx Create(string path, Vector3 position, Vector3 size, float rotation, Vector4? color = null)
    {
        if (!VfxFunctions.CheckAvailable()) throw new InvalidOperationException(VfxFunctions.UnavailableMessage);
        return TryCreate(path, position, size, rotation, color ?? Vector4.One) ?? throw new ArgumentNullException("null vfx data");
    }

    // 不擲例外：遊戲沒建出來就回 null（VfxRenderer 靠它限制重試次數）。
    internal static Vfx? TryCreate(string path, Vector3 position, Vector3 size, float rotation, Vector4 color)
    {
        var data = CreateVfxInternal(path, position, size, rotation, color, out var instance);
        if (data == null) return null;
        Vfx vfx = new(path, data, position, size, rotation, color);
        vfx.pctOwned = true;
        vfx.Track(instance, known: true);
        return vfx;
    }

    public static Vfx Wrap(VfxData* data, Vector3 position, Vector3 size, float rotation, Vector4? color = null)
    {
        Vfx vfx = new(null, data, position, size, rotation, color ?? Vector4.One);
        vfx.Track(0, known: false);
        return vfx;
    }

    // 已忘記＝把指標丟掉：之後每條路徑都碰不到它（IsValid 回 false、不更新、不 DestroyVfx）。
    private void Track(ulong instance, bool known)
    {
        if (!VfxLifetimeWatch.Begin(ref watch, instance, known, nameof(Vfx))) data = null;
    }

    private Vfx(string? path, VfxData* data, Vector3 position, Vector3 size, float rotation, Vector4 color)
    {
        if (data == null) throw new ArgumentNullException("null vfx data");
        this.path = path;
        this.data = data;
        this.position = position;
        this.size = size;
        this.rotation = rotation;
    }

    public bool IsValid => ReadInstance() != null;

    // 持有中的 VfxData 每次讀實例都經過這裡：已忘記就不讀；讀到不見／被換掉就從這一刻起忘掉。
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

    public void Dispose()
    {
        if (pctOwned && VfxLifetimeWatch.IsForgotten(watch))
            VfxLifetimeWatch.NoteSkipped(ref watch, "DestroyVfx");
        else if (pctOwned && IsValid && VfxFunctions.Available)
            VfxFunctions.DestroyVfx(data);
        VfxLifetimeWatch.End(ref watch);
        data = null;
    }

    private static VfxData* CreateVfxInternal(string path, Vector3 position, Vector3 size, float rotation, Vector4 color, out ulong instance)
    {
        instance = 0;
        if (!VfxFunctions.Available) return null;
        var pathBytes = System.Text.Encoding.UTF8.GetBytes(path);

        // 遊戲的建構子會寫滿整個 VfxInitData：給一塊遠大於它的 16 對齊緩衝區，改版結構變大也不會寫到堆疊別處。
        byte* raw = stackalloc byte[InitDataBufferSize + 16];
        var init = (VfxInitData*)(((nint)raw + 15) & ~(nint)15);
        new Span<byte>(init, InitDataBufferSize).Clear();
        VfxFunctions.VfxInitDataCtor(init);

        fixed (byte* pathPtr = pathBytes)
        {
            var vfx = VfxFunctions.CreateVfx(pathPtr, init, 2, 0, position.X, position.Y, position.Z, size.X, size.Y, size.Z, rotation, 1, -1);
            if (vfx == null) return null;
            VfxFunctions.TryReadInstance(vfx, out instance);
            VfxFunctions.WriteColor(VfxFunctions.ToInstance(instance), color);

            return vfx;
        }
    }

    private const int InitDataBufferSize = 0x800;

    public void UpdatePosition(Vector3 position)
    {
        UpdateTransform(position, size, rotation);
    }

    public void UpdateScale(Vector3 scale)
    {
        UpdateTransform(position, scale, rotation);
    }

    public void UpdateRotation(float rotation)
    {
        UpdateTransform(position, size, rotation);
    }

    public void UpdateTransform(Vector3 position, Vector3 size, float rotation)
    {
        if (!VfxFunctions.Available) return;
        MaybeRefreshVfxResourceInstance();
        if (!IsValid) return;
        if (this.position == position && this.rotation == rotation && this.size == size) return;

        this.position = position;
        this.size = size;
        this.rotation = rotation;

        // UpdateVfxTransform 用 movaps 讀矩陣（要 16 對齊），當場複製、不保留指標：放堆疊上，每次更新不再配置也不會漏。
        byte* raw = stackalloc byte[sizeof(Matrix4x4) + 16];
        Matrix4x4* alignedMatrix = (Matrix4x4*)(((nint)raw + 15) & ~(nint)15);
        alignedMatrix->M11 = size.X;
        alignedMatrix->M12 = 0;
        alignedMatrix->M13 = 0;
        alignedMatrix->M14 = 0;

        alignedMatrix->M21 = 0;
        alignedMatrix->M22 = size.Y;
        alignedMatrix->M23 = 0;
        alignedMatrix->M24 = 0;

        alignedMatrix->M31 = 0;
        alignedMatrix->M32 = 0;
        alignedMatrix->M33 = size.Z;
        alignedMatrix->M34 = 0;

        VfxFunctions.RotateMatrix(alignedMatrix, rotation);

        alignedMatrix->M41 = position.X;
        alignedMatrix->M42 = position.Y;
        alignedMatrix->M43 = position.Z;
        alignedMatrix->M44 = 0;
        var instance = ReadInstance();
        if (instance == null) return;
        VfxFunctions.UpdateVfxTransform(instance, alignedMatrix);
    }

    public void UpdateColor(Vector4 color)
    {
        if (!VfxFunctions.Available) return;
        var instance = ReadInstance();
        if (instance == null) return;
        if (this.color == color) return;

        this.color = color;
        VfxFunctions.UpdateVfxColor(instance, color.X, color.Y, color.Z, color.W);
    }

    // 舊的 VfxData 不見／被換掉／建立失敗才重建；遊戲一直收掉時由重建上限擋住，不會每幀呼叫建立函式。
    private void MaybeRefreshVfxResourceInstance()
    {
        if (!pctOwned || path == null || IsValid) return;
        if (!VfxLifetimeWatch.TryRebuild(ref rebuild, Environment.TickCount64, nameof(Vfx))) return;
        VfxLifetimeWatch.End(ref watch);
        data = CreateVfxInternal(path, position, size, rotation, color, out var instance);
        if (data != null) Track(instance, known: true);
    }
}
