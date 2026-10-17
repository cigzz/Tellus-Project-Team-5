using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class TooltipSystem : MonoBehaviour
{
    // container holding 3 strings: canvas UI name, bolded header, and underneath explanation text
    struct Entry
    {
        public string objectName, title, body;
        public Entry(string objectName, string title, string body)
        {
            this.objectName = objectName;
            this.title = title;
            this.body = body;
        }
    }

    // EDIT TOOLTIP TEXT HERE 
    // First entry is exact canvas name, 2nd is tooltip header, 3rd is description
    static readonly Entry[] Entries =
    {
        new Entry("CoverageBar", "Coverage",
            "The share of the planet your plants cover. The color shows how healthy their world is: red is danger, yellow is safe for now, green is thriving."),
        new Entry("Coverage%", "Coverage",
            "The share of the planet your plants cover. The color shows how healthy their world is: red is danger, yellow is safe for now, green is thriving."),
        new Entry("AvgTemperature", "Average Temperature",
            "The average temperature across the whole planet. Your plants grow best in a certain range, and too hot or too cold slows them down."),
        new Entry("AvgHumidity", "Average Humidity",
            "How much water vapor is in the air. Plants release water into the air, so more plants usually means more humidity."),
        new Entry("InspectBar", "Tile Inspector",
            "Hover over a tile to see its conditions. Every spot on the planet is different, so check where your plants are struggling."),
        new Entry("TileInspectIcon", "Tile Inspector",
            "Hover over a tile to see its conditions. Every spot on the planet is different, so check where your plants are struggling."),
        new Entry("Filters", "Map Filters",
            "Switch the map view to see temperature, water, or other conditions. Use them to spot problems before your plants do."),
        new Entry("Tick Controls", "Tick Speed",
            "Controls how fast time passes. Slow down to watch what is changing, speed up to see long-term results."),
        new Entry("Tick scroll bar", "Tick Speed",
            "Controls how fast time passes. Slow down to watch what is changing, speed up to see long-term results."),
        new Entry("Evolution", "Evolution",
            "Change your plant's traits using DNA. Different conditions need different traits."),
        new Entry("Options", "Options",
            "Save your game, adjust settings, or exit."),
        new Entry("Button", "Play / Pause",
            "Pause or resume time. While paused, the planet stops changing, so it is a good time to think."),    
    };

    public static TooltipSystem Instance { get; private set; }

    // EDIT PANEL AND FONT STUFF HERE
    [SerializeField] float showDelay = 1.5f;   // seconds of hovering before the tooltip appears
    [SerializeField] float panelWidth = 460f; // tooltip box width
    [SerializeField] float padding = 16f; // padding between box edge and text
    [SerializeField] float titleSize = 30f; // header size
    [SerializeField] float bodySize = 24f; // description size

    Canvas canvas;
    RectTransform canvasRect, panel;
    CanvasGroup group;
    TextMeshProUGUI label;

    string pendingTitle, pendingBody;
    bool hovering, shown;
    float hoverTime;

    // Applies to a scene automatically
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("TooltipSystem").AddComponent<TooltipSystem>();
    }

    // Register as a shared instance
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Checks for canvases in the scene
    void Start()
    {
        Canvas found = FindFirstObjectByType<Canvas>();
        if (found == null)
        {
            enabled = false;
            return;
        }

        // Grabs canvas  size and position
        canvas = found.rootCanvas;
        canvasRect = canvas.GetComponent<RectTransform>();

        EnsureEventSystem();

        // Hover detection for name dobjects and builds panel for tooltip
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera == null)
            canvas.worldCamera = Camera.main;

        AttachTargets();
        BuildPanel();
    }

    // Used to detect the mouse
    void EnsureEventSystem()
    {
        // Checks for an existing eventsystem and makes one if not
        EventSystem es = FindFirstObjectByType<EventSystem>();
        if (es == null)
            es = new GameObject("EventSystem").AddComponent<EventSystem>();

        var oldModule = es.GetComponent<StandaloneInputModule>();
        if (oldModule != null) Destroy(oldModule);

        if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            var module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    // Iterates through objects on cavans and gives a tooltiptarget to the component
    void AttachTargets()
    {
        // Converts entires to a dict
        var lookup = new System.Collections.Generic.Dictionary<string, Entry>();
        foreach (Entry e in Entries) lookup[e.objectName] = e;

        var found = new System.Collections.Generic.HashSet<string>();
        foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true))
        {
            // Skips objects that dont have a name in the table
            if (!lookup.TryGetValue(t.name, out Entry entry)) continue;

            // Reuse existing ones and assigns text
            TooltipTarget target = t.GetComponent<TooltipTarget>();
            if (target == null) target = t.gameObject.AddComponent<TooltipTarget>();
            target.title = entry.title;
            target.body = entry.body;
            found.Add(t.name);
        }

        // Name in the table matched nothing :(
        foreach (Entry e in Entries)
            if (!found.Contains(e.objectName))
                Debug.LogWarning($"[Tooltips] No UI object named '{e.objectName}' found.");
    }

    // Tooltip box formatting
    void BuildPanel()
    {
        var go = new GameObject("Tooltip", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        go.transform.SetParent(canvas.transform, false);

        // Cursor in top left corner
        panel = (RectTransform)go.transform;
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0f, 1f);

        // Transparent and uninteractable
        group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        // Background
        var bg = go.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.10f, 0.14f, 0.95f);
        bg.raycastTarget = false;

        // Text 
        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(panel, false);
        label = textGo.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.richText = true;
        label.color = Color.white;
        label.fontSize = bodySize;
        label.textWrappingMode = TextWrappingModes.Normal;

        // Size
        RectTransform lr = label.rectTransform;
        lr.anchorMin = Vector2.zero;
        lr.anchorMax = Vector2.one;
        lr.offsetMin = new Vector2(padding, padding);
        lr.offsetMax = new Vector2(-padding, -padding);
    }

    //Tooltip target stuff
    public void Show(string title, string body)
    {
        if (panel == null) return;
        pendingTitle = title;
        pendingBody = body;
        hovering = true;
        shown = false;
        hoverTime = 0f;
    }

    public void Hide()
    {
        hovering = false;
        shown = false;
        if (group != null) group.alpha = 0f;
    }

    // Detects how long hover and makes tooltip follow the mouse
    void Update()
    {
        if (!hovering || panel == null) return;

        hoverTime += Time.unscaledDeltaTime; // works even if the game is paused
        if (!shown && hoverTime >= showDelay)
        {
            Display();
            shown = true;
        }
        if (shown) Follow();
    }

    // Shows text in box
    void Display()
    {
        label.text = $"<b><size={titleSize}>{pendingTitle}</size></b>\n{pendingBody}";
        Vector2 pref = label.GetPreferredValues(label.text, panelWidth - padding * 2f, 0f);
        panel.sizeDelta = new Vector2(panelWidth, pref.y + padding * 2f);
        panel.SetAsLastSibling(); // draw on top of everything
        group.alpha = 1f;
    }

    // Takes mouse position and gives it a canvas coord
    void Follow()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, mouse.position.ReadValue(), cam, out Vector2 local))
            return;

        Rect r = canvasRect.rect;
        Vector2 size = panel.rect.size;
        Vector2 cursor = local - r.center;
        const float gap = 24f;

        Vector2 pos = new Vector2(cursor.x + gap, cursor.y - gap);

        // Flips to other side if it runs off the edge
        if (pos.x + size.x > r.width * 0.5f) pos.x = cursor.x - gap - size.x;
        if (pos.y - size.y < -r.height * 0.5f) pos.y = cursor.y + gap + size.y;

        //Forces box to stay inside the canvas
        pos.x = Mathf.Clamp(pos.x, -r.width * 0.5f, r.width * 0.5f - size.x);
        pos.y = Mathf.Clamp(pos.y, -r.height * 0.5f + size.y, r.height * 0.5f);

        panel.anchoredPosition = pos;
    }
}
