global using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Pictomancy.DXDraw;
using Pictomancy.VfxDraw;

namespace Pictomancy;
#nullable disable

public class PictoService
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; }
    [PluginService] public static IGameGui GameGui { get; private set; }
    [PluginService] public static ICondition Condition { get; private set; }
    [PluginService] public static IPluginLog Log { get; private set; }
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    private static DXRenderer? _dxRenderer;
    private static AddonClipper? _addonClipper;
    private static VfxRenderer? _vfxRenderer;
    public static VfxRenderer VfxRenderer => _vfxRenderer;

    /// <summary>
    /// False until <see cref="InitializeVfxRenderer"/> has resolved every game VFX function and derived the VFX memory layout from game code.
    /// While false, <see cref="VfxRenderer"/> draws nothing (bool-returning Add* methods return false);
    /// once this has been queried (or VFX drawing requested), a one-time chat notice naming the consuming plugin is printed after login.
    /// </summary>
    public static bool VfxAvailable => VfxFunctions.CheckAvailable();

    // 0＝不用提示、1＝待提示、2＝已提示：整個插件生命期最多印一次，重新初始化也不再印。
    private static int vfxEchoState;
    // 插件真的要畫 VFX 才提示；從沒用到 VFX 的插件不該多一行聊天訊息。
    private static volatile bool vfxRequestedWhileUnavailable;

    internal static void RequestVfxUnavailableEcho() => Interlocked.CompareExchange(ref vfxEchoState, 1, 0);

    internal static void NoteVfxRequestedWhileUnavailable() => vfxRequestedWhileUnavailable = true;

    // 只從 Update（框架執行緒）呼叫、登入後才印；只寫 log 容易沒注意到，所以進聊天視窗。
    private static void EchoVfxUnavailableOnce()
    {
        if (Volatile.Read(ref vfxEchoState) != 1 || !vfxRequestedWhileUnavailable) return;
        if (ClientState is not { IsLoggedIn: true } || Chat == null) return;
        if (Interlocked.CompareExchange(ref vfxEchoState, 2, 1) != 1) return;
        Chat.PrintError($"[{PluginInterface.Manifest.Name}] 遊戲改版後，用遊戲特效（VFX）畫地面範圍的功能對不上這個版本，先停用（有備援畫法的會自動改用備援），等插件更新。");
    }

    internal static PctDrawList DrawList;
    internal static PctDrawHints Hints;

    public static void Initialize(IDalamudPluginInterface pluginInterface)
    {
        InitializeDxRenderer(pluginInterface);
        InitializeVfxRenderer(pluginInterface);
    }

    public static void InitializeDxRenderer(IDalamudPluginInterface pluginInterface)
    {
        InitializePluginServices(pluginInterface);
        try
        {
            _dxRenderer = new();
            _addonClipper = new();
        }
        catch (InvalidOperationException ex)
        {
            // 攔的是 RenderContext 在原生繪製裝置取不到時擲的受控例外：
            // 插件照樣載入，只是 Draw() 一律回 null（fail-closed，不畫）。
            _dxRenderer = null;
            _addonClipper = null;
            Log.Error(ex, "[Pictomancy] DX renderer unavailable; overlay drawing disabled.");
        }
    }

    public static void InitializeVfxRenderer(IDalamudPluginInterface pluginInterface)
    {
        InitializePluginServices(pluginInterface);
        _vfxRenderer = new();
        VfxFunctions.Initialize();
        Framework.Update += Update;
        VfxLifetimeWatch.Sink = static message => Log?.Information(message);
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    private static void OnTerritoryChanged(ushort territory) => VfxLifetimeWatch.TerritoryChanged(territory);

    private static bool ServicesInitialized = false;
    private static void InitializePluginServices(IDalamudPluginInterface pluginInterface)
    {
        if (!ServicesInitialized)
        {
            pluginInterface.Create<PictoService>();
            ServicesInitialized = true;
        }
    }

    public static void Dispose()
    {
        _dxRenderer?.Dispose();
        _dxRenderer = null;
        _vfxRenderer?.Dispose();
        _vfxRenderer = null;
        Framework.Update -= Update;
        if (ClientState != null) ClientState.TerritoryChanged -= OnTerritoryChanged;
        VfxLifetimeWatch.WriteSummary();
    }

    internal static void Update(IFramework framework)
    {
        _vfxRenderer?.Update();
        EchoVfxUnavailableOnce();
    }

    /// <summary>
    /// Helper for getting the current drawlist so it does not need to be passed around.
    /// Must be called within the scope of Draw().
    /// </summary>
    /// <returns>Current drawlist.</returns>
    /// <exception cref="InvalidOperationException">Throws when called before Draw()</exception>
    public static PctDrawList GetDrawList()
    {
        if (DrawList == null)
        {
            throw new InvalidOperationException("GetDrawList called without Draw");
        }
        return DrawList;
    }

    /// <summary>
    /// Begin drawing. Returns nullptr if drawing in unavailable.
    /// </summary>
    /// <param name="imguidrawlist">Optional ImGui drawlist. If unspecified, the background drawlist is used.</param>
    /// <param name="hints">Optional rendering hints. If unspecified, default hints are used.</param>
    /// <returns>Returns nullptr if drawing in unavailable.</returns>
    public static PctDrawList? Draw(ImDrawListPtr? imguidrawlist = null, PctDrawHints? hints = null)
    {
        if (_dxRenderer == null) return null;
        Hints = hints ?? new();
        if (Hints.DrawInCutscene || IsInCutscene()) return null;
        if (Hints.DrawWhenFaded || IsFaded()) return null;
        // Control.Instance() 靠特徵碼解析，解不出時 BeginFrame 與 ImGuiRenderer 每框都會擲；整框不畫。
        if (!IsControlResolved())
        {
            ReportUnresolved(ref ControlReported, "Control.Instance", "overlay drawing disabled");
            return null;
        }
        // fail-closed：原生繪製狀態取不到就整幀不畫；Draw() 本來就約定「不能畫時回 null」。
        if (!DXRenderer.IsRenderStateAvailable()) return null;

        return DrawList = new PctDrawList(
            imguidrawlist ?? ImGui.GetBackgroundDrawList(),
            _dxRenderer,
            _addonClipper
        );
    }

    private static unsafe bool IsControlResolved() => Control.StaticAddressPointers.pInstance != null;

    // 只在框架執行緒（繪製回呼）呼叫；每個缺的位址每次載入只記一行。
    internal static bool ControlReported;
    internal static bool CameraReported;
    internal static bool NodeVisibleReported;
    internal static bool WindowClipReported;

    internal static void ReportUnresolved(ref bool reported, string what, string effect)
    {
        if (reported) return;
        reported = true;
        Log?.Information($"[Pictomancy] Address for {what} is not resolved (signature not found); {effect}.");
    }

    private static bool IsInCutscene()
    {
        return Condition[ConditionFlag.OccupiedInCutSceneEvent] || Condition[ConditionFlag.WatchingCutscene78];
    }

    private unsafe static bool IsFaded()
    {
        var fadeMiddleWidget = (AtkUnitBase*)GameGui.GetAddonByName("FadeMiddle").Address;
        var fadeBlackWidget = (AtkUnitBase*)GameGui.GetAddonByName("FadeBlack").Address;
        return fadeMiddleWidget != null && fadeMiddleWidget->IsVisible ||
            fadeBlackWidget != null && fadeBlackWidget->IsVisible;
    }
}
