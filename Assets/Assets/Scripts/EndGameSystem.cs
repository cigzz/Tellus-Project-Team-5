using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WorldSim
{
    public sealed class EndGameSystem : ITickSystem
    {
        readonly PlantModel model;
        readonly TickEngine engine;
        readonly float victoryCoverage;

        GameObject overlay;
        bool hadPlants;
        bool hasEnded;
        bool wasPaused;

        public EndGameSystem(PlantModel model, TickEngine engine, float victoryCoverage)
        {
            this.model = model;
            this.engine = engine;
            // Coverage is a fraction from 0 to 1
            this.victoryCoverage = Mathf.Clamp01(victoryCoverage);
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
                    FinishRun(false, coverage);
                return;
            }
            hadPlants = true;

            // Reaching the coverage goal ends the game
            if (coverage >= victoryCoverage)
                FinishRun(true, coverage);
        }

        void FinishRun(bool won, float coverage)
        {
            hasEnded = true;
            ShowEnding(won, coverage);
        }

#if UNITY_EDITOR
        // Lets you dismiss popups
        public void PreviewEnding(bool won)
        {
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

            // Center result popup
            Image panel = AddImage(overlay.transform, "Panel", new Color(0.055f, 0.105f, 0.105f, 0.99f));
            Place(panel.rectTransform, Vector2.zero, new Vector2(900f, 400f));

            TMP_Text title = AddLabel(panel.transform, won ? "PLANET RESTORED" : "EXTINCTION",
                48f, new Vector2(0f, 115f), new Vector2(820f, 70f));
            title.color = won ? new Color(0.55f, 0.88f, 0.56f) : new Color(0.96f, 0.55f, 0.38f);
            // Convert coverage fractions to percentages 
            AddLabel(panel.transform, $"PLANT COVERAGE     {coverage * 100f:0.###}%\n" +
                $"COVERAGE GOAL     {victoryCoverage * 100f:0.###}%",
                28f, new Vector2(0f, 10f), new Vector2(800f, 105f));

            AddButton(panel.transform, "Restart", new Vector2(-145f, -120f),
                new Color(0.19f, 0.43f, 0.31f), Restart);
            AddButton(panel.transform, "Keep Playing", new Vector2(145f, -120f),
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
