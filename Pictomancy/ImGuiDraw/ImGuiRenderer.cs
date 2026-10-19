using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Numerics;
using static Pictomancy.ImGuiDraw.Utils;

namespace Pictomancy.ImGuiDraw;
internal class ImGuiRenderer
{
    readonly ImDrawListPtr drawList;
    readonly Matrix4x4 viewProj;
    readonly Vector4 nearPlane;

    public unsafe ImGuiRenderer(ImDrawListPtr drawList)
    {
        this.drawList = drawList;
        // 刻意不對 Control.Instance() 判空——那會是死碼：Control 是 isPointer:false 的靜態位址，
        // 特徵碼失配時擲例外、成功時回傳靜態結構本身的位址，不會回 null（會回 null 的是 Device 那類 isPointer:true 的）。
        viewProj = Control.Instance()->ViewProjectionMatrix;

        // The view matrix in CameraManager is 1 frame stale compared to the Control viewproj matrix.
        // Computing the near plane using the stale view matrix results in clipping errors that look really bad when moving the camera.
        // Instead, compute the view matrix using the accurate viewproj matrix multiplied by the stale inverse proj matrix (Which rarely changes)
        // GetActiveCamera 靠特徵碼解析；解不出時走「沒有鏡頭」那條路（近平面留空），不擲。
        if (CameraManager.MemberFunctionPointers.GetActiveCamera == null)
        {
            PictoService.ReportUnresolved(ref PictoService.CameraReported, "CameraManager.GetActiveCamera", "ImGui fallback strokes are not drawn");
            return;
        }
        var controlCamera = Control.Instance()->CameraManager.GetActiveCamera();
        var renderCamera = controlCamera != null ? controlCamera->SceneCamera.RenderCamera : null;
        if (renderCamera == null)
            return;
        var Proj = renderCamera->ProjectionMatrix;
        if (!Matrix4x4.Invert(Proj, out var InvProj))
            return;
        var View = viewProj * InvProj;

        nearPlane = new(View.M13, View.M23, View.M33, View.M43 + renderCamera->NearPlane);
    }

    public unsafe void DrawStroke(IEnumerable<Vector3> worldCoords, float thickness, uint color, bool closed = false)
    {
        var enumerator = worldCoords.GetEnumerator();
        if (!enumerator.MoveNext()) return;
        Vector3 first = enumerator.Current;
        Vector3 start = first;

        while (enumerator.MoveNext())
        {
            Vector3 stop = enumerator.Current;
            DrawStroke(start, stop, thickness, color);
            start = stop;
        }
        if (closed)
        {
            DrawStroke(start, first, thickness, color);
        }
    }

    public unsafe void DrawStroke(Vector3 start, Vector3 stop, float thickness, uint color)
    {
        var status = ClipLineToPlane(nearPlane, ref start, ref stop, out float _);
        if (status == LineClipStatus.NotVisible)
            return;
        drawList.PathClear();
        drawList.PathLineTo(WorldToScreenOld(viewProj, start));
        drawList.PathLineTo(WorldToScreenOld(viewProj, stop));
        drawList.PathStroke(color, ImDrawFlags.None, thickness);
    }
}
