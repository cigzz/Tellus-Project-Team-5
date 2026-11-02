using UnityEngine;
using WorldSim;

public class ClimateSimulationTestHUD : MonoBehaviour
{
    [SerializeField] TickEngine tickEngine;

    void OnGUI()
    {
        if (tickEngine == null) return;

        WorldGrid grid = tickEngine.Grid;
        GUI.Box(new Rect(12f, 12f, 350f, 205f), "Climate Simulation Test");
        if (grid == null)
        {
            GUI.Label(new Rect(24f, 42f, 320f, 28f), "Starting simulation...");
            return;
        }

        float[] temperature = grid.GetField("temperature");
        float[] humidity = grid.GetField("humidity");
        float[] water = grid.GetField("water");
        float[] soil = grid.GetField("soilMoisture");
        float meanTemperature = 0f;
        float meanHumidity = 0f;
        float totalWater = 0f;
        float meanSoil = 0f;
        for (int i = 0; i < grid.CellCount; i++)
        {
            meanTemperature += temperature[i];
            meanHumidity += humidity[i];
            totalWater += water[i];
            meanSoil += soil[i];
        }

        float count = grid.CellCount;
        GUI.Label(new Rect(24f, 42f, 320f, 24f), $"Tick: {tickEngine.TickNumber}   {(tickEngine.Paused ? "Paused" : "Running")}");
        GUI.Label(new Rect(24f, 67f, 320f, 24f), $"Mean temperature: {meanTemperature / count:0.0} °C");
        GUI.Label(new Rect(24f, 91f, 320f, 24f), $"Mean relative humidity: {meanHumidity / count:P0}");
        GUI.Label(new Rect(24f, 115f, 320f, 24f), $"Surface water: {totalWater:0.0} mm total");
        GUI.Label(new Rect(24f, 139f, 320f, 24f), $"Mean soil moisture: {meanSoil / count:0.0} mm");

        if (GUI.Button(new Rect(24f, 169f, 145f, 28f), tickEngine.Paused ? "Resume" : "Pause"))
            tickEngine.Paused = !tickEngine.Paused;
        if (GUI.Button(new Rect(181f, 169f, 165f, 28f), "Add water pulse"))
        {
            int center = grid.Index(grid.Width / 2, grid.Height / 2);
            grid.GetField("water")[center] += 50f;
        }
    }
}
