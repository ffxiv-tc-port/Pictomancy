using System;
using System.Threading;

namespace Pictomancy.VfxDraw;

/// <summary>
/// 持有中的 VfxData 讀到實例為 0（不見）或別的值（被換掉）那一刻就判定「已忘記」：持有者之後不再讀它、
/// 不交給 UpdateVfx*、也不交給 DestroyVfx。判定只用現有程式碼本來就讀到的實例指標值，不多讀原生記憶體。
/// </summary>
/// <remarks>
/// 遊戲每幀釋放實例為 0 的 VfxData、建立後只有建立函式會寫入非 0 實例、DestroyVfx 對實例為 0 的是 no-op（推導見 commit 訊息）⇒
/// 忘掉對還活著的 VfxData 行為等價，對已釋放的則避開使用已釋放記憶體。
/// 每種事件只在第 1、10、100…次寫一行；卸載時寫一行彙總。計數可跨執行緒；單一 <see cref="State"/> 只由持有它的物件更新。
/// 只依賴 BCL，離線探針直接編這個檔來測。
/// </remarks>
internal static class VfxLifetimeWatch
{
    internal enum Phase : byte { Normal, Gone, Replaced }

    internal struct State
    {
        internal ulong Expected; // 建立時讀到的實例指標；0＝不知道（Wrap 進來的，採用第一次讀到的使用者位址）
        internal Phase Phase;
        internal bool Held;
        internal string? Owner;
    }

    // 同一個持有者（或 VfxRenderer 的同一個鍵）的建立次數上限：遊戲一直收掉或一直建不出來時，
    // 每個時間窗最多呼叫建立函式這麼多次，不會每幀建一個。
    internal struct RebuildBudget
    {
        internal long WindowStart;
        internal int Count;
    }

    internal const int MaxRebuildsPerWindow = 3;
    internal const long RebuildWindowMs = 5000;

    internal readonly record struct Counters(
        long Tracked, long GoneAtCreation, long Gone, long Replaced, long Skipped, long RebuildsDeferred,
        long CreateFailed, long CreateRetriesDeferred, long TerritoryWhileHeld, long MaxHeldAtTerritory, long Held);

    internal static Action<string>? Sink;

    private static long tracked, goneAtCreation, gone, replaced, skipped, rebuildsDeferred, createFailed, createRetriesDeferred;
    private static long territoryWhileHeld, maxHeldAtTerritory, held;
    private static long lastTerritoryTick;

    internal static bool IsUserPointer(ulong value) => value >= 0x10000 && value <= 0x7FFF_FFFF_FFFF;

    /// <summary>true＝已忘記：持有者不可再讀這個 VfxData 的任何欄位、不可交給 UpdateVfx*／DestroyVfx。</summary>
    internal static bool IsForgotten(in State state) => state.Held && state.Phase != Phase.Normal;

    /// <param name="instance">建立時讀到的實例指標原值（known 為 false 時忽略）。</param>
    /// <param name="known">false＝外部交進來的 VfxData，不知道它原本的實例。</param>
    /// <returns>false＝建立時就沒有實例，已忘記（持有者要丟掉指標）。</returns>
    internal static bool Begin(ref State state, ulong instance, bool known, string owner)
    {
        End(ref state);
        state = new State { Expected = known ? instance : 0, Held = true, Owner = owner };
        Interlocked.Increment(ref held);
        Interlocked.Increment(ref tracked);
        if (!known || instance != 0) return true;
        state.Phase = Phase.Gone;
        var n = Interlocked.Increment(ref goneAtCreation);
        if (IsMilestone(n))
            Write($"[Pictomancy] VFX lifetime #{n}: the game created a {owner} without an instance; it frees such a VfxData on its next pass, so the pointer is forgotten right away.");
        return false;
    }

    internal static void End(ref State state)
    {
        if (state.Held) Interlocked.Decrement(ref held);
        state = default;
    }

    /// <param name="raw">現有程式碼這一次本來就讀到的實例指標原值。</param>
    /// <returns>true＝實例照舊、可以用；false＝已忘記（這一刻起，或早就忘了），持有者要丟掉指標。</returns>
    internal static bool Observe(ref State state, ulong raw)
    {
        if (!state.Held) return true;
        if (state.Phase != Phase.Normal) return false;
        if (raw != 0 && raw == state.Expected) return true;
        if (state.Expected == 0 && IsUserPointer(raw))
        {
            state.Expected = raw;
            return true;
        }
        if (raw == 0)
        {
            state.Phase = Phase.Gone;
            var n = Interlocked.Increment(ref gone);
            if (IsMilestone(n))
                Write($"[Pictomancy] VFX lifetime #{n}: instance gone on a held {state.Owner} ({SinceTerritory()}); the game frees such a VfxData, so the pointer is forgotten (no more reads, updates or DestroyVfx on it).");
            return false;
        }
        state.Phase = Phase.Replaced;
        var r = Interlocked.Increment(ref replaced);
        if (IsMilestone(r))
            Write($"[Pictomancy] VFX lifetime #{r}: a held {state.Owner} now reads instance 0x{raw:X} instead of 0x{state.Expected:X}{(IsUserPointer(raw) ? "" : " (not a user address)")} ({SinceTerritory()}); its memory was freed and reused, so the pointer is forgotten (no more reads, updates or DestroyVfx on it).");
        return false;
    }

