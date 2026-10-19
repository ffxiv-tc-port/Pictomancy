<!-- ffxiv-tc-port 繁體中文說明開始 -->
# Pictomancy(台服 fork：ffxiv_pictomancy-sourpuh)

在 Dalamud 插件中繪製 3D 世界疊加層與 VFX 的函式庫：介面類似 ImGui，但畫在世界座標上，
並處理鏡頭後方的裁切與避開遊戲原生介面。

## 艦隊唯一的 Pictomancy 正本

艦隊裡所有用到 Pictomancy 的插件，子模組（路徑 `ffxiv_pictomancy`）都指向**本 repo 的 `tc-7.20` 分支**。
修正只改這裡，再把各消費端的子模組對齊到新頂端。

原本另外兩個 repo `Pictomancy`、`ffxiv_pictomancy-Limiana` 的內容已經併進本 repo，之後不再更新。

### 上游來源

兩個上游都已經離開 API13（sourpuh 是 SDK 15，Limiana 是 SDK 14／net10），不能整個合進來，只當 cherry-pick 的參考來源：

| 上游 | 本 repo 的快照分支 | 說明 |
|---|---|---|
| [sourpuh/ffxiv_pictomancy](https://github.com/sourpuh/ffxiv_pictomancy) | `upstream-sourpuh/master` | 原作者；本 repo 的程式碼以它為基底 |
| [Limiana/ffxiv_pictomancy](https://github.com/Limiana/ffxiv_pictomancy) | `upstream-limiana/master` | sourpuh 版的分支，Splatoon 上游用的是這一支 |

### 消費端（7 個）

| 插件 repo | 說明 |
|---|---|
| `AutoDuty` | |
| `ICE` | |
| `Questionable` | `GatheringPathRenderer` 用到 |
| `Explorers-Icebox` | |
| `Splatoon` | |
| `Ocelot` | 函式庫，經它帶給 `BOCCHI` |
| `BOCCHI` | 經 `Ocelot` 轉手，Ocelot 對齊後 BOCCHI 再對齊 Ocelot |

## 台服 fork 的目的

讓疊加層在原生指標取不到、或遊戲改版後特徵碼對不上時少畫一塊而不是讓遊戲崩潰或整個插件載入失敗，
並降低每幀的記憶體配置。

## 與上游的差異

- **繪製裝置判空**：`Device.Instance()` 取不到時不畫（fail-closed）。
- **介面裁切逐跳判空**：避開原生介面的裁切（`AddonClipper`）讀節點鏈時每一層都先判空，取不到就略過
  那一塊；順手修掉多處讀到節點清單尾端外的越界讀取。
- **遊戲改版後的容錯**：VFX 相關特徵碼對不上時只停用 VFX 繪製、log 列出缺哪幾條，插件照常載入；
  鏡頭、介面節點相關特徵碼對不上時缺的那一塊降級並記一行 log，不再每幀擲例外或靜默消失。
- **降低記憶體用量**：描邊不再為每一筆複製頂點陣列，每幀的描邊路徑暫存區沿用同一份；頂點緩衝區的寫入器與描邊備援繪製器不再每幀重新配置（畫出來的東西不變）。

---

以下為上游原始 README，內容未經修改：

<!-- ffxiv-tc-port 繁體中文說明結束 -->

# FFXIV Pictomancy
Pictomancy is a library for drawing 3D world overlays and VFX in Dalamud plugins.
Pictomancy has an ImGui-like interface that operates in world space instead of a 2D canvas.
Pictomancy simplifies the hard parts of 3D overlays by correctly clipping objects behind the camera and clipping around the native UI.

# Pictomancy is still in development and does not have a stable API. Use at your own risk.

## Installation
Nuget package: https://www.nuget.org/packages/Pictomancy


Use as a git sub-module:
```bash
git submodule add https://github.com/sourpuh/ffxiv_pictomancy
```

## Use
Library initialization:
```c#
public MyPlugin(DalamudPluginInterface pluginInterface)
{
    PictoService.Initialize(pluginInterface);

    ... Your Code Here ...
}

public void Dispose()
{
    PictoService.Dispose();

    ... Your Code Here ...
}
```

### Drawing an ImGui overlay with DirectX Renderer
```c#
using (var drawList = PictoService.Draw())
{
    if (drawList == null)
        return;
    // Draw a circle around a GameObject's hitbox
    Vector3 worldPosition = gameObject.Position;
    float radius = gameObject.HitboxRadius;
    drawList.AddCircleFilled(worldPosition, radius, fillColor);
    drawList.AddCircle(worldPosition, radius, outlineColor);
}
```

### Drawing with in-game VFX
You must specify an ID for each element you draw. IDs should be consistent across frames and unique for each VFX with the same path.

VFX with the same ID and path are retained when drawn in consecutive frames.  If the ID is specified in consecutive frames, the VFX is updated to match the new parameters. If the ID is not specified in consecutive frames, the VFX is destroyed.
```c#
// Draw a circle omen VFX on a GameObject's hitbox
PictoService.VfxRenderer.AddOmen($"{gameobject.EntityId}", "general01bf", gameObject.Position, gameObject.HitboxRadius);

// Draw a tankbuster lockon VFX on a GameObject
PictoService.VfxRenderer.AddLockon($"{gameobject.EntityId}", "tank_lockon01i", gameobject);

// Draw a tether channeling VFX between two GameObjects
PictoService.VfxRenderer.AddChanneling($"{gameobject.EntityId}", "chn_nomal01f", gameobject1, gameobject2);
```

If you want to draw basic Omen shapes, there are helpers provided to draw circles, lines, cones, and donuts. If the method returns void, it will always successfully draw. If the method returns a boolean, it will return false if it did not find an Omen to match your desired shape.
```c#
// Draw a circle around a GameObject's hitbox using the AddCircle helper
PictoService.VfxRenderer.AddCircle($"{gameobject.EntityId}", gameObject.Position, gameObject.HitboxRadius);
```