using TMPro;
using UnityEngine;
using WorldSim;

// Plain simulation system (not a MonoBehaviour). WorldBootstrap creates it and adds it to the TickEngine.
public class WorldClock : ITickSystem
{
    readonly TMP_Text label;
    readonly float referenceTicksPerSecond; // ticks per in-game second
    int totalSeconds; // 0 .. 86399

    public WorldClock(TMP_Text label, float referenceTicksPerSecond)
    {
        this.label = label != null ? label : GameObject.Find("WorldClock")?.GetComponent<TMP_Text>();
        this.referenceTicksPerSecond = referenceTicksPerSecond;
    }

    public string TimeString =>
        $"{totalSeconds / 3600:00}:{(totalSeconds / 60) % 60:00}:{totalSeconds % 60:00}";

    public void Tick(WorldGrid grid, int tickNumber)
    {
        totalSeconds = (int)(tickNumber / referenceTicksPerSecond) % 86400; // 24 * 60 * 60
        if (label != null) label.text = TimeString;
    }
}
