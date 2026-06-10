using Dalamud.Plugin;

namespace Pictomancy;

public sealed class GaolbreakHeartbeatReader(IDalamudPluginInterface pi)
{
    public const string Key = "Gaolbreak.Heartbeat.v1";

    private long lastBeat = -1;

    public bool Alive()
    {
        if (!pi.TryGetData<long[]>(Key, out var data) || data.Length < 1)
        {
            lastBeat = -1;
            return false;
        }

        long beat = data[0];
        bool alive = beat > lastBeat;
        lastBeat = beat;
        return alive;
    }
}
