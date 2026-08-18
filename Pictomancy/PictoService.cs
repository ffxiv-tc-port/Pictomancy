global using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
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

    private static DXRenderer? _dxRenderer;
    private static AddonClipper? _addonClipper;
    private static VfxRenderer? _vfxRenderer;
    public static VfxRenderer VfxRenderer => _vfxRenderer;

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
            // 只攔 RenderContext 對「原生繪製裝置取不到」擲出的受控例外：
            // 讓外掛照樣載入，只是 Draw() 一律回 null（fail-closed，不畫）。
            // 改這裡之前的行為是對 null 解參考 → AccessViolationException → 整個遊戲被帶走。
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
    }

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
    }

    internal static void Update(IFramework framework)
    {
        _vfxRenderer?.Update();
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
        // 🔴 fail-closed：原生繪製狀態取不到就整幀不畫。Draw() 本來就約定「不能畫時回 null」，
        //    呼叫端都已經在處理 null，所以這是既有契約內的安全出口，不會回退任何行為。
        if (!DXRenderer.IsRenderStateAvailable()) return null;

        return DrawList = new PctDrawList(
            imguidrawlist ?? ImGui.GetBackgroundDrawList(),
            _dxRenderer,
            _addonClipper
        );
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
