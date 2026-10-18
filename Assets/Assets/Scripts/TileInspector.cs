using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace WorldSim
{
    public enum TileInspectorMode { Standard, Debug } // defines inspection modes, debug shows all field data and standard has a defined list for the player
    public class TileInspector : MonoBehaviour
    {
        [Header("Timing")] // inspector heading
        public float HoverDelay = 1.5f;       // seconds of hovering before the tooltip appears
        public float RefreshInterval = 0.25f; // how often shown data is re-read while hovering

        [Header("Mode")] // inspector heading
        public TileInspectorMode Mode = TileInspectorMode.Standard; // current inspection mode
        [SerializeField] string[] standardFields = { "water", "plantGrowth", "temperature" }; // fields shown in Standard mode

        [Header("References")] // inspector heading
        [SerializeField] TickEngine tickEngine; // reference to main simulation tick engine
        [SerializeField] Tilemap tilemap;   // only used to convert a mouse position into a grid cell
        [SerializeField] Text displayText;  // optional: assign a UI Text to auto-display the tooltip
        public event Action<string> OnShowTooltip; // event triggered when tooltip shown
        public event Action OnHideTooltip; // event triggered when tooltip hidden
        Vector2Int hoveredCell; // holds coords for currently hovered cell
        bool isHoveringACell;
        bool tooltipVisible;
        float hoverTimer; // hover delay interval
        float refreshTimer; // data refresh interval
        void Update()
        {
            if (tickEngine.Grid == null) return;

            Vector2Int? cellUnderMouse = GetCellUnderMouse();

            if (cellUnderMouse == null)
            {
                ResetHover();
                return;
            }

            if (!isHoveringACell || cellUnderMouse.Value != hoveredCell)
            {
                // after moving, restart the hover timer and hide until it's held again
                hoveredCell = cellUnderMouse.Value;
                isHoveringACell = true;
                hoverTimer = 0f;
                refreshTimer = 0f;
                Hide();
                return;
            }

            hoverTimer += Time.deltaTime;
            if (hoverTimer < HoverDelay) return;

            refreshTimer += Time.deltaTime;
            if (tooltipVisible && refreshTimer < RefreshInterval) return; // not time to re-read data yet
            refreshTimer = 0f;

            Show(BuildText(tickEngine.Grid, hoveredCell.x, hoveredCell.y));
        }

        Vector2Int? GetCellUnderMouse()
        {
            Vector3 worldPoint = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector3Int cell = tilemap.WorldToCell(worldPoint);

            WorldGrid grid = tickEngine.Grid;
            if (cell.x < 0 || cell.x >= grid.Width || cell.y < 0 || cell.y >= grid.Height)
                return null;

            return new Vector2Int(cell.x, cell.y);
        }

        string BuildText(WorldGrid grid, int x, int y)
        {
            int index = grid.Index(x, y);
            var sb = new StringBuilder();
            sb.AppendLine($"Tile ({x}, {y})");

            IEnumerable<string> fieldsToShow = Mode == TileInspectorMode.Debug ? grid.FieldNames : standardFields; 

            foreach (string fieldName in fieldsToShow)
            {
                if (!grid.HasField(fieldName)) continue; // not created yet (e.g. no plant ever placed here)
                float value = grid.GetField(fieldName)[index];
                sb.AppendLine($"{fieldName}: {value:0.00}");
            }

            return sb.ToString();
        }

        void Show(string text) // show tooltip
        {
            tooltipVisible = true;
            if (displayText != null)
            {
                displayText.gameObject.SetActive(true);
                displayText.text = text;
            }
            OnShowTooltip?.Invoke(text);
        }

        void Hide() // hide tooltip
        {
            if (!tooltipVisible) return;
            tooltipVisible = false;
            if (displayText != null) displayText.gameObject.SetActive(false);
            OnHideTooltip?.Invoke();
        }

        void ResetHover()
        {
            isHoveringACell = false;
            hoverTimer = 0f;
            Hide();
        }
    }
}
