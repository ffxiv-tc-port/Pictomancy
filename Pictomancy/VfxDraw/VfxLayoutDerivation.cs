using System;
using System.Collections.Generic;

namespace Pictomancy.VfxDraw;

/// <summary>
/// 從遊戲碼推導 VFX 直接寫入用到的偏移：VfxData 裡的實例指標、實例上的 Scale（3 個 float）與 Color（4 個 float）。
/// </summary>
/// <remarks>
/// 契約：純函式、不碰遊戲記憶體——位元組由呼叫端提供（實機＝SigScanner 的模組副本；離線＝執行檔）。
/// 每個偏移至少兩個來源解得出、而且解得出的全部一致；三個都成立才回 true，否則 VFX 繪製整個停用。
/// 只依賴 BCL，離線探針直接編這個檔來測（推導見 commit 訊息）。
/// </remarks>
internal static class VfxLayoutDerivation
{
    internal const int NotDerived = -1;

    // 呼叫點特徵碼，VfxFunctions 用同一串解析要呼叫的函式：E8 開頭，掃描器會跟到被呼叫的函式開頭。
    internal const string DestroyVfxCall = "E8 ?? ?? ?? ?? 4D 89 A4 DE ?? ?? ?? ??";
    internal const string UpdateVfxTransformCall = "E8 ?? ?? ?? ?? EB 19 48 8B 0B";
    internal const string UpdateVfxColorCall = "E8 ?? ?? ?? ?? 8B 4B F3";

    internal readonly record struct Layout(int InstanceOffset, int ScaleOffset, int ColorOffset);

    /// <param name="Name">寫進 log 的來源名稱。</param>
    /// <param name="Scan">交給掃描器的特徵碼。</param>
    /// <param name="Pattern">讀回的位元組要逐位元組吻合的形狀；null＝與 Scan 相同。</param>
    /// <param name="Decode">解出偏移；形狀內部互相矛盾時回 <see cref="NotDerived"/>。</param>
    internal sealed record Source(string Name, string Scan, string? Pattern, Func<byte[], int> Decode)
    {
        internal string Shape => Pattern ?? Scan;
    }

    // VfxResourceInstance 建構子：Scale 三格、Color 四格（最後一格以 qword 寫）全部初始化成 1.0f。
    private const string InstanceCtor =
        "B8 01 00 00 00 C7 81 ?? ?? ?? ?? 00 00 80 3F C7 81 ?? ?? ?? ?? 00 00 80 3F C7 81 ?? ?? ?? ?? 00 00 80 3F " +
        "C7 81 ?? ?? ?? ?? 00 00 80 3F C7 81 ?? ?? ?? ?? 00 00 80 3F C7 81 ?? ?? ?? ?? 00 00 80 3F 48 C7 81 ?? ?? ?? ?? 00 00 80 3F";

    // VfxData 設色：mov rcx,[rcx+實例] ; test ; je ; 之後與 UpdateVfxColor 開頭相同（依序寫 R,G,B,A）。
    private const string DataSetColor =
        "48 8B 89 ?? ?? ?? ?? 48 85 C9 74 ?? 48 8B 51 08 F3 0F 10 44 24 28 " +
        "F3 0F 11 89 ?? ?? ?? ?? F3 0F 11 91 ?? ?? ?? ?? F3 0F 11 99 ?? ?? ?? ?? F3 0F 11 81 ?? ?? ?? ?? 48 85 D2";

    internal static readonly Source[] InstanceSources =
    [
        // DestroyVfx 開頭：push rbx ; sub rsp,20h ; mov rbx,[rcx+實例] ; test rbx,rbx ; je ; mov rcx,[rbx+8]
        new("DestroyVfx", DestroyVfxCall, "40 53 48 83 EC 20 48 8B 99 ?? ?? ?? ?? 48 85 DB 74 ?? 48 8B 4B 08", b => I32(b, 9)),
        // VfxData 建構子：mov [rbx+實例],rsi ; mov [rbx],rax ; lea rax,監聽者vtable ; mov [rbx+監聽者],rax
        new("VfxData.ctor", "48 89 B3 ?? ?? ?? ?? 48 89 03 48 8D 05 ?? ?? ?? ?? 48 89 83 ?? ?? ?? ??", null, b => AfterListener(I32(b, 3), I32(b, 20))),
        // CreateVfx：mov [rbx+實例],rax ; test rax,rax ; je ; lea rcx,[rbx+監聽者] ; mov [rax+10h],rcx
        new("CreateVfx", "48 8B CE E8 ?? ?? ?? ?? 48 89 83 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8D 8B ?? ?? ?? ?? 48 89 48 10", null, b => AfterListener(I32(b, 11), I32(b, 23))),
        new("VfxData.SetColor", DataSetColor, null, b => Run(b, 26, 8, 4) == NotDerived ? NotDerived : I32(b, 3)),
    ];

