using TMPro;
using UnityEngine;
using WorldSim;

public class WorldClock : MonoBehaviour, ITickSystem
{
    readonly PlantModel model;
    public TickEngine tickEngine;
    public TMP_Text worldClockTMP;
    public string worldClockParentGOName = "WorldClock";
    public float referenceTicksPerSecondValue;
    float ticksPerSecond;
    int tickCounter;
    int totalSeconds; // 0 .. 86399
    public WorldClock(PlantModel model) { this.model = model; }
    public string TimeString =>
        $"{totalSeconds / 3600:00}:{(totalSeconds / 60) % 60:00}:{totalSeconds % 60:00}";

    public void Tick(WorldGrid grid, int tickNumber)
    {
        if (tickEngine == null)
            tickEngine = FindAnyObjectByType<TickEngine>();
        if (worldClockTMP == null)
            worldClockTMP = (GameObject.Find(worldClockParentGOName)).GetComponent<TextMeshProUGUI>();
        // ticksPerSecond = tickEngine.GetTickRate();
        if (++tickCounter < referenceTicksPerSecondValue)
            return;
        tickCounter = 0;
        totalSeconds++;
        if (totalSeconds >= 86400) // 24 * 60 * 60
            totalSeconds = 0;
        worldClockTMP.text = this.TimeString;
        // Debug.Log($"Tick {tickNumber}, Total {totalSeconds}");
    }
}