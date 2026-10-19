using SourOmen.Structs;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Pictomancy.VfxDraw;
public static unsafe class VfxFunctions
{
    public const string VfxInitDataCtorSig = VfxFunctionHeads.VfxInitDataCtorCall;
    public delegate VfxInitData* VfxInitDataCtorDelegate(VfxInitData* self);
    public static VfxInitDataCtorDelegate VfxInitDataCtor;

    public const string CreateVfxSig = VfxFunctionHeads.CreateVfxCall;
    public delegate VfxData* CreateVfxDelegate(byte* path, VfxInitData* init, byte a3, byte a4, float originX, float originY, float originZ, float sizeX, float sizeY, float sizeZ, float angle, float duration, int a13);
    public static CreateVfxDelegate CreateVfx;

    public const string CreateGameObjectVfxSig = VfxFunctionHeads.CreateGameObjectVfxCall;
    public delegate VfxData* CreateGameObjectVfxDelegate(byte* path, nint target, nint source, float a4, byte a5, ushort a6, byte a7);
    public static CreateGameObjectVfxDelegate CreateGameObjectVfxInternal;

    public const string DestroyVfxSig = VfxLayoutDerivation.DestroyVfxCall;
    public delegate void DestroyVfxDataDelegate(VfxData* self);
    public static DestroyVfxDataDelegate DestroyVfx;

    public const string UpdateVfxTransformSig = VfxLayoutDerivation.UpdateVfxTransformCall;
    public delegate long UpdateVfxTransformDelegate(VfxResourceInstance* vfxInstance, Matrix4x4* transform);
    public static UpdateVfxTransformDelegate UpdateVfxTransform;

    public const string UpdateVfxColorSig = VfxLayoutDerivation.UpdateVfxColorCall;
    public delegate long UpdateVfxColorDelegate(VfxResourceInstance* vfxInstance, float r, float g, float b, float a);
    public static UpdateVfxColorDelegate UpdateVfxColor;

    public const string RotateMatrixSig = VfxFunctionHeads.RotateMatrixCall;
    public delegate void RotateMatrixDelegate(Matrix4x4* matrix, float rotation);
    public static RotateMatrixDelegate RotateMatrix;

    /// <summary>
    /// True only when all seven functions above were resolved, each one's head matches the expected shape,
    /// and the VFX memory layout was derived from game code.
    /// While false any of those delegates may be null, so every call site checks this first.
    /// Consumers read <see cref="PictoService.VfxAvailable"/>.
    /// </summary>
    public static bool Available { get => available; private set => available = value; }
    private static volatile bool available;

    // 只在 Available 為 true 時有效；先寫它再把 Available 設成 true（volatile 寫入保證讀端看得到）。
    private static VfxLayoutDerivation.Layout layout;

    internal const string UnavailableMessage = "[Pictomancy] VFX functions are unavailable in this game version.";

    /// <summary>
    /// Same as <see cref="Available"/>, and when false marks that a consumer asked for VFX drawing,
    /// which lets the one-time chat notice go out after login. Use on paths where a consumer asks for VFX.
    /// </summary>
    internal static bool CheckAvailable()
    {
        if (available) return true;
        PictoService.NoteVfxRequestedWhileUnavailable();
        return false;
    }

