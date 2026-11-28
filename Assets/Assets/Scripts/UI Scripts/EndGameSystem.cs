using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WorldSim
{
    [System.Serializable]
    public class EndGameParams
    {
        [Header("Victory")]
        public float victoryMinTemp = 10f;              // mean plant temperature, deg C
        public float victoryMaxTemp = 30f;
        [Min(1)] public int victoryHoldSlowTicks = 60;  // slow ticks the win conditions must hold in a row
        public Sprite victoryIcon;
        public string victoryMessage = "Congratulations you have created a stable home for life!";

        [Header("Extinction (by likely cause). Empty icons use the generic extinction icon")]
        public Sprite extinctionIcon;
        public Sprite tooHotIcon;
        public string tooHotMessage = @"
It appears the planet was too hot to accommodate your plant. There are several evolutions that can prevent this outcome.  
A lighter pigmentation can be used to reflect more solar energy into space. 
More transpiration moderately lower temperatures. 
Canopy size increases effects the magnitude of both pigmentation and transpiration evolutions as well. 
You get a second chance but your plant does not. 
        ";
        public Sprite tooColdIcon;
        public string tooColdMessage = @"
It appears the planet was too cold to accommodate your plant. There are several evolutions that can prevent this outcome. 
A darker pigmentation can be used to absorb more solar energy. 
Less transpiration moderately raises temperatures. 
Canopy size increases effects the magnitude of both pigmentation and transpiration evolutions as well. Meaning the larger the canopy, the more pigmentation and transpiration effect the environment. 
You get a second chance but your plant does not. 
        ";
        public Sprite droughtIcon;
        public string droughtMessage = @"
It appears the planet became too dry to accommodate your plant. To prevent this, less transpiration and a smaller canopy size can preserve water levels. There is still water in the air, but not enough in the ground. 
You get a second chance but your plant does not. 
        ";
        public Sprite floodIcon;
        public string floodMessage = @"
It appears the planet became too wet to support your plant. To prevent this, a larger canopy size can preserve water levels. 

You get a second chance but your plant does not. 
        ";
    }

    public sealed class EndGameSystem : ITickSystem
    {
        readonly PlantModel model;
        readonly TickEngine engine;
        readonly float victoryCoverage;
        readonly EndGameParams p;
        int heldTicks;
        string endMessage = "";
        string causeWord = "";
        Sprite endIcon;
        PlantDeathCause lastCause;

        // 0 to 1: how far the victory hold timer has run
        public float VictoryProgress => Mathf.Clamp01((float)heldTicks / p.victoryHoldSlowTicks);

        public enum ProgressReset { None, LowCoverage, TooCold, TooHot }
        // Which victory condition broke and reset progress. Stays set until all conditions are met again
        public ProgressReset LastReset { get; private set; }
        // True once the game has ended and the player chose Keep Playing
        public bool KeptPlaying => hasEnded && overlay == null;

        GameObject overlay;
        bool hadPlants;
        bool hasEnded;
        bool wasPaused;

        public EndGameSystem(PlantModel model, TickEngine engine, float victoryCoverage, EndGameParams p)
        {
            this.model = model;
            this.engine = engine;
            // Coverage is a fraction from 0 to 1
            this.victoryCoverage = Mathf.Clamp01(victoryCoverage);
            this.p = p;
            // Keep the main death cause of each slow tick. The last one before extinction is the likely cause
            model.PlantDeathsReported += r => { if (r.TotalDeaths > 0) lastCause = r.PrimaryCause; };
        }

        public void Tick(WorldGrid grid, int tickNumber)
        {
            // DOesnt need to check if the main screen is up
            if (hasEnded || overlay != null)
                return;

            // Grab coverage for our use
            float coverage = model.Stats.Coverage;

            // Prevents starting with a failure when you start with an empty map
            if (coverage <= 0f)
            {
                if (hadPlants)
                {
                    SetCause();
                    FinishRun(false, coverage);
                }
                return;
            }
            hadPlants = true;

            // Rolling timer: coverage and temperature must hold for victoryHoldSlowTicks in a row
            float temp = model.Stats.MeanTemp;
            bool met = coverage >= victoryCoverage && temp >= p.victoryMinTemp && temp <= p.victoryMaxTemp;
            if (met) LastReset = ProgressReset.None;
            else if (heldTicks > 0)
                LastReset = coverage < victoryCoverage ? ProgressReset.LowCoverage
                    : temp < p.victoryMinTemp ? ProgressReset.TooCold : ProgressReset.TooHot;
            heldTicks = met ? heldTicks + 1 : 0;
            if (heldTicks >= p.victoryHoldSlowTicks)
            {
                SetVictory();
                FinishRun(true, coverage);
            }
        }

        void FinishRun(bool won, float coverage)
        {
            hasEnded = true;
            ShowEnding(won, coverage);
        }

        void SetVictory()
        {
            causeWord = "";
            endMessage = p.victoryMessage;
            endIcon = p.victoryIcon;
        }

        // Picks the one-word cause, its message and its icon. Other causes are a plain extinction
        void SetCause()
        {
            causeWord = "";
            endMessage = "";
            Sprite icon = null;
            switch (lastCause)
            {
                case PlantDeathCause.Heat:        causeWord = "Heat";    endMessage = p.tooHotMessage;  icon = p.tooHotIcon;  break;
                case PlantDeathCause.Cold:        causeWord = "Cold";    endMessage = p.tooColdMessage; icon = p.tooColdIcon; break;
                case PlantDeathCause.Drought:     causeWord = "Drought"; endMessage = p.droughtMessage; icon = p.droughtIcon; break;
                case PlantDeathCause.Waterlogged: causeWord = "Flood";   endMessage = p.floodMessage;   icon = p.floodIcon;   break;
            }
            endIcon = icon != null ? icon : p.extinctionIcon;
        }

#if UNITY_EDITOR
        // Lets you dismiss popups
        public void PreviewEnding(bool won)
        {
            if (won) SetVictory();
            else SetCause();
            if (overlay == null)
                ShowEnding(won, model.Stats.Coverage);
        }
#endif

        void ShowEnding(bool won, float coverage)
        {
            // Save the gamestate before whenever the popup appears
            wasPaused = engine.Paused;
            engine.Paused = true;
            // Hide other tooltips that may have been up when endgame popup appears
            if (TooltipSystem.Instance != null)
                TooltipSystem.Instance.Hide();

            // Creates a new canvas and puts it above all the rest
            overlay = new GameObject("TellusEndGameOverlay", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000; // Places it above all the other canvases

            // Scale to fit game window
            CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Shades behind the canvas to make the background dimmed to draw attenition to endgame popup
            Image shade = AddImage(overlay.transform, "Shade", new Color(0.015f, 0.035f, 0.04f, 0.88f));
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = Vector2.one;
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;

            // Center result popup. Stacks its contents top to bottom and grows to fit them
            Image panel = AddImage(overlay.transform, "Panel", new Color(0.055f, 0.105f, 0.105f, 0.99f));
            Place(panel.rectTransform, Vector2.zero, new Vector2(900f, 0f));
            VerticalLayoutGroup stack = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(50, 50, 40, 40);
            stack.spacing = 20f;
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = stack.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Victory or extinction icon at the top of the popup
            if (endIcon != null)
            {
                Image icon = AddImage(panel.transform, "EndIcon", Color.white);
                icon.sprite = endIcon;
                icon.preserveAspect = true;
                LayoutElement iconSize = icon.gameObject.AddComponent<LayoutElement>();
                iconSize.preferredWidth = iconSize.preferredHeight = 120f;
            }

            TMP_Text title = AddLabel(panel.transform, won ? "PLANET RESTORED" : "EXTINCTION",
                48f, Vector2.zero, Vector2.zero);
            title.color = won ? new Color(0.55f, 0.88f, 0.56f) : new Color(0.96f, 0.55f, 0.38f);
            // Convert coverage fractions to percentages 
            AddLabel(panel.transform, $"PLANT COVERAGE     {coverage * 100f:0.###}%\n" +
                $"COVERAGE GOAL     {victoryCoverage * 100f:0.###}%",
                28f, Vector2.zero, Vector2.zero);

            // Likely cause of extinction, one word (skipped on victory or an unmatched cause)
            if (!string.IsNullOrEmpty(causeWord))
            {
                TMP_Text cause = AddLabel(panel.transform, causeWord, 34f, Vector2.zero, Vector2.zero);
                cause.color = new Color(0.96f, 0.75f, 0.45f);
            }

            // Custom message. Its length sets the height of the popup
            if (!string.IsNullOrEmpty(endMessage))
                AddLabel(panel.transform, endMessage, 22f, Vector2.zero, Vector2.zero);

            // Buttons side by side at the bottom
            GameObject buttons = new GameObject("Buttons", typeof(RectTransform));
            buttons.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup row = buttons.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 40f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            AddButton(buttons.transform, "Restart", Vector2.zero,
                new Color(0.19f, 0.43f, 0.31f), Restart);
            AddButton(buttons.transform, "Keep Playing", Vector2.zero,
                new Color(0.20f, 0.30f, 0.31f), KeepPlaying);
        }

        // Restores the saved pause state and gets rid of the popup to keep playing
        void KeepPlaying()
        {
            Object.Destroy(overlay);
            overlay = null;
            engine.Paused = wasPaused;
        }

        // Reloads the current scenes
        static void Restart()
        {
            Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            if (scene.buildIndex < 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    scene.path, new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            SceneManager.LoadScene(scene.buildIndex);
        }

        // Function for popup and shade
        static Image AddImage(Transform parent, string name, Color color)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(Image));
            item.transform.SetParent(parent, false);
            Image image = item.GetComponent<Image>();
            image.color = color;
            return image;
        }

        // Centers popup and shade
        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static TMP_Text AddLabel(Transform parent, string value, float size, Vector2 position, Vector2 bounds)
        {
            GameObject label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(parent, false);
            TextMeshProUGUI text = label.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            Place(text.rectTransform, position, bounds);
            return text;
        }

        // Give the button a label and connect it to its action
        static void AddButton(Transform parent, string label, Vector2 position, Color color, UnityAction onClick)
        {
            Image image = AddImage(parent, label, color);
            Vector2 size = new Vector2(250f, 64f);
            Place(image.rectTransform, position, size);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            AddLabel(image.transform, label, 23f, Vector2.zero, size);
        }
    }
}
