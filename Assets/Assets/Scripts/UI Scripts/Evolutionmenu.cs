using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldSim;

// The Evolution menu, driven by the EvolutionMenuRoot prefab (Images/UI ASSETS/Menu's). The prefab holds the layout and
// look; this script, on the prefab's root, finds its parts by name (Bind), wires the buttons and keeps the numbers live.
// The player spends banked DNA segments to move the plant's four traits one tier at a time (rules in EvolutionModel,
// PlantModel.cs). Changing a trait locks that trait, both + and -, for upgradeCooldownTicks simulation ticks.
//
// WorldBootstrap spawns it (Spawn) under the canvas of the scene's "Evolution" button.
// Close it with the Close button, a click on the dimmed backdrop, or Esc.
//
// Parts it needs (renaming or removing one logs an error naming its path):
//   Backdrop (Button)
//   Panel (CanvasGroup)
//     Stats, CloseButton
//     CanopyCard, TranspirationCard, PigmentationCard, SpreadCard:  Accent, Up/Label, Down/Label, Signal/Tier, Signal/Bar1-7
//     DnaColumn: Count, Next, Bar/Segment1-10/Fill
public class EvolutionMenu : MonoBehaviour
{
    const string OpenButtonName = "Evolution"; // name of the button (or its wrapper) on the scene's canvas
    const float RefreshInterval = 0.1f;        // seconds between redraws of the live numbers

    // Card names in PlantAttribute order.
    static readonly string[] CardNames = { "CanopyCard", "TranspirationCard", "PigmentationCard", "SpreadCard" };

    [Header("Upgrade delay")]
    [Tooltip("Simulation ticks a trait stays locked (both + and -) after it changes. Counts TickEngine ticks, so it " +
             "pauses with the game and runs faster at higher speeds (8 ticks = 1 s at normal speed). 0 = no delay.")]
    [SerializeField, Min(0)] int upgradeCooldownTicks = 24;