    // 不擲例外：特徵碼對不上或版面推不出來只停用 VFX 繪製，不可以讓呼叫端（多半是插件建構子）跟著失敗。
    internal static void Initialize()
    {
        Available = false;
        List<string> missing = [];
        VfxInitDataCtor = Resolve<VfxInitDataCtorDelegate>(VfxInitDataCtorSig, nameof(VfxInitDataCtor), missing);
        CreateVfx = Resolve<CreateVfxDelegate>(CreateVfxSig, nameof(CreateVfx), missing);
        CreateGameObjectVfxInternal = Resolve<CreateGameObjectVfxDelegate>(CreateGameObjectVfxSig, nameof(CreateGameObjectVfxInternal), missing);
        DestroyVfx = Resolve<DestroyVfxDataDelegate>(DestroyVfxSig, nameof(DestroyVfx), missing);
        UpdateVfxTransform = Resolve<UpdateVfxTransformDelegate>(UpdateVfxTransformSig, nameof(UpdateVfxTransform), missing);
        UpdateVfxColor = Resolve<UpdateVfxColorDelegate>(UpdateVfxColorSig, nameof(UpdateVfxColor), missing);
        RotateMatrix = Resolve<RotateMatrixDelegate>(RotateMatrixSig, nameof(RotateMatrix), missing);

        if (missing.Count > 0)
        {
            PictoService.Log.Warning($"[Pictomancy] VFX rendering disabled: signature not found for {string.Join(", ", missing)}.");
            PictoService.RequestVfxUnavailableEcho();
            return;
        }

        // 呼叫點特徵碼可能改版後指到別支函式：開頭對不上就不呼叫（呼叫錯的函式＝AVE，攔不到）。
        if (!VfxFunctionHeads.Check(ReadMatch, out var heads))
        {
            PictoService.Log.Error($"[Pictomancy] VFX rendering disabled: a game function does not look like the one Pictomancy expects ({heads}).");
            PictoService.RequestVfxUnavailableEcho();
            return;
        }

        // 直接寫進遊戲結構的三個偏移不寫死：推不出來就整個停用，寫錯欄位＝寫壞遊戲記憶體（AVE 攔不到）。
        if (!VfxLayoutDerivation.Derive(ReadMatch, out var derived, out var evidence))
        {
            PictoService.Log.Error($"[Pictomancy] VFX rendering disabled: could not derive the VFX memory layout from game code ({evidence}).");
            PictoService.RequestVfxUnavailableEcho();
            return;
        }
        layout = derived;
        Available = true;
        PictoService.Log.Information($"[Pictomancy] VFX layout from game code: instance +0x{derived.InstanceOffset:X}, scale +0x{derived.ScaleOffset:X}, color +0x{derived.ColorOffset:X} ({evidence}; {heads}).");
    }

    private static T Resolve<T>(string signature, string name, List<string> missing) where T : Delegate
    {
        if (PictoService.SigScanner.TryScanText(signature, out var address) && address != nint.Zero)
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        missing.Add(name);
        return null!;
    }

    // 讀 SigScanner 的模組副本（取自磁碟上的執行檔，不受別的 hook 改寫），命中點與長度都要落在 .text 內。
    private static byte[]? ReadMatch(string signature, int length)
    {
        var scanner = PictoService.SigScanner;
        if (!scanner.TryScanText(signature, out var address)) return null;
        var rva = (long)address - (long)scanner.Module.BaseAddress;
        if (rva < scanner.TextSectionOffset || rva + length > scanner.TextSectionOffset + scanner.TextSectionSize) return null;
        var bytes = new byte[length];
        Marshal.Copy(scanner.SearchBase + (nint)rva, bytes, 0, length);
        return bytes;
    }

    /// <summary>
    /// Reads the raw instance pointer stored in a <see cref="VfxData"/>; false (nothing read) when data is null or VFX is unavailable.
    /// Callers turn it into a pointer with <see cref="ToInstance"/> and may hand the raw value to <see cref="VfxLifetimeWatch"/>.
    /// </summary>
    internal static bool TryReadInstance(VfxData* data, out ulong raw)
    {
        raw = 0;
        if (data == null || !available) return false;
        raw = *(ulong*)((byte*)data + layout.InstanceOffset);
        return true;
    }

    /// <summary>A raw instance value outside the user address range is treated as null.</summary>
    internal static VfxResourceInstance* ToInstance(ulong raw)
        => VfxLifetimeWatch.IsUserPointer(raw) ? (VfxResourceInstance*)raw : null;

    internal static void WriteScale(VfxResourceInstance* instance, Vector3 scale)
    {
        if (instance == null || !available) return;
        *(Vector3*)((byte*)instance + layout.ScaleOffset) = scale;
    }

    internal static void WriteColor(VfxResourceInstance* instance, Vector4 color)
    {
        if (instance == null || !available) return;
        *(Vector4*)((byte*)instance + layout.ColorOffset) = color;
    }
}