    internal static readonly Source[] ScaleSources =
    [
        // UpdateVfxTransform 開頭：把矩陣複製進實例後讀 Scale.X
        new("UpdateVfxTransform", UpdateVfxTransformCall,
            "48 83 EC 78 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 60 0F 28 02 0F 11 41 20 0F 28 4A 10 0F 11 49 30 " +
            "0F 28 42 20 0F 11 41 40 0F 28 4A 30 0F 11 49 50 F3 0F 10 91 ?? ?? ?? ??", b => I32(b, 54)),
        new("VfxResourceInstance.ctor", InstanceCtor, null, b => Run(b, 7, 10, 3)),
        // SetScale：movss [rcx+X],xmm6 ; [rcx+X+4],xmm2 ; [rcx+X+8],xmm7 ; mov rcx,[rcx+60h]
        new("SetScale", "F3 0F 11 B1 ?? ?? ?? ?? F3 0F 11 91 ?? ?? ?? ?? F3 0F 11 B9 ?? ?? ?? ?? 48 8B 49 60", null, b => Run(b, 4, 8, 3)),
    ];

    internal static readonly Source[] ColorSources =
    [
        // UpdateVfxColor 開頭：mov rdx,[rcx+8] ; movss xmm0,[rsp+28h] ; 依序寫 R,G,B,A
        new("UpdateVfxColor", UpdateVfxColorCall,
            "48 8B 51 08 F3 0F 10 44 24 28 F3 0F 11 89 ?? ?? ?? ?? F3 0F 11 91 ?? ?? ?? ?? F3 0F 11 99 ?? ?? ?? ?? F3 0F 11 81 ?? ?? ?? ?? 48 85 D2",
            b => Run(b, 14, 8, 4)),
        new("VfxResourceInstance.ctor", InstanceCtor, null, b => Run(b, 37, 10, 3) is var c && c != NotDerived && I32(b, 68) == c + 12 ? c : NotDerived),
        new("VfxData.SetColor", DataSetColor, null, b => Run(b, 26, 8, 4)),
    ];

    /// <param name="readMatch">給特徵碼與長度，回掃描器解析到的位址起算的位元組；沒命中回 null。</param>
    /// <param name="evidence">每個來源的結果，寫進 log 用。</param>
    internal static bool Derive(Func<string, int, byte[]?> readMatch, out Layout layout, out string evidence)
    {
        var notes = new List<string>();
        var instance = Agree("instance", InstanceSources, readMatch, 0x10, 0x400, 8, notes);
        var scale = Agree("scale", ScaleSources, readMatch, 0x10, 0x200, 4, notes);
        var color = Agree("color", ColorSources, readMatch, 0x10, 0x200, 4, notes);
        var ok = instance != NotDerived && scale != NotDerived && color != NotDerived;
        // Scale 寫 12 bytes、Color 寫 16 bytes，兩段重疊代表至少一個是錯的。
        if (ok && !(scale + 12 <= color || color + 16 <= scale))
        {
            ok = false;
            notes.Add("scale and color overlap");
        }
        evidence = string.Join("; ", notes);
        layout = ok ? new Layout(instance, scale, color) : default;
        return ok;
    }

    private static int Agree(string field, Source[] sources, Func<string, int, byte[]?> readMatch, int min, int max, int align, List<string> notes)
    {
        var found = new List<int>();
        var parts = new List<string>();
        foreach (var source in sources)
        {
            var value = Read(source, readMatch, out var note);
            if (value != NotDerived && (value < min || value >= max || value % align != 0))
            {
                note = $"out of range 0x{value:X}";
                value = NotDerived;
            }
            parts.Add($"{source.Name}={(value == NotDerived ? note : $"0x{value:X}")}");
            if (value != NotDerived) found.Add(value);
        }
        notes.Add($"{field}: {string.Join(", ", parts)}");
        if (found.Count < 2) return NotDerived;
        foreach (var value in found)
        {
            if (value != found[0]) return NotDerived;
        }
        return found[0];
    }

    private static int Read(Source source, Func<string, int, byte[]?> readMatch, out string note)
    {
        var length = SignatureLength(source.Shape);
        var bytes = readMatch(source.Scan, length);
        if (bytes == null || bytes.Length != length)
        {
            note = "no match";
            return NotDerived;
        }
        if (!MatchesFixedBytes(source.Shape, bytes))
        {
            note = "bytes differ";
            return NotDerived;
        }
        var value = source.Decode(bytes);
        note = value == NotDerived ? "inconsistent" : "";
        return value;
    }

    private static int I32(byte[] b, int at) => BitConverter.ToInt32(b, at);

    // 實例指標緊跟在監聽者子物件的 vtable 之後；兩筆寫的是同一個結構才採用。
    private static int AfterListener(int instance, int listener) => instance == listener + 8 ? instance : NotDerived;

    // count 個相鄰 float 欄位：第 i 筆的位移必須是第一筆 + 4*i。
    private static int Run(byte[] b, int first, int stride, int count)
    {
        var value = I32(b, first);
        for (var i = 1; i < count; i++)
        {
            if (I32(b, first + stride * i) != value + 4 * i) return NotDerived;
        }
        return value;
    }

    internal static int SignatureLength(string signature)
        => signature.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    internal static bool MatchesFixedBytes(string signature, byte[] bytes)
    {
        var tokens = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != bytes.Length) return false;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??") continue;
            if (Convert.ToByte(tokens[i], 16) != bytes[i]) return false;
        }
        return true;
    }
}