    [Header("Look (lit signal bars use each card's Accent colour)")]
    [SerializeField] float popInSeconds = 0.18f;
    [SerializeField] Color barOffColor = new Color(1f, 1f, 1f, 0.10f);
    [SerializeField] Color dnaColor = new Color(0.27f, 0.62f, 1.00f, 1f);
    [SerializeField] Color dnaEarningColor = new Color(0.27f, 0.62f, 1.00f, 0.45f); // the segment being earned
    [SerializeField] Color buttonTextColor = Color.white;
    [SerializeField] Color disabledButtonTextColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] Color statLabelColor = new Color(0.66f, 0.73f, 0.82f, 1f);

    // The live controls of one trait card.
    class TraitRow
    {
        public Button Down, Up;
        public TextMeshProUGUI DownLabel, UpLabel, TierText;
        public Image Accent;
        public Image[] Bars; // one per tier above 0, shortest first
    }

    PlantModel model;
    TickEngine ticks;             // the simulation clock the upgrade delay counts
    int lockedAttribute = -1;     // trait changed last (PlantAttribute index), or -1
    int lockedUntilTick;

    RectTransform panel;
    CanvasGroup panelFade;
    Vector2 panelSize;            // the prefab's panel size, for shrink-to-fit
    float openedAt;               // Time.unscaledTime of the last Open
    float nextRefresh;            // Time.unscaledTime of the next redraw
    string statLabelHex;

    TextMeshProUGUI statsText, dnaCountText, dnaNextText;
    Button closeButton, backdropButton;
    readonly TraitRow[] rows = new TraitRow[CardNames.Length];
    Image[] dnaFills;             // one per DNA segment, bottom first
    RectTransform[] dnaFillRects;

    public bool IsOpen => gameObject.activeSelf;

    // ------------------------------------------------------------------ setup

    // Creates the menu under the canvas of the scene's Evolution button and hooks that button up.
    // Does nothing in scenes without an Evolution button.
    public static EvolutionMenu Spawn(EvolutionMenu prefab, PlantModel model, TickEngine ticks)
    {
        Button openButton = FindOpenButton();
        if (openButton == null) return null;
        if (prefab == null)
        {
            Debug.LogWarning("No Evolution Menu Prefab is assigned on WorldBootstrap, so the Evolution button does nothing.");
            return null;
        }

        Canvas canvas = openButton.GetComponentInParent<Canvas>(true).rootCanvas;
        EvolutionMenu menu = Instantiate(prefab, canvas.transform, false);
        menu.name = prefab.name;
        if (menu.TryGetComponent(out Canvas menuCanvas))
            menuCanvas.sortingLayerID = canvas.sortingLayerID; // its own canvas (sorting order 100) sorts above the HUD
        menu.Initialize(model, ticks, openButton);
        return menu;
    }

    static Button FindOpenButton()
    {
        foreach (Button b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b.name == OpenButtonName && b.GetComponentInParent<Canvas>(true) != null) return b;
        return null;
    }

    // Binds the prefab, wires its buttons and the open button, then hides the menu. Public for the edit-mode tests.
    public void Initialize(PlantModel plantModel, TickEngine tickEngine, Button openButton)
    {
        model = plantModel;
        ticks = tickEngine;
        statLabelHex = ColorUtility.ToHtmlStringRGBA(statLabelColor);

        if (!Bind())
        {
            gameObject.SetActive(false);
            return;
        }

        closeButton.onClick.AddListener(Close);
        backdropButton.onClick.AddListener(Close);
        for (int i = 0; i < rows.Length; i++)
        {
            int attribute = i; // captured by the click handlers
            rows[i].Up.onClick.AddListener(() => Move(attribute, +1));
            rows[i].Down.onClick.AddListener(() => Move(attribute, -1));
        }

        if (openButton != null)
        {
            openButton.onClick.AddListener(Toggle);
            // Dynamic_UI has a child Button that receives clicks before the parent; Dynamic_UI_V2 uses the parent directly.
            Transform child = openButton.transform.Find("Button");
            if (child != null && child.TryGetComponent(out Button childButton))
                childButton.onClick.AddListener(Toggle);
        }

        gameObject.SetActive(false); // start hidden
    }

    // Finds every part the menu drives. Logs all missing paths at once and returns false if there were any.
    // Public so the edit-mode tests can check the prefab.
    public bool Bind()
    {
        var missing = new List<string>();
        T Need<T>(string path) where T : Component
        {
            Transform t = transform.Find(path);
            T part = t != null ? t.GetComponent<T>() : null;
            if (part == null) missing.Add($"{path} ({typeof(T).Name})");
            return part;
        }

        backdropButton = Need<Button>("Backdrop");
        panel = Need<RectTransform>("Panel");
        panelFade = Need<CanvasGroup>("Panel");
        statsText = Need<TextMeshProUGUI>("Panel/Stats");
        closeButton = Need<Button>("Panel/CloseButton");

        for (int i = 0; i < rows.Length; i++)
        {
            string card = "Panel/" + CardNames[i];
            var row = new TraitRow
            {
                Accent = Need<Image>(card + "/Accent"),
                Up = Need<Button>(card + "/Up"),
                UpLabel = Need<TextMeshProUGUI>(card + "/Up/Label"),
                Down = Need<Button>(card + "/Down"),
                DownLabel = Need<TextMeshProUGUI>(card + "/Down/Label"),
                TierText = Need<TextMeshProUGUI>(card + "/Signal/Tier"),
                Bars = new Image[TierTable.MaxTier],
            };
            for (int b = 0; b < row.Bars.Length; b++)
                row.Bars[b] = Need<Image>($"{card}/Signal/Bar{b + 1}");
            rows[i] = row;
        }

        dnaCountText = Need<TextMeshProUGUI>("Panel/DnaColumn/Count");
        dnaNextText = Need<TextMeshProUGUI>("Panel/DnaColumn/Next");
        dnaFills = new Image[EvolutionModel.MaxSegments];
        dnaFillRects = new RectTransform[EvolutionModel.MaxSegments];
        for (int s = 0; s < dnaFills.Length; s++)
        {
            string fill = $"Panel/DnaColumn/Bar/Segment{s + 1}/Fill";
            dnaFills[s] = Need<Image>(fill);
            dnaFillRects[s] = Need<RectTransform>(fill);
        }

        if (missing.Count > 0)
        {
            Debug.LogError($"{name}: the Evolution menu prefab is missing {missing.Count} part(s):\n  " +
                           string.Join("\n  ", missing), this);
            return false;
        }

        panelSize = panel.sizeDelta;
        return true;
    }

    // ------------------------------------------------------------------ opening and closing

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (panel == null) return; // failed to bind

        gameObject.SetActive(true);
        transform.SetAsLastSibling(); // in front of the rest of its canvas
        openedAt = Time.unscaledTime;
        Animate();
        Refresh();
    }

    public void Close() => gameObject.SetActive(false);

    void Update() // runs only while open: the root is inactive when closed
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        Animate();
        if (Time.unscaledTime >= nextRefresh) Refresh(); // DNA, stats and the upgrade delay change every tick
    }

    // Pop-in (fade in and grow from 94%) combined with shrink-to-fit, so the panel never overflows a small screen
    // or browser window.
    void Animate()
    {
        Vector2 room = ((RectTransform)transform).rect.size - new Vector2(40f, 40f);
        float fit = Mathf.Clamp(Mathf.Min(room.x / panelSize.x, room.y / panelSize.y), 0.2f, 1f);

        float t = popInSeconds > 0f ? Mathf.Clamp01((Time.unscaledTime - openedAt) / popInSeconds) : 1f;
        float ease = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic

        panel.localScale = Vector3.one * (fit * Mathf.Lerp(0.94f, 1f, ease));
        panelFade.alpha = ease;
    }

    // ------------------------------------------------------------------ live values

    // Moves one attribute one tier up (+1) or down (-1) if it isn't locked and the player has enough segments.
    // A successful move locks that attribute for upgradeCooldownTicks.
    void Move(int attributeIndex, int direction)
    {
        var attribute = (PlantAttribute)attributeIndex;
        if (model == null || TicksLocked(attribute) > 0) return;

        if (model.Evolution.TrySetTier(attribute, model.Evolution.GetTier(attribute) + direction) && ticks != null)
        {
            lockedAttribute = attributeIndex;
            lockedUntilTick = ticks.TickNumber + upgradeCooldownTicks;
        }
        Refresh();
    }

    // Simulation ticks until this attribute can change again (0 = free now).
    int TicksLocked(PlantAttribute attribute) =>
        (int)attribute == lockedAttribute && ticks != null ? Mathf.Max(0, lockedUntilTick - ticks.TickNumber) : 0;

    void Refresh()
    {
        nextRefresh = Time.unscaledTime + RefreshInterval;
        if (model == null) return;

        EvolutionModel evo = model.Evolution;
        statsText.text = StatsLine(model.Stats);
        RefreshDnaBar(evo);
        for (int i = 0; i < rows.Length; i++)
            RefreshRow(rows[i], evo, (PlantAttribute)i);
    }

    string StatsLine(PlantStats s)
    {
        if (s.Occupied == 0) return "No plants yet. Close this menu and click a tile to plant.";

        return $"{Label("Coverage")} {s.Coverage * 100f:0}%      {Label("Mean temp")} {s.MeanTemp:0.0} C      " +
               $"{Label("Suitability")} {s.MeanSuitability:0.00}      {Label("CO2")} {s.MeanCo2:0} ppm";
    }

    string Label(string text) => $"<color=#{statLabelHex}>{text}</color>";

    // Segments fill from the bottom. Banked ones are solid; the one being earned fills partway in a paler blue.
    void RefreshDnaBar(EvolutionModel evo)
    {
        int banked = evo.Segments;
        float progress = evo.SegmentProgress;

        for (int s = 0; s < dnaFills.Length; s++)
        {
            float fill = s < banked ? 1f : s == banked ? progress : 0f;
            dnaFillRects[s].anchorMax = new Vector2(1f, fill);
            dnaFills[s].color = s < banked ? dnaColor : dnaEarningColor;
            dnaFills[s].enabled = fill > 0f;
        }

        dnaCountText.text = $"{banked} / {EvolutionModel.MaxSegments}";
        dnaNextText.text = banked >= EvolutionModel.MaxSegments ? "Bank full" : $"Next segment {progress * 100f:0}%";
    }

    void RefreshRow(TraitRow row, EvolutionModel evo, PlantAttribute attribute)
    {
        int tier = evo.GetTier(attribute);
        row.TierText.text = $"Tier {tier} / {TierTable.MaxTier}";

        // Signal bars: bar b lights once the tier passes it, so tier 0 shows none and the top tier shows them all.
        Color lit = row.Accent.color;
        for (int b = 0; b < row.Bars.Length; b++)
            row.Bars[b].color = b < tier ? lit : barOffColor;

        int locked = TicksLocked(attribute);
        SetStepButton(row.Down, row.DownLabel, "-", evo, attribute, tier - 1, "MIN", locked);
        SetStepButton(row.Up, row.UpLabel, "+", evo, attribute, tier + 1, "MAX", locked);
    }

    // A +/- button: the sign over its cost in segments, the time left while the trait is locked, or MIN / MAX at the
    // end of the tier range.
    void SetStepButton(Button button, TextMeshProUGUI label, string sign, EvolutionModel evo,
        PlantAttribute attribute, int target, string atLimit, int ticksLocked)
    {
        bool inRange = target >= 0 && target <= TierTable.MaxTier;
        string detail = !inRange ? atLimit
                      : ticksLocked > 0 ? $"wait {ticksLocked / Mathf.Max(ticks.GetTickRate(), 0.01f):0.0}s"
                      : $"cost {evo.CostToMoveOnto(target)}";

        button.interactable = ticksLocked == 0 && evo.CanMove(attribute, target);
        label.text = $"<size=34><b>{sign}</b></size>\n<size=15>{detail}</size>";
        label.color = button.interactable ? buttonTextColor : disabledButtonTextColor;
    }
}