    /// <summary>持有者因為已忘記而略過一次讀取或遊戲呼叫時呼叫；沒忘記的不算。</summary>
    internal static void NoteSkipped(ref State state, string what)
    {
        if (!IsForgotten(state)) return;
        var n = Interlocked.Increment(ref skipped);
        if (IsMilestone(n))
            Write($"[Pictomancy] VFX lifetime: skipped {n} use(s) of a forgotten VfxData (this one: {what} on a {state.Owner}); nothing was read or passed to the game.");
    }

    /// <summary>重建前呼叫；false＝這個時間窗已經重建過上限次數，這次不建。</summary>
    internal static bool TryRebuild(ref RebuildBudget budget, long nowMs, string owner)
    {
        if (Spend(ref budget, nowMs)) return true;
        var n = Interlocked.Increment(ref rebuildsDeferred);
        if (IsMilestone(n))
            Write($"[Pictomancy] VFX lifetime #{n}: deferred rebuilding a {owner} the game keeps removing (at most {MaxRebuildsPerWindow} rebuilds per {RebuildWindowMs / 1000} s per VFX).");
        return false;
    }

    /// <summary>遊戲沒建出來時呼叫（第一次與重試都算）。回傳第一次失敗時該存下的額度：這一次已算進去。</summary>
    internal static RebuildBudget NoteCreateFailed(long nowMs, string owner, string path)
    {
        var n = Interlocked.Increment(ref createFailed);
        if (IsMilestone(n))
            Write($"[Pictomancy] VFX lifetime #{n}: the game did not create a {owner} for {path}; retrying at most {MaxRebuildsPerWindow} times per {RebuildWindowMs / 1000} s per VFX.");
        return new RebuildBudget { WindowStart = nowMs, Count = 1 };
    }

    /// <summary>重試建立前呼叫；false＝這個時間窗已經試過上限次數，這次不呼叫建立函式。</summary>
    internal static bool TryRetryCreate(ref RebuildBudget budget, long nowMs, string owner)
    {
        if (Spend(ref budget, nowMs)) return true;
        var n = Interlocked.Increment(ref createRetriesDeferred);
        if (IsMilestone(n))
            Write($"[Pictomancy] VFX lifetime #{n}: deferred creating a {owner} the game did not create (at most {MaxRebuildsPerWindow} tries per {RebuildWindowMs / 1000} s per VFX).");
        return false;
    }

    /// <summary>時間窗已過：丟掉這份額度與留著它等價（下一次本來就會重新給滿）。</summary>
    internal static bool WindowExpired(in RebuildBudget budget, long nowMs) => nowMs - budget.WindowStart >= RebuildWindowMs;

    private static bool Spend(ref RebuildBudget budget, long nowMs)
    {
        if (budget.Count == 0 || WindowExpired(budget, nowMs))
        {
            budget.WindowStart = nowMs;
            budget.Count = 0;
        }
        if (budget.Count >= MaxRebuildsPerWindow) return false;
        budget.Count++;
        return true;
    }

    internal static void TerritoryChanged(ushort territory)
    {
        Volatile.Write(ref lastTerritoryTick, Environment.TickCount64);
        var holding = Interlocked.Read(ref held);
        if (holding <= 0) return;
        if (holding > Volatile.Read(ref maxHeldAtTerritory)) Volatile.Write(ref maxHeldAtTerritory, holding);
        var n = Interlocked.Increment(ref territoryWhileHeld);
        if (IsMilestone(n))
            Write($"[Pictomancy] VFX lifetime #{n}: territory changed to {territory} while holding {holding} VFX.");
    }

    /// <summary>卸載時呼叫；從沒追蹤過任何 VfxData、也從沒建立失敗過的插件不寫。</summary>
    internal static void WriteSummary()
    {
        var c = Snapshot();
        if (c.Tracked == 0 && c.CreateFailed == 0) return;
        Write($"[Pictomancy] VFX lifetime summary: tracked {c.Tracked}, gone at creation {c.GoneAtCreation}, gone {c.Gone}, replaced {c.Replaced}, " +
            $"skipped uses of forgotten VfxData {c.Skipped}, deferred rebuilds {c.RebuildsDeferred}, " +
            $"failed creations {c.CreateFailed} (+{c.CreateRetriesDeferred} deferred retries), " +
            $"territory changes while holding VFX {c.TerritoryWhileHeld} (max {c.MaxHeldAtTerritory}), still held {c.Held}.");
    }

    internal static Counters Snapshot() => new(
        Interlocked.Read(ref tracked), Interlocked.Read(ref goneAtCreation), Interlocked.Read(ref gone), Interlocked.Read(ref replaced),
        Interlocked.Read(ref skipped), Interlocked.Read(ref rebuildsDeferred), Interlocked.Read(ref createFailed),
        Interlocked.Read(ref createRetriesDeferred), Interlocked.Read(ref territoryWhileHeld),
        Interlocked.Read(ref maxHeldAtTerritory), Interlocked.Read(ref held));

    // 離線探針用：插件本身從不呼叫。
    internal static void Reset()
    {
        tracked = goneAtCreation = gone = replaced = skipped = rebuildsDeferred = createFailed = createRetriesDeferred = 0;
        territoryWhileHeld = maxHeldAtTerritory = held = 0;
        lastTerritoryTick = 0;
    }

    internal static bool IsMilestone(long n)
    {
        if (n <= 0) return false;
        while (n % 10 == 0) n /= 10;
        return n == 1;
    }

    private static string SinceTerritory()
    {
        var last = Volatile.Read(ref lastTerritoryTick);
        return last == 0 ? "no territory change seen yet" : $"{(Environment.TickCount64 - last) / 1000.0:0.0}s after the last territory change";
    }

    private static void Write(string message) => Sink?.Invoke(message);
}
