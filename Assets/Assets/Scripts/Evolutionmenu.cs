
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Displays an Evolution menu when the Evolution button is clicked.
// Automatically attaches itself when a scene loads.
// The menu renders above other UI using its own high-priority Canvas.
// Click Close, click the dimmed background, or press Escape to close it.
public class EvolutionMenu : MonoBehaviour
{
    const string ButtonName = "Evolution";

    [SerializeField] Vector2 panelSize = new Vector2(900f, 600f);
    [SerializeField] float padding = 32f;
    [SerializeField] float titleSize = 48f;
    [SerializeField] float bodySize = 28f;

    GameObject root;

    // Empty area where trait and DNA controls can be added later.
    public RectTransform Content { get; private set; }

    public bool IsOpen => root != null && root.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        TryAttach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryAttach();
    }

    // Finds the Evolution button and attaches this menu automatically.
    static void TryAttach()
    {
        if (FindFirstObjectByType<EvolutionMenu>() != null)
            return;

        Button[] buttons = FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Button b in buttons)
        {
            if (b.name != ButtonName)
                continue;

            Canvas canvas = b.GetComponentInParent<Canvas>();

            if (canvas == null)
                continue;

            EvolutionMenu menu =
                new GameObject("EvolutionMenu")
                .AddComponent<EvolutionMenu>();

            menu.Setup(b, canvas.rootCanvas);
            return;
        }
    }

    void Setup(Button openButton, Canvas canvas)
    {
        BuildMenu(canvas);

        // Prevent duplicate listeners if Setup is called again.
        openButton.onClick.RemoveListener(Toggle);
        openButton.onClick.AddListener(Toggle);
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
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

    public void Close()
    {
        if (root != null)
            root.SetActive(false);
    }

    void Update()
    {
        if (IsOpen &&
            Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }

    void BuildMenu(Canvas parentCanvas)
    {
        // Create a full-screen root with its own Canvas.
        root = new GameObject(
            "EvolutionMenuRoot",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster)
        );

        root.transform.SetParent(parentCanvas.transform, false);

        RectTransform rootRect = (RectTransform)root.transform;
        Stretch(rootRect);

        // IMPORTANT: Override the parent Canvas sorting order.
        Canvas menuCanvas = root.GetComponent<Canvas>();
        menuCanvas.overrideSorting = true;
        menuCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        menuCanvas.sortingOrder = 100;

        // Dimmed backdrop. Clicking it closes the menu.
        GameObject backdropGo = new GameObject(
            "Backdrop",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );

        backdropGo.transform.SetParent(root.transform, false);

        Stretch((RectTransform)backdropGo.transform);

        Image backdropImage = backdropGo.GetComponent<Image>();
        backdropImage.color = new Color(0f, 0f, 0f, 0.55f);
        backdropImage.raycastTarget = true;

        Button backdropButton = backdropGo.GetComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.targetGraphic = backdropImage;
        backdropButton.onClick.AddListener(Close);

        // Main panel.
        GameObject panelGo = new GameObject(
            "Panel",
            typeof(RectTransform),
            typeof(Image)
        );

        panelGo.transform.SetParent(root.transform, false);

        RectTransform panel = (RectTransform)panelGo.transform;

        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = panelSize;

        Image panelImage = panelGo.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.10f, 0.14f, 0.98f);
        panelImage.raycastTarget = true;

        // Title.
        MakeText(
            panel,
            "Title",
            "Evolution",
            titleSize,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            padding,
            titleSize * 1.4f,
            Color.white
        );

        // Description.
        MakeText(
            panel,
            "Body",
            "Change your plant's traits using DNA. Different conditions need different traits.",
            bodySize,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            padding + titleSize * 1.4f + 8f,
            bodySize * 2.8f,
            Color.white
        );

        // Content area for future trait controls.
        GameObject contentGo = new GameObject(
            "Content",
            typeof(RectTransform)
        );

        contentGo.transform.SetParent(panel, false);

        Content = (RectTransform)contentGo.transform;

        Content.anchorMin = Vector2.zero;
        Content.anchorMax = Vector2.one;

        float contentTop =
            padding + titleSize * 1.4f + 8f + bodySize * 2.8f + 16f;

        float contentBottom = padding + 70f + 16f;

        Content.offsetMin = new Vector2(padding, contentBottom);
        Content.offsetMax = new Vector2(-padding, -contentTop);

        // Placeholder text.
        MakeText(
            Content,
            "Placeholder",
            "Traits coming soon.",
            bodySize,
            FontStyles.Italic,
            TextAlignmentOptions.Center,
            0f,
            bodySize * 1.5f,
            new Color(1f, 1f, 1f, 0.5f)
        );

        // Close button.
        GameObject closeGo = new GameObject(
            "CloseButton",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );

        closeGo.transform.SetParent(panel, false);

        RectTransform closeRect = (RectTransform)closeGo.transform;

        closeRect.anchorMin = new Vector2(0.5f, 0f);
        closeRect.anchorMax = new Vector2(0.5f, 0f);
        closeRect.pivot = new Vector2(0.5f, 0f);
        closeRect.sizeDelta = new Vector2(220f, 70f);
        closeRect.anchoredPosition = new Vector2(0f, padding);

        Image closeImage = closeGo.GetComponent<Image>();
        closeImage.color = new Color(0.20f, 0.45f, 0.30f, 1f);
        closeImage.raycastTarget = true;

        Button closeButton = closeGo.GetComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.onClick.AddListener(Close);

        TextMeshProUGUI closeLabel = MakeText(
            closeRect,
            "Label",
            "Close",
            30f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            0f,
            70f,
            Color.white
        );

        Stretch(closeLabel.rectTransform);

        // Start hidden.
        root.SetActive(false);
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    TextMeshProUGUI MakeText(
        Transform parent,
        string objectName,
        string text,
        float size,
        FontStyles style,
        TextAlignmentOptions align,
        float top,
        float height,
        Color color)
    {
        GameObject go = new GameObject(
            objectName,
            typeof(RectTransform)
        );

        go.transform.SetParent(parent, false);

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();

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
