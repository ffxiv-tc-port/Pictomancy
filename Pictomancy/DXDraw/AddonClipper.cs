using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pictomancy.DXDraw;

/**
 * Automatically clip FFXIV native UI elements so they "appear" to be in front of Pictomancy's overlay.
 */
internal class AddonClipper
{
    private readonly static string[] _actionBarAddonNames = { "_ActionBar", "_ActionBar01", "_ActionBar02", "_ActionBar03", "_ActionBar04", "_ActionBar05", "_ActionBar06", "_ActionBar07", "_ActionBar08", "_ActionBar09", "_ActionBarEx" };
    private readonly static string[] _statusAddonNames = { "_StatusCustom0", "_StatusCustom1", "_StatusCustom2" };
    private readonly static List<string> _ignoredAddonNames = ["_FocusTargetInfo"];
    private readonly static Vector2 _diamondScale = new(0.75f);
    private readonly static Vector2 _gaugeTextScale = new(0.6f);
    private readonly static Vector2 _resourceBarScale = new Vector2(0.95f, 0.7f);

    private DXRenderer? _renderer;
    const int numCircleSegments = 16;
    private Vector2[] circleOffsets;

    public AddonClipper()
    {
        circleOffsets = new Vector2[numCircleSegments + 1];
        float angleStep = MathF.PI * 2 / numCircleSegments;

        for (int step = 0; step <= numCircleSegments; step++)
        {
            float angle = MathF.PI / 2 + step * angleStep;
            circleOffsets[step] = new(MathF.Cos(angle), MathF.Sin(angle));
        }
    }

    public void Clip(DXRenderer renderer)
    {
        _renderer = renderer;
        ClipCastBar();
        ClipMainTargetInfo();
        ClipTargetInfoCastBar();
        ClipTargetInfoStatus();
        ClipFocusTarget();
        ClipActionBars();
        ClipActionCross();
        ClipPartyList();
        // ClipChatBubbles();
        ClipEnemyList();
        ClipStatuses();
        ClipParameterWidget();
        ClipLimitBreak();
        ClipMinimap();
        ClipMainCommand();
        // ClipChat();
        ClipNamePlates();

        ClipPld();
        ClipWar();
        ClipDrk();
        ClipGnb();

        ClipMnk();
        ClipDrg();
        ClipNin();
        ClipSam();
        ClipRpr();
        ClipVpr();

        ClipWhm();
        ClipSch();
        ClipAst();
        ClipSge();

        ClipBrd();
        ClipMch();
        ClipDnc();

        ClipBlm();
        ClipSmn();
        ClipRdm();
        ClipPct();

        ClipWindows();
        _renderer = null;
    }

    private unsafe void ClipPartyList()
    {
        var addon = GetVisibleAddonOrNull("_PartyList", 24);
        if (addon == null) return;

        for (int i = 6; i <= 23; i++)
        {
            AtkResNode* slotNode = GetNodeOrNull(addon, i);
            if (slotNode is null) continue;

            if (slotNode->IsVisible())
            {
                Vector2 pos = new Vector2(
                    slotNode->ScreenX + (18f * addon->Scale),
                    slotNode->ScreenY
                    );
                Vector2 size = new Vector2(
                    slotNode->Width * addon->Scale - 25f * addon->Scale,
                    slotNode->Height * addon->Scale - 5f * addon->Scale
                    );

                _renderer!.AddClipRect(pos, size);
            }
        }
    }

    private unsafe void ClipEnemyList()
    {
        var addon = GetVisibleAddonOrNull("_EnemyList", 12);
        if (addon == null || !addon->IsVisible || addon->UldManager.NodeList == null) return;

        for (int i = 4; i <= 11; i++)
        {
            ClipAtkNodeRectangle(GetNodeOrNull(addon, i));
        }
    }

