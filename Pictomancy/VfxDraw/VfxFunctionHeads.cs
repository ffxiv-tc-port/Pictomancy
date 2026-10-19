using System;
using System.Collections.Generic;

namespace Pictomancy.VfxDraw;

/// <summary>
/// 每一支 Pictomancy 會呼叫的遊戲函式：呼叫點特徵碼解出位址之後，函式開頭也要對得上；任何一支對不上就整個停用 VFX 繪製。
/// </summary>
/// <remarks>
/// 契約：純函式、不碰遊戲記憶體——位元組由呼叫端提供（實機＝SigScanner 的模組副本，不受別的插件 hook 影響）。
/// 結構偏移、堆疊大小、rip 位移一律 ??；留下的固定位元組要能把目標和長得像的鄰居分開（推導見 commit 訊息）。
/// 只依賴 BCL 與 <see cref="VfxLayoutDerivation"/>，離線探針直接編這個檔來測。
/// </remarks>
internal static class VfxFunctionHeads
{
    internal const string VfxInitDataCtorCall = "E8 ?? ?? ?? ?? 8D 57 06 48 8D 4C 24 ??";
    internal const string CreateVfxCall = "E8 ?? ?? ?? ?? 48 8B D8 48 8D 95";
    internal const string CreateGameObjectVfxCall = "E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 27 B2 01";
    internal const string RotateMatrixCall = "E8 ?? ?? ?? ?? 4C 8D 76 20";

    internal sealed record Head(string Name, string Call, string Shape);

    internal static readonly Head[] Heads =
    [
        // push rbx ; sub rsp,20h ; movaps xmm0,[常數] ; xor edx,edx ; mov eax,[rcx+X] ; mov rbx,rcx ; movups [rcx],xmm0
        new("VfxInitDataCtor", VfxInitDataCtorCall,
            "40 53 48 83 EC 20 0F 28 05 ?? ?? ?? ?? 33 D2 8B 81 ?? ?? ?? ?? 48 8B D9 0F 11 01"),
        // 存 rbx/rsi/rdi/r12、push rbp/r14/r15 ; 先比 VFX 數量上限 ; a3/a4 取 byte ; rbx=init、rdi=path（孿生函式的開頭不同）
        new("CreateVfx", CreateVfxCall,
            "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 4C 89 64 24 20 55 41 56 41 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? " +
            "81 3D ?? ?? ?? ?? ?? ?? ?? ?? 45 0F B6 F1 45 0F B6 F8 48 8B DA 48 8B F9"),
        // rcx=path、rdx=target、r8=source、xmm3=a4、第 5 個參數取 byte ; 之後判 target 是否為空
        new("CreateGameObjectVfx", CreateGameObjectVfxCall,
            "40 53 55 56 57 48 81 EC ?? ?? ?? ?? 0F 29 B4 24 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? " +
            "0F B6 AC 24 ?? ?? ?? ?? 0F 28 F3 49 8B F8 48 8B F2 48 8B D9"),
        // 繞 Y 軸：sin／cos 之後先讀 [rbx+28h]、[rbx+20h]。繞 X 軸（28h/24h）、繞 Z 軸（24h/20h）的開頭其餘位元組完全相同
        new("RotateMatrix", RotateMatrixCall,
            "40 53 48 81 EC ?? ?? ?? ?? 0F 29 74 24 ?? 0F 28 C1 0F 29 7C 24 ?? 0F 28 F1 44 0F 29 44 24 ?? 48 8B D9 " +
            "44 0F 29 4C 24 ?? 44 0F 29 54 24 ?? 44 0F 29 5C 24 ?? E8 ?? ?? ?? ?? 44 0F 28 D8 0F 28 C6 E8 ?? ?? ?? ?? " +
            "F3 0F 10 7B 28 44 0F 28 D0 F3 0F 10 73 20"),
        // 讀實例 ; 放掉資源參考 ; 清掉實例的保留位元（and byte [rbx+X],0FEh）
        new("DestroyVfx", VfxLayoutDerivation.DestroyVfxCall,
            "40 53 48 83 EC 20 48 8B 99 ?? ?? ?? ?? 48 85 DB 74 ?? 48 8B 4B 08 48 85 C9 74 ?? 33 C0 F0 0F C1 41 0C 83 F8 01 75 ?? " +
            "48 8B 4B ?? 33 D2 E8 ?? ?? ?? ?? 80 A3 ?? ?? ?? ?? FE"),
        // 安全 cookie 之後用 movaps 從 [rdx] 讀四列複製進實例
        new("UpdateVfxTransform", VfxLayoutDerivation.UpdateVfxTransformCall,
            "48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 0F 28 02 0F 11 41 ?? 0F 28 4A 10 0F 11 49 ?? " +
            "0F 28 42 20 0F 11 41 ?? 0F 28 4A 30 0F 11 49 ??"),
        // mov rdx,[rcx+8] ; 取第 5 個參數 ; 依序寫 R,G,B,A
        new("UpdateVfxColor", VfxLayoutDerivation.UpdateVfxColorCall,
            "48 8B 51 08 F3 0F 10 44 24 28 F3 0F 11 89 ?? ?? ?? ?? F3 0F 11 91 ?? ?? ?? ?? F3 0F 11 99 ?? ?? ?? ?? F3 0F 11 81 ?? ?? ?? ??"),
    ];

    /// <param name="readMatch">給特徵碼與長度，回掃描器解析到的位址起算的位元組；沒命中回 null。</param>
    /// <param name="evidence">結果摘要，寫進 log 用。</param>
    internal static bool Check(Func<string, int, byte[]?> readMatch, out string evidence)
    {
        var bad = new List<string>();
        foreach (var head in Heads)
        {
            var length = VfxLayoutDerivation.SignatureLength(head.Shape);
            var bytes = readMatch(head.Call, length);
            if (bytes == null || bytes.Length != length)
                bad.Add($"{head.Name}=no match");
            else if (!VfxLayoutDerivation.MatchesFixedBytes(head.Shape, bytes))
                bad.Add($"{head.Name}=head differs");
        }
        evidence = bad.Count == 0
            ? $"function heads {Heads.Length}/{Heads.Length} match"
            : $"function heads differ: {string.Join(", ", bad)}";
        return bad.Count == 0;
    }
}
