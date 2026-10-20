using UnityEngine;
using WorldSim;

public class WorldClock : MonoBehaviour, ITickSystem
{
    [SerializeField] int ticksPerSecond = 8;
    int tickCounter;
    int totalSeconds; // 0 .. 86399
    public string TimeString =>
        $"{totalSeconds / 3600:00}:{(totalSeconds / 60) % 60:00}:{totalSeconds % 60:00}";

    public void Tick(WorldGrid grid, int tickNumber)
    {
        if (++tickCounter < ticksPerSecond) return;
        tickCounter = 0;
        totalSeconds++;
        if (totalSeconds >= 86400) // 24 * 60 * 60
            totalSeconds = 0;
    }
}