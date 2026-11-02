using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WorldSim;

// Pops up a menu when the "Evolution" button on the canvas is clicked, and lets the player spend DNA segments
// to move the plant's four traits up or down one tier at a time (see EvolutionModel in PlantModel.cs).
// Attaches itself whenever a scene loads (same idea as TooltipSystem), so nothing needs wiring up in the editor.
// Click the Close button, click the dimmed background, or press Esc to close it.
public class EvolutionMenu : MonoBehaviour
{
    const string ButtonName = "Evolution"; // exact name of the button on the canvas

    [SerializeField] Vector2 panelSize = new Vector2(900f, 680f);
    [SerializeField] float padding = 32f;
    [SerializeField] float titleSize = 48f;
    [SerializeField] float bodySize = 28f;

    // name and hint for each attribute, in PlantAttribute order
    static readonly string[] AttributeNames = { "Canopy", "Transpiration", "Pigmentation", "Spread" };
    static readonly string[] AttributeHints =
    {
        "More growth and DNA, but uses more water.",
        "Cools tiles, but loses water. Raise if hot.",
        "Cools the ground, dims light. Raise if hot.",
        "Spreads faster, grows 6% slower per tier.",
    };

    GameObject root; // overlay + panel together; toggled on and off
    PlantModel model; // found lazily: WorldBootstrap creates it in Start

    TextMeshProUGUI statusText, statsText;
    readonly TextMeshProUGUI[] tierTexts = new TextMeshProUGUI[4];
    readonly Button[] downButtons = new Button[4];
    readonly Button[] upButtons = new Button[4];
    readonly TextMeshProUGUI[] downLabels = new TextMeshProUGUI[4];
    readonly TextMeshProUGUI[] upLabels = new TextMeshProUGUI[4];

    public RectTransform Content { get; private set; }

    public bool IsOpen => root != null && root.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; // guards against double subscription when domain reload is off
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryAttach(); // the first scene has already loaded by now
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryAttach();

    // Finds the Evolution button and gives it a menu. Does nothing in scenes that don't have one.
    static void TryAttach()
    {
        if (FindFirstObjectByType<EvolutionMenu>() != null) return; // already attached

        foreach (Button b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b.name != ButtonName) continue;

            Canvas canvas = b.GetComponentInParent<Canvas>();
            if (canvas == null) continue;

            new GameObject("EvolutionMenu").AddComponent<EvolutionMenu>().Setup(b, canvas.rootCanvas);
            return;
        }
    }

    void Setup(Button openButton, Canvas canvas)
    {
        BuildMenu(canvas);
        openButton.onClick.AddListener(Toggle);
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (root == null)
            return;

        root.SetActive(true);

        // Bring the menu to the front of its own Canvas.
        root.transform.SetAsLastSibling();

        Canvas.ForceUpdateCanvases();
    }

    public void Close() => root.SetActive(false);

    void Update()
    {
        if (!IsOpen) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        Refresh(); // DNA and stats change every slow tick, so keep the numbers live
    }

    // Moves one attribute one tier up (+1) or down (-1) if the player has enough segments.
    void Move(int attributeIndex, int direction)
    {
        if (!FindModel()) return;
        var attribute = (PlantAttribute)attributeIndex;
        model.Evolution.TrySetTier(attribute, model.Evolution.GetTier(attribute) + direction);
        Refresh();
    }

    bool FindModel()
    {
        if (model != null) return true;
        var boot = FindFirstObjectByType<WorldBootstrap>();
        if (boot != null) model = boot.Model;
        return model != null;
    }

    void Refresh()
    {
        if (!FindModel())
        {
            statusText.text = "Waiting for the simulation to start...";
            statsText.text = "";
            SetAllButtons(false);
            return;
        }

        EvolutionModel evo = model.Evolution;
        statusText.text = evo.Segments >= EvolutionModel.MaxSegments
            ? $"Segments  <b>{evo.Segments} / {EvolutionModel.MaxSegments}</b> (full)   Costs in brackets are segments"
            : $"Segments  <b>{evo.Segments} / {EvolutionModel.MaxSegments}</b>   DNA for next  {evo.Dna:0.0} / {evo.NextSegmentCost:0.0}   Costs in brackets are segments";

        PlantStats s = model.Stats;
        statsText.text = s.Occupied > 0
            ? $"Coverage {s.Coverage * 100f:0}%   Mean temp {s.MeanTemp:0.0} C   Suitability {s.MeanSuitability:0.00}   CO2 {s.MeanCo2:0} ppm"
            : "No plants yet. Close this menu and click a tile to plant.";

        for (int i = 0; i < 4; i++)
        {
            var attribute = (PlantAttribute)i;
            int tier = evo.GetTier(attribute);
            tierTexts[i].text = $"Tier {tier} / {TierTable.MaxTier}";

            bool canDown = evo.CanMove(attribute, tier - 1);
            bool canUp = evo.CanMove(attribute, tier + 1);
            downButtons[i].interactable = canDown;
            upButtons[i].interactable = canUp;
            downLabels[i].text = tier > 0 ? $"-  ({evo.CostToMoveOnto(tier - 1)})" : "-";
            upLabels[i].text = tier < TierTable.MaxTier ? $"+  ({evo.CostToMoveOnto(tier + 1)})" : "+";
        }
    }

    void SetAllButtons(bool interactable)
    {
        for (int i = 0; i < 4; i++)
        {
            downButtons[i].interactable = interactable;
            upButtons[i].interactable = interactable;
        }
    }

    // Builds: root (full screen) > Backdrop (dim, click to close) + Panel (title, body, trait rows, close button).
    // Backdrop and Panel are siblings so clicking the panel doesn't bubble up and close the menu.
    void BuildMenu(Canvas canvas)
    {
        // Create a full-screen root with its own Canvas and raycaster.
        root = new GameObject(
            "EvolutionMenuRoot",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster)
        );

        root.transform.SetParent(canvas.transform, false);
        Stretch((RectTransform)root.transform);

        // Give the menu its own sorting priority.
        Canvas menuCanvas = root.GetComponent<Canvas>();
        menuCanvas.overrideSorting = true;
        menuCanvas.sortingLayerID = canvas.sortingLayerID;
        menuCanvas.sortingOrder = 100;

        // Dimmed backdrop. Also blocks clicks from reaching the HUD and map underneath.
        var backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
        backdropGo.transform.SetParent(root.transform, false);
        Stretch((RectTransform)backdropGo.transform);
        var backdropImage = backdropGo.GetComponent<Image>();
        backdropImage.color = new Color(0f, 0f, 0f, 0.55f);
        var backdropButton = backdropGo.GetComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.targetGraphic = backdropImage;
        backdropButton.onClick.AddListener(Close);

        // Panel
        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(root.transform, false);
        var panel = (RectTransform)panelGo.transform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = panelSize;
        panelGo.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.14f, 0.98f);

        // Heading and description (description matches the Evolution tooltip text)
        MakeText(panel, "Title", "Evolution", titleSize, FontStyles.Bold, TextAlignmentOptions.Center,
            top: padding, height: titleSize * 1.4f, color: Color.white);
        MakeText(panel, "Body", "Change your plant's traits using DNA. Different conditions need different traits.",
            bodySize, FontStyles.Normal, TextAlignmentOptions.Center,
            top: padding + titleSize * 1.4f + 8f, height: bodySize * 2.8f, color: Color.white);

        // Content area for trait controls
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(panel, false);
        Content = (RectTransform)contentGo.transform;
        Content.anchorMin = Vector2.zero;
        Content.anchorMax = Vector2.one;
        float contentTop = padding + titleSize * 1.4f + 8f + bodySize * 2.8f + 16f;
        float contentBottom = padding + 70f + 16f; // leave room for the close button
        Content.offsetMin = new Vector2(padding, contentBottom);
        Content.offsetMax = new Vector2(-padding, -contentTop);

        BuildTraitRows();

        // Close button along the bottom
        var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        closeGo.transform.SetParent(panel, false);
        var closeRect = (RectTransform)closeGo.transform;
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0f);
        closeRect.pivot = new Vector2(0.5f, 0f);
        closeRect.sizeDelta = new Vector2(220f, 70f);
        closeRect.anchoredPosition = new Vector2(0f, padding);
        var closeImage = closeGo.GetComponent<Image>();
        closeImage.color = new Color(0.20f, 0.45f, 0.30f, 1f);
        var closeButton = closeGo.GetComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.onClick.AddListener(Close);

        TextMeshProUGUI closeLabel = MakeText(closeRect, "Label", "Close", 30f, FontStyles.Bold,
            TextAlignmentOptions.Center, top: 0f, height: 70f, color: Color.white);
        Stretch(closeLabel.rectTransform);

        root.SetActive(false); // start hidden
    }

    // Two status lines on top, then one row per attribute:  [name + hint]   [-]  Tier n / 5  [+]
    void BuildTraitRows()
    {
        const float lineHeight = 34f, rowHeight = 66f, rowsTop = 76f;
        const float buttonWidth = 100f, buttonHeight = 50f, tierWidth = 170f, gap = 8f;
        float contentWidth = Content.rect.width > 0f ? Content.rect.width : panelSize.x - padding * 2f;

        statusText = MakeContentText("Status", "", 22f, FontStyles.Normal, TextAlignmentOptions.Left,
            0f, 0f, contentWidth, lineHeight, Color.white);
        statsText = MakeContentText("Stats", "", 22f, FontStyles.Normal, TextAlignmentOptions.Left,
            0f, lineHeight, contentWidth, lineHeight, new Color(1f, 1f, 1f, 0.6f));

        float textWidth = contentWidth - (buttonWidth * 2f + tierWidth + gap * 2f) - 16f;
        var grey = new Color(1f, 1f, 1f, 0.6f);

        for (int i = 0; i < 4; i++)
        {
            int index = i; // captured by the click handlers
            float y = rowsTop + i * rowHeight;

            MakeContentText($"Name{i}", AttributeNames[i], 28f, FontStyles.Bold, TextAlignmentOptions.Left,
                0f, y, textWidth, 30f, Color.white);
            MakeContentText($"Hint{i}", AttributeHints[i], 18f, FontStyles.Normal, TextAlignmentOptions.Left,
                0f, y + 30f, textWidth, 28f, grey);

            // right-aligned group, laid out from the right edge: [+] then tier text then [-]
            upButtons[i] = MakeContentButton($"Up{i}", "+", -0f, y + 8f, buttonWidth, buttonHeight,
                () => Move(index, +1), out upLabels[i]);
            tierTexts[i] = MakeContentText($"Tier{i}", "", 26f, FontStyles.Normal, TextAlignmentOptions.Center,
                -(buttonWidth + gap), y + 8f, tierWidth, buttonHeight, Color.white, anchorRight: true);
            downButtons[i] = MakeContentButton($"Down{i}", "-", -(buttonWidth + gap + tierWidth + gap), y + 8f,
                buttonWidth, buttonHeight, () => Move(index, -1), out downLabels[i]);
        }
    }

    // Text placed inside Content, 'x' and 'top' measured from its top-left (or top-right when anchorRight).
    TextMeshProUGUI MakeContentText(string objectName, string text, float size, FontStyles style,
        TextAlignmentOptions align, float x, float top, float width, float height, Color color, bool anchorRight = false)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(Content, false);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;

        RectTransform r = t.rectTransform;
        float corner = anchorRight ? 1f : 0f;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(corner, 1f);
        r.sizeDelta = new Vector2(width, height);
        r.anchoredPosition = new Vector2(x, -top);
        return t;
    }

    // Button anchored to the top-right of Content; 'x' is measured leftward from the right edge (use negative values).
    Button MakeContentButton(string objectName, string label, float x, float top, float width, float height,
        UnityAction onClick, out TextMeshProUGUI labelText)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(Content, false);

        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 1f);
        r.sizeDelta = new Vector2(width, height);
        r.anchoredPosition = new Vector2(x, -top);

        var image = go.GetComponent<Image>();
        image.color = new Color(0.20f, 0.45f, 0.30f, 1f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        labelText = MakeText(r, "Label", label, 26f, FontStyles.Bold, TextAlignmentOptions.Center,
            top: 0f, height: height, color: Color.white);
        Stretch(labelText.rectTransform);
        return button;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    // Text stretched across the width of its parent, placed 'top' pixels down from the parent's top edge.
    TextMeshProUGUI MakeText(Transform parent, string objectName, string text, float size, FontStyles style,
        TextAlignmentOptions align, float top, float height, Color color)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;

        RectTransform r = t.rectTransform;
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.offsetMin = new Vector2(padding, -(top + height));
        r.offsetMax = new Vector2(-padding, -top);
        return t;
    }
}