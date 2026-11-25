using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WorldSim
{
    public sealed class GameplayTutorial : MonoBehaviour
    {
        // dev option for starting with/without tutorial
#if UNITY_EDITOR
        static bool dismissedInPlaySession;

        // keeps track of if dismissed previously
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPlaySession() => dismissedInPlaySession = false;
#else
        const string FinishedKey = "Tellus.GameplayTutorial.V2.Finished";
#endif

        enum Step { Hover, Inspect, Plant, Speed, Evolution, Goal }

        PlantPlacer planter;
        TileInspector inspector;
        TickEngine tickEngine;
        TickSpeedTextDriver speedControl;
        EvolutionMenu evolution;
        TMP_Text heading;
        TMP_Text progress;
        TMP_Text description;
        TMP_Text action;
        TMP_Text buttonLabel;
        float coverageGoal;
        float speedAtEntry;
        bool openedEvolution;
        Step step;

        // grabbing vars from the game start to use in tutorials
        public static void TryStart(WorldBootstrap world, float victoryCoverage)
        {
#if UNITY_EDITOR
            if (dismissedInPlaySession)
#else
            if (PlayerPrefs.GetInt(FinishedKey) != 0)
#endif
                return;

            if (!world.TryGetComponent(out PlantPlacer planter) || !planter.isActiveAndEnabled)
                return;

            var overlay = new GameObject("Gameplay Tutorial", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(overlay, world.gameObject.scene);
            var tutorial = overlay.AddComponent<GameplayTutorial>();
            tutorial.planter = planter;
            tutorial.inspector = world.GetComponent<TileInspector>();
            tutorial.tickEngine = world.GetComponent<TickEngine>();
            tutorial.speedControl = FindAnyObjectByType<TickSpeedTextDriver>();
            tutorial.coverageGoal = Mathf.Clamp01(victoryCoverage);
            tutorial.BuildPanel();
            tutorial.Show(Step.Hover);
        }

        void LateUpdate()
        {
            // Check for each action after showed tutorial and increment steps
            switch (step)
            {
                case Step.Hover:
                    if (TooltipSystem.Instance != null && TooltipSystem.Instance.IsVisible)
                        Show(Step.Inspect);
                    break;
                case Step.Inspect:
                    if (inspector == null || inspector.IsShowingDetails)
                        Show(Step.Plant);
                    break;
                case Step.Plant:
                    if (planter.HasPlanted)
                        Show(Step.Speed);
                    break;
                case Step.Speed:
                    if (speedControl == null || !Mathf.Approximately(tickEngine.GetTickRate(), speedAtEntry))
                        Show(Step.Evolution);
                    break;
                case Step.Evolution:
                    // wait til evolution menu is closed before going to next step
                    if (evolution == null || (openedEvolution && !evolution.IsOpen))
                        Show(Step.Goal);
                    else if (evolution.IsOpen)
                        openedEvolution = true;
                    break;
            }
        }

        void Show(Step next)
        {
            step = next;
            progress.text = $"{(int)next + 1}/{(int)Step.Goal + 1}";
            buttonLabel.text = next == Step.Goal ? "Done" : "Skip";
            switch (next)
            {
                case Step.Hover:
                    SetCopy("Learn from tooltips",
                        "Hover over interface objects to see what they do. Tooltips appear after a short delay and explain things like coverage, filters, and controls.",
                        "Rest your cursor on the coverage bar at the top until a tooltip appears.");
                    break;
                case Step.Inspect:
                    SetCopy("Inspect a map tile",
                        "Individual tiles can be warmer, colder, wetter, or drier than the planet average. The tile inspector shows conditions at the spot under your cursor.",
                        "Hover over a map tile until its details appear in the inspector at the top.");
                    break;
                case Step.Plant:
                    SetCopy("Plant your first seed",
                        "A plant starts from one seed and spreads on its own. Use the Temperature filter to find warmer ground.",
                        "Left-click a suitable tile near the middle of the map.");
                    break;
                case Step.Speed:
                    speedAtEntry = tickEngine.GetTickRate();
                    SetCopy("Control time",
                        "Speed up to see growth sooner or slow down to give yourself time to react. The Play / Pause button stops or resumes time while you inspect the planet.",
                        "Move the Speed slider in the top-right to speed up or slow down time.");
                    break;
                case Step.Evolution:
                    evolution = FindAnyObjectByType<EvolutionMenu>();
                    openedEvolution = evolution != null && evolution.IsOpen;
                    SetCopy("Explore Evolution",
                        "DNA lets you change traits such as canopy and spread. A trait can help your plants in one set of conditions and come with a trade-off in another.",
                        "Open Evolution in the bottom-left, review the traits, then close the menu.");
                    break;
                case Step.Goal:
                    SetCopy("Grow your colony",
                        $"The coverage bar tracks how much of the planet your colony occupies. Your goal is {coverageGoal:0.##%} plant coverage. Your plants spread over time if conditions support them.",
                        string.Empty);
                    break;
            }
        }

        void SetCopy(string title, string body, string instruction)
        {
            heading.text = title;
            description.text = body;
            action.text = instruction;
        }

        void Finish()
        {
#if UNITY_EDITOR
            dismissedInPlaySession = true;
#else
            PlayerPrefs.SetInt(FinishedKey, 1);
            PlayerPrefs.Save();
#endif
            Destroy(gameObject);
        }

        // nothingburger tbh
        void BuildPanel()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var panel = new GameObject("Instructions", typeof(Image)).GetComponent<Image>();
            panel.transform.SetParent(transform, false);
            panel.color = new Color(0.055f, 0.105f, 0.105f, 0.96f);
            panel.raycastTarget = false;
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 24f);
            rect.sizeDelta = new Vector2(900f, 198f);

            heading = AddLabel(rect, 30, new Vector2(24f, 142f), new Vector2(-112f, -12f));
            heading.fontStyle = FontStyles.Bold;
            progress = AddLabel(rect, 24, new Vector2(24f, 142f), new Vector2(-24f, -12f));
            progress.alignment = TextAlignmentOptions.MidlineRight;
            description = AddLabel(rect, 23, new Vector2(24f, 65f), new Vector2(-24f, -56f));
            action = AddLabel(rect, 23, new Vector2(24f, 14f), new Vector2(-146f, -133f));
            action.fontStyle = FontStyles.Bold;
            action.color = new Color(0.70f, 0.93f, 0.77f);

            var buttonImage = new GameObject("Dismiss Tutorial", typeof(Image)).GetComponent<Image>();
            buttonImage.transform.SetParent(rect, false);
            buttonImage.color = new Color(0.19f, 0.43f, 0.31f);
            var buttonRect = buttonImage.rectTransform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.anchoredPosition = new Vector2(-76f, 40f);
            buttonRect.sizeDelta = new Vector2(104f, 44f);
            var button = buttonImage.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(Finish);
            buttonLabel = AddLabel(buttonRect, 21, Vector2.zero, Vector2.zero);
            buttonLabel.alignment = TextAlignmentOptions.Center;
        }

        static TMP_Text AddLabel(RectTransform parent, float size, Vector2 min, Vector2 max)
        {
            var label = new GameObject("Text", typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = min;
            label.rectTransform.offsetMax = max;
            return label;
        }
    }
}