    public unsafe void ClipWindows()
    {
        AtkStage* stage = AtkStage.Instance();
        if (stage == null) { return; }

        RaptureAtkUnitManager* manager = stage->RaptureAtkUnitManager;
        if (manager == null) { return; }

        AtkUnitList* loadedUnitsList = &manager->AtkUnitManager.AllLoadedUnitsList;
        if (loadedUnitsList == null) { return; }

        // Count 由遊戲寫入,超出 Entries 容量時夾住,不要越界讀。
        int unitCount = Math.Min((int)loadedUnitsList->Count, loadedUnitsList->Entries.Length);
        string name = "";
        for (int i = 0; i < unitCount; i++)
        {
            try
            {
                AtkUnitBase* addon = *(AtkUnitBase**)Unsafe.AsPointer(ref loadedUnitsList->Entries[i]);
                if (addon == null
                    || !addon->IsVisible
                    || addon->WindowNode == null
                    || addon->Scale == 0
                    || addon->RootNode == null
                    || !addon->RootNode->IsVisible())
                {
                    continue;
                }

                name = addon->NameString;
                if (name != null && _ignoredAddonNames.Contains(name))
                {
                    continue;
                }

                float margin = 5 * addon->Scale;
                float bottomMargin = 13 * addon->Scale;

                Vector2 pos = new Vector2(addon->X + margin, addon->Y + margin);
                Vector2 size = new Vector2(
                    addon->WindowNode->AtkResNode.Width * addon->Scale - margin,
                    addon->WindowNode->AtkResNode.Height * addon->Scale - bottomMargin
                );

                // special case for duty finder
                if (name == "ContentsFinder")
                {
                    size.X += size.X + (16 * addon->Scale);
                    size.Y += (30 * addon->Scale);
                }

                _renderer!.AddClipRect(pos, size);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Pictomancy exception during {name} window clip; loadedUnitsList count: {loadedUnitsList->Count}", e);
            }
        }
    }
    private unsafe void ClipCastBar()
    {
        var addon = GetVisibleAddonOrNull("_CastBar", 9);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 3), scale: _resourceBarScale);
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 8), scale: new(0.95f, 0.85f));
    }
    private unsafe void ClipMainTargetInfo()
    {
        var addon = GetVisibleAddonOrNull("_TargetInfoMainTarget", 11);
        if (addon == null) return;
        var gaugeBar = GetNodeOrNull(addon, 5);
        if (gaugeBar == null || !gaugeBar->IsVisible()) return;
        ClipAtkNodeRectangle(GetComponentNodeOrNull(gaugeBar, 0));
        var nameNode = GetNodeOrNull(addon, 9);
        if (nameNode != null && nameNode->IsVisible())
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 10));
    }

    private unsafe void ClipTargetInfoCastBar()
    {
        var addon = GetVisibleAddonOrNull("_TargetInfoCastBar", 3);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 2), scale: new Vector2(0.93f, 0.42f));
    }

    private unsafe void ClipTargetInfoStatus()
    {
        var addon = GetVisibleAddonOrNull("_TargetInfoBuffDebuff", 32);
        if (addon == null) return;
        for (int i = 2; i <= 31; i++)
        {
            var status = GetNodeOrNull(addon, i);
            if (status == null || !status->IsVisible()) continue;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(status, 1));
            ClipAtkNodeRectangle(GetComponentNodeOrNull(status, 2), scale: _gaugeTextScale);
        }
    }
    private unsafe void ClipFocusTarget()
    {
        var addon = GetVisibleAddonOrNull("_FocusTargetInfo", 16);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 2));
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 16));
    }
    private unsafe void ClipActionBars()
    {
        foreach (string addonName in _actionBarAddonNames)
        {
            var addon = GetVisibleAddonOrNull(addonName, 1);
            if (addon == null) continue;
            for (int i = 9; i <= 20; i++)
            {
                var hotbarBtn = GetNodeOrNull(addon, i);
                if (hotbarBtn == null || !hotbarBtn->IsVisible()) continue;
                ClipAtkNodeRectangle(GetComponentNodeOrNull(hotbarBtn, 0));
            }
        }
    }

    private unsafe void ClipActionCross()
    {
        {
            var addon = GetVisibleAddonOrNull("_ActionCross", 12);
            if (addon == null) return;
            for (int i = 8; i <= 11; i++)
            {
                ClipCrossButtonGroup(GetNodeOrNull(addon, i));
            }
        }

        {
            var addon = GetVisibleAddonOrNull("_ActionDoubleCrossL", 7);
            if (addon == null) return;
            ClipCrossButtonGroup(GetNodeOrNull(addon, 5));
            ClipCrossButtonGroup(GetNodeOrNull(addon, 6));
        }

        {
            var addon = GetVisibleAddonOrNull("_ActionDoubleCrossR", 7);
            if (addon == null) return;
            ClipCrossButtonGroup(GetNodeOrNull(addon, 5));
            ClipCrossButtonGroup(GetNodeOrNull(addon, 6));
        }
    }

    private unsafe void ClipCrossButtonGroup(AtkResNode* buttonGroup)
    {
        if (buttonGroup == null || !buttonGroup->IsVisible()) return;
        for (int j = 0; j <= 3; j++)
        {
            ClipAtkNodeRectangle(GetComponentNodeOrNull(buttonGroup, j));
        }
    }

    private unsafe void ClipStatuses()
    {
        foreach (string addonName in _statusAddonNames)
        {
            var addon = GetVisibleAddonOrNull(addonName, 25);
            if (addon == null) continue;

            for (int i = 5; i <= 24; i++)
            {
                var status = GetNodeOrNull(addon, i);
                if (status == null || !status->IsVisible()) continue;
                ClipAtkNodeRectangle(GetComponentNodeOrNull(status, 1));
                ClipAtkNodeRectangle(GetComponentNodeOrNull(status, 2), scale: _gaugeTextScale);
            }
        }
    }

    private unsafe void ClipChatBubbles()
    {
        var addon = GetVisibleAddonOrNull("_MiniTalk", 11);
        if (addon == null) return;
        for (int i = 1; i <= 10; i++)
        {
            AtkResNode* node = GetNodeOrNull(addon, i);
            if (node == null || !node->IsVisible()) continue;

            ClipAtkNodeRectangle(GetComponentNodeOrNull(node, 1));
        }
    }

    private unsafe void ClipParameterWidget()
    {
        var addon = GetVisibleAddonOrNull("_ParameterWidget", 3);
        if (addon == null) return;
        // HP
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 2), 0), scale: _resourceBarScale);
        // MP
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 1), 0), scale: _resourceBarScale);
    }
    private unsafe void ClipLimitBreak()
    {
        var addon = GetVisibleAddonOrNull("_LimitBreak", 5);
        if (addon == null) return;
        for (int i = 2; i <= 4; i++)
        {
            var barNode = GetNodeOrNull(addon, i);
            if (barNode != null && barNode->DrawFlags == 0)
                ClipAtkNodeRectangle(barNode);
        }
    }
    private unsafe void ClipChat()
    {
        var addon = GetVisibleAddonOrNull("ChatLog");
        if (addon == null) return;
        // ClipAtkNodeRectangle(GetNodeOrNull(addon, 3), null, 0.5f, true);
    }
    private unsafe void ClipMinimap()
    {
        var addon = GetVisibleAddonOrNull("_NaviMap", 16);
        if (addon == null) return;
        ClipAtkNodeCircle(GetNodeOrNull(addon, 6));
        ClipAtkNodeCircle(GetNodeOrNull(addon, 8));
        ClipAtkNodeCircle(GetNodeOrNull(addon, 15));
    }
    private unsafe void ClipMainCommand()
    {
        var addon = GetVisibleAddonOrNull("_MainCommand", 8);
        if (addon == null) return;
        for (int i = 1; i <= 7; i++)
        {
            ClipAtkNodeCircle(GetNodeOrNull(addon, i));
        }
    }

    private unsafe void ClipNamePlates()
    {
        var addon = GetVisibleAddonOrNull("NamePlate", 50);
        if (addon == null) return;
        for (int i = 1; i <= 50; i++)
        {
            AtkResNode* node = GetNodeOrNull(addon, i);
            if (node == null || !node->IsVisible()) continue;

            ClipAtkNodeRectangle(GetComponentNodeOrNull(node, 4));
        }
    }
    private unsafe void ClipPld()
    {
        var addon = GetVisibleAddonOrNull("JobHudPLD0", 5);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
        ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
    }

    private unsafe void ClipWar()
    {
        var addon = GetVisibleAddonOrNull("JobHudWAR0", 5);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
        ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
    }
    private unsafe void ClipDrk()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudDRK0", 5);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudDRK1", 6);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 5), scale: _diamondScale);
        }
    }
    private unsafe void ClipGnb()
    {
        var addon = GetVisibleAddonOrNull("JobHudGNB0", 7);
        if (addon == null) return;
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 4), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 5), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 6), scale: _diamondScale);
    }
    private unsafe void ClipMnk()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudMNK0", 30);
            if (addon == null) return;
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 6));
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 7));
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 8));
            ClipAtkNodeCircle(GetNodeOrNull(addon, 11), scale: new(0.7f));
            ClipAtkNodeCircle(GetNodeOrNull(addon, 14), scale: new(0.7f));
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 21), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 22), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 26), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 29), scale: _diamondScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 19), scale: new(0.7f));
            ClipAtkNodeCircle(GetNodeOrNull(addon, 24), scale: new(0.7f));
            ClipAtkNodeCircle(GetNodeOrNull(addon, 28), scale: new(0.7f));
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudMNK1", 8);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 3), scale: new(0.55f));
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 4), scale: new(0.55f));
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 5), scale: new(0.55f));
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 6), scale: new(0.55f));
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 7), scale: new(0.55f));
        }
    }
    private unsafe void ClipDrg()
    {
        var addon = GetVisibleAddonOrNull("JobHudDRG0", 9);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 7), scale: new(0.7f));
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 8), scale: new(0.7f));
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
    }
    private unsafe void ClipNin()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudNIN0", 5);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudNIN1v70", 8);
            if (addon == null) return;
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 3), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 4), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 5), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 6), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 7), scale: _diamondScale);
        }
    }

    private unsafe void ClipSam()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudSAM0", 9);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 6), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 7), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 8), scale: _diamondScale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudSAM1", 12);
            if (addon == null) return;
            var scale = new Vector2(0.6f);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: scale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 7), scale: scale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 11), scale: scale);
        }
    }
    private unsafe void ClipRpr()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudRRP0", 8);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 6), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 7), scale: _gaugeTextScale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudRRP1", 7);
            if (addon == null) return;
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 2), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 3), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 4), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 5), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 6), scale: _diamondScale);
        }
    }
    private unsafe void ClipVpr()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudRDB0", 10);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 5), 0), scale: _resourceBarScale);
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale * new Vector2(-1, 1));
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 7), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 8), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 9), scale: _diamondScale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudRDB1", 33);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 25), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 26), scale: _gaugeTextScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 29), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 30), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 31), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 32), scale: _diamondScale);
        }
    }
    private unsafe void ClipWhm()
    {
        var addon = GetVisibleAddonOrNull("JobHudWHM0", 13);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 6), scale: new(0.55f));
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 7), scale: new(0.55f));
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 8), scale: new(0.55f));
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 10), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 11), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 12), scale: _diamondScale);
    }
    private unsafe void ClipSch()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudACN0", 5);
            if (addon == null) return;
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 2), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 3), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 4), scale: _diamondScale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudSCH0", 12);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
        }
    }
    private unsafe void ClipAst()
    {
        var addon = GetVisibleAddonOrNull("JobHudAST0", 10);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 6), scale: new(0.75f));
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 7), scale: new(0.75f));
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 8), scale: new(0.75f));
        ClipAtkNodeRectangle(GetNodeOrNull(addon, 9), scale: new(0.75f));
    }
    private unsafe void ClipSge()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudGFF0", 2);
            if (addon == null) return;
            ClipAtkNodeCircle(GetNodeOrNull(addon, 1));
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudGFF1", 12);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 5), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 6), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 7), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 9), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 10), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 11), scale: _diamondScale);
        }
    }
    private unsafe void ClipBrd()
    {
        var addon = GetVisibleAddonOrNull("JobHudBRD0", 14);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 4), 0), scale: _resourceBarScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 8), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 9), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 10), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 11), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 12), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 13), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 14), scale: _diamondScale);
        ClipAtkNodeCircle(GetNodeOrNull(addon, 5), scale: _gaugeTextScale);
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 16), 0), scale: _resourceBarScale);
        ClipAtkNodeCircle(GetNodeOrNull(addon, 17), scale: _gaugeTextScale);
    }
    private unsafe void ClipMch()
    {
        var addon = GetVisibleAddonOrNull("JobHudMCH0", 10);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
        ClipAtkNodeCircle(GetNodeOrNull(addon, 4), scale: _gaugeTextScale);
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 8), 0), scale: _resourceBarScale);
        ClipAtkNodeCircle(GetNodeOrNull(addon, 9), scale: _gaugeTextScale);
    }
    private unsafe void ClipDnc()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudDNC1", 26);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 19), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 20), scale: _gaugeTextScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 22), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 23), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 24), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 25), scale: _diamondScale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudDNC0", 26);
            if (addon == null) return;
            ClipAtkNodeCircle(GetNodeOrNull(addon, 27), scale: new(0.7f));
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 34));
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 35));
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 36));
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 37));
        }
    }
    private unsafe void ClipBlm()
    {
        var addon = GetVisibleAddonOrNull("JobHudBLM0", 21);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 4), 0), scale: _resourceBarScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 5), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 6), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 7), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 13), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 14), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 15), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 20), scale: _diamondScale);
    }
    private unsafe void ClipSmn()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudSMN0", 4);
            if (addon == null) return;
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 2), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 3), scale: _diamondScale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudSMN1", 12);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 6), 0), scale: new(0.4f, 0.6f));
            ClipAtkNodeDiamond(GetComponentNodeOrNull(GetNodeOrNull(addon, 7), 0), scale: _diamondScale);
            ClipAtkNodeCircle(GetComponentNodeOrNull(GetNodeOrNull(addon, 8), 0), scale: new(0.5f, 0.6f));
            ClipAtkNodeCircle(GetComponentNodeOrNull(GetNodeOrNull(addon, 11), 1));
        }
    }

    private unsafe void ClipRdm()
    {
        var addon = GetVisibleAddonOrNull("JobHudRDM0", 15);
        if (addon == null) return;
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
        ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 4), 0), scale: _resourceBarScale);
        ClipAtkNodeCircle(GetNodeOrNull(addon, 15), scale: _gaugeTextScale);
        ClipAtkNodeCircle(GetNodeOrNull(addon, 16), scale: _gaugeTextScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 6), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 12), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 13), scale: _diamondScale);
        ClipAtkNodeDiamond(GetNodeOrNull(addon, 14), scale: _diamondScale);
    }
    private unsafe void ClipPct()
    {
        {
            var addon = GetVisibleAddonOrNull("JobHudRPM0", 12);
            if (addon == null) return;
            var scale = new Vector2(0.8f, 0.75f);
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 4), scale: scale);
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 7), scale: scale);
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 10), scale: scale);
            ClipAtkNodeRectangle(GetNodeOrNull(addon, 11), scale: scale);
        }
        {
            var addon = GetVisibleAddonOrNull("JobHudRPM1", 14);
            if (addon == null) return;
            ClipAtkNodeRectangle(GetComponentNodeOrNull(GetNodeOrNull(addon, 3), 0), scale: _resourceBarScale);
            ClipAtkNodeCircle(GetNodeOrNull(addon, 7));

            ClipAtkNodeDiamond(GetNodeOrNull(addon, 9), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 10), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 11), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 12), scale: _diamondScale);
            ClipAtkNodeDiamond(GetNodeOrNull(addon, 13), scale: _diamondScale);
        }
    }

    /// <summary>
    /// 取得 addon 節點清單中指定索引的節點。節點清單指標為 null、索引越界(以 NodeListCount 為上界)
    /// 或該格為 null 時回 null;呼叫端只會少畫一塊裁切區域,不會裸解參考。
    /// </summary>
    private static unsafe AtkResNode* GetNodeOrNull(AtkUnitBase* addon, int index)
    {
        if (addon == null) return null;
        var nodeList = addon->UldManager.NodeList;
        if (nodeList == null || index < 0 || index >= addon->UldManager.NodeListCount) return null;
        return nodeList[index];
    }

    /// <summary>
    /// 取得節點底下元件的子節點。鏈上每一跳都可能取不到:節點本身、
    /// GetAsAtkComponentNode()(型別不符時回 null)、Component、元件的節點清單與其上界。
    /// 任何一跳取不到就回 null,不繼續解參考。
    /// </summary>
    private static unsafe AtkResNode* GetComponentNodeOrNull(AtkResNode* node, int index)
    {
        if (node == null) return null;
        var componentNode = node->GetAsAtkComponentNode();
        if (componentNode == null) return null;
        var component = componentNode->Component;
        if (component == null) return null;
        var nodeList = component->UldManager.NodeList;
        if (nodeList == null || index < 0 || index >= component->UldManager.NodeListCount) return null;
        return nodeList[index];
    }

    private unsafe AtkUnitBase* GetVisibleAddonOrNull(string name, int expectedNodeCount = 0)
    {
        var addon = (AtkUnitBase*)PictoService.GameGui.GetAddonByName(name).Address;
        if (addon == null || !addon->IsVisible || addon->VisibilityFlags != 0 || addon->UldManager.NodeListCount < expectedNodeCount) return null;
        return addon;
    }

    private unsafe void ClipAtkNodeRectangle(AtkResNode* node, float alpha = 0, Vector2? scale = null)
    {
        if (!GetNodeVisible(node)) return;
        var nodeScale = GetNodeScale(node);
        var actualSize = new Vector2(node->Width, node->Height) * nodeScale;
        var extraScale = scale ?? Vector2.One;
        var padding = actualSize * (Vector2.One - extraScale) / 2;
        var size = actualSize * extraScale;

        _renderer!.AddClipRect(new Vector2(node->ScreenX, node->ScreenY) + padding, size, alpha);
    }
    private unsafe void ClipAtkNodeCircle(AtkResNode* node, float alpha = 0, Vector2? scale = null)
    {
        if (!GetNodeVisible(node)) return;
        var nodeScale = GetNodeScale(node);
        float xRadius = 0.5f * node->Width * nodeScale.X;
        float yRadius = 0.5f * node->Height * nodeScale.Y;
        Vector2 radius = new(xRadius, yRadius);
        Vector2 center = new Vector2(node->ScreenX, node->ScreenY) + radius;
        radius *= scale ?? Vector2.One;

        for (int i = 0; i < numCircleSegments; i++)
        {
            _renderer!.AddClipTri(center, center + circleOffsets[i] * radius, center + circleOffsets[i + 1] * radius, alpha);
        }
    }
    private unsafe void ClipAtkNodeDiamond(AtkResNode* node, float alpha = 0, Vector2? scale = null)
    {
        if (!GetNodeVisible(node)) return;
        var nodeScale = GetNodeScale(node);
        float xRadius = 0.5f * node->Width * nodeScale.X;
        float yRadius = 0.5f * node->Height * nodeScale.Y;
        Vector2 radius = new(xRadius, yRadius);
        Vector2 center = new Vector2(node->ScreenX, node->ScreenY) + radius;
        radius *= scale ?? Vector2.One;

        _renderer!.AddClipTri(center + circleOffsets[0] * radius, center + circleOffsets[4] * radius, center + circleOffsets[8] * radius, alpha);
        _renderer!.AddClipTri(center + circleOffsets[8] * radius, center + circleOffsets[12] * radius, center + circleOffsets[0] * radius, alpha);
    }

    private static unsafe Vector2 GetNodeScale(AtkResNode* node)
    {
        if (node == null) return Vector2.One;
        var scale = new Vector2(node->ScaleX, node->ScaleY);
        while (node->ParentNode != null)
        {
            node = node->ParentNode;
            scale *= new Vector2(node->ScaleX, node->ScaleY);
        }

        return scale;
    }

    private static unsafe bool GetNodeVisible(AtkResNode* node)
    {
        if (node == null) return false;
        while (node != null)
        {
            if (!node->NodeFlags.HasFlag(NodeFlags.Visible)) return false;
            node = node->ParentNode;
        }

        return true;
    }
}
