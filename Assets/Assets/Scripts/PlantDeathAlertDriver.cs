using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WorldSim
{
    // Why a tile's plant died: the suitability factor that dragged it down most, and which side of the optimum it was on.
    public enum PlantDeathCause
    {
        Unknown = 0,
        Cold, Heat,
        Drought, Waterlogged,
        Darkness, Scorch,
        CarbonStarved, CarbonOverload,
        DryAir, Humid,
    }

    // One slow tick's plant deaths, counted by cause. Filled by GrowthSystem and sent through PlantModel.PlantDeathsReported.
    // The same object is reused every tick, so listeners copy what they need.
    public sealed class PlantDeathReport
    {
        public static readonly int CauseCount = Enum.GetValues(typeof(PlantDeathCause)).Length;

        public int Tick { get; private set; }
        public int AliveBefore { get; internal set; }   // occupied tiles going into this slow tick
        public int TotalDeaths { get; private set; }
        public readonly int[] DeathsByCause = new int[CauseCount];

        public PlantDeathCause PrimaryCause
        {
            get
            {
                int best = 0;
                for (int c = 1; c < CauseCount; c++)
                    if (DeathsByCause[c] > DeathsByCause[best]) best = c;
                return (PlantDeathCause)best;
            }
        }

        internal void Begin(int tick)
        {
            Tick = tick;
            AliveBefore = 0;
            TotalDeaths = 0;
            Array.Clear(DeathsByCause, 0, CauseCount);
        }

        internal void Add(PlantDeathCause cause)
        {
            DeathsByCause[(int)cause]++;
            TotalDeaths++;
        }
    }

    // Goes on the GameManager next to WorldBootstrap. Keeps a rolling window of plant deaths and shows one alert icon
    // for the main cause, coloured by how fast plants are dying.
    [RequireComponent(typeof(WorldBootstrap))]
    public class PlantDeathAlertDriver : MonoBehaviour
    {
        public enum Severity { None = 0, Low, Moderate, High, Critical }

        [Serializable]
        public class CauseVisual
        {
            public PlantDeathCause cause;
            public Sprite icon;
            public string title;
            [TextArea(2, 4)] public string advice;
        }

        [Header("UI")]
        [Tooltip("Switched on while an alert is active. Defaults to the icon's GameObject.")]
        [SerializeField] GameObject alertRoot;
        [SerializeField] Image icon;
        [Tooltip("Optional. Shows the cause and the share of plants lost.")]
        [SerializeField] TMP_Text label;
        [Tooltip("Optional. Gets the cause and advice so hovering the icon explains it.")]
        [SerializeField] TooltipTarget tooltip;

        [Header("Detection window")]
        [Tooltip("Slow ticks of deaths added together to pick the main cause and measure the death rate.")]
        [SerializeField, Min(1)] int windowSlowTicks = 10;
        [Tooltip("Fewer deaths than this in the window never raise an alert, so a few frontier tiles dying stay quiet.")]
        [SerializeField, Min(1)] int minDeathsToAlert = 5;

        [Header("Severity: share of living plants lost over the window")]
        [SerializeField, Range(0f, 1f)] float lowRate = 0.01f;
        [SerializeField, Range(0f, 1f)] float moderateRate = 0.03f;
        [SerializeField, Range(0f, 1f)] float highRate = 0.08f;
        [SerializeField, Range(0f, 1f)] float criticalRate = 0.15f;

        [Header("Severity colours")]
        [SerializeField] Color lowColor = new Color(0.95f, 0.85f, 0.35f);
        [SerializeField] Color moderateColor = new Color(0.98f, 0.60f, 0.20f);
        [SerializeField] Color highColor = new Color(0.92f, 0.30f, 0.20f);
        [SerializeField] Color criticalColor = new Color(0.75f, 0.10f, 0.35f);
        [Tooltip("Fade between colours as the rate moves between thresholds, instead of jumping at each one.")]
        [SerializeField] bool blendColors = true;

        [Header("Behaviour")]
        [Tooltip("Slow ticks an alert stays up after deaths fall below the Low rate, so it doesn't flicker.")]
        [SerializeField, Min(0)] int holdSlowTicks = 6;
        [Tooltip("A new cause must lead the current one by this share of the window's deaths before the icon switches.")]
        [SerializeField, Range(0f, 1f)] float causeSwitchMargin = 0.15f;
        [SerializeField] bool pulseOnCritical = true;
        [SerializeField, Min(0f)] float pulseSpeed = 4f;
        [SerializeField, Range(0f, 1f)] float pulseMinAlpha = 0.45f;

        [Header("Icon and text per cause")]
        [SerializeField] CauseVisual[] causeVisuals = DefaultVisuals();

        // cause, severity, rate. Raised whenever the alert changes, for any other UI that wants to react.
        public event Action<PlantDeathCause, Severity, float> AlertChanged;

        public PlantDeathCause CurrentCause { get; private set; }
        public Severity CurrentSeverity { get; private set; }
        public float CurrentRate { get; private set; }
        public int WindowDeaths => windowDeaths;

        WorldBootstrap bootstrap;
        PlantModel model;

        // Rolling window: one slot per slow tick, with running totals kept in step.
        int[][] slotDeaths;
        int[] slotTotals, slotAlive;
        int head;
        readonly int[] windowByCause = new int[PlantDeathReport.CauseCount];
        int windowDeaths;
        int holdLeft;
        Color baseColor;

        void Awake()
        {
            bootstrap = GetComponent<WorldBootstrap>();
            if (alertRoot == null && icon != null) alertRoot = icon.gameObject;
            AllocateWindow();
            Hide();
        }

        void Update()
        {
            // WorldBootstrap builds the model in its Start, which may run after ours, so hook up as soon as it exists.
            if (model == null && bootstrap.Model != null)
            {
                model = bootstrap.Model;
                model.PlantDeathsReported += OnPlantDeaths;
            }

            if (pulseOnCritical && CurrentSeverity == Severity.Critical && icon != null)
            {
                float a = Mathf.Lerp(pulseMinAlpha, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed));
                icon.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * a);
            }
        }

        void OnDestroy()
        {
            if (model != null) model.PlantDeathsReported -= OnPlantDeaths;
        }

        void OnValidate()
        {
            // Keep the thresholds in order.
            moderateRate = Mathf.Max(moderateRate, lowRate);
            highRate = Mathf.Max(highRate, moderateRate);
            criticalRate = Mathf.Max(criticalRate, highRate);
        }

        void AllocateWindow()
        {
            int n = Mathf.Max(1, windowSlowTicks);
            slotDeaths = new int[n][];
            for (int s = 0; s < n; s++) slotDeaths[s] = new int[PlantDeathReport.CauseCount];
            slotTotals = new int[n];
            slotAlive = new int[n];
            Array.Clear(windowByCause, 0, windowByCause.Length);
            windowDeaths = 0;
            head = 0;
        }

        void OnPlantDeaths(PlantDeathReport r)
        {
            if (slotTotals.Length != Mathf.Max(1, windowSlowTicks)) AllocateWindow(); // window resized in the Inspector

            // Overwrite the oldest slot.
            int[] slot = slotDeaths[head];
            for (int c = 0; c < slot.Length; c++)
            {
                windowByCause[c] += r.DeathsByCause[c] - slot[c];
                slot[c] = r.DeathsByCause[c];
            }
            windowDeaths += r.TotalDeaths - slotTotals[head];
            slotTotals[head] = r.TotalDeaths;
            slotAlive[head] = r.AliveBefore;
            head = (head + 1) % slotTotals.Length;

            Evaluate();
        }

        void Evaluate()
        {
            // Rate = plants lost in the window / the most plants alive at any point in it.
            int peakAlive = 0;
            for (int s = 0; s < slotAlive.Length; s++) peakAlive = Mathf.Max(peakAlive, slotAlive[s]);
            float rate = windowDeaths / (float)Mathf.Max(1, peakAlive);

            Severity severity = windowDeaths >= minDeathsToAlert ? Classify(rate) : Severity.None;

            if (severity != Severity.None)
            {
                holdLeft = holdSlowTicks;
                Show(PickCause(), severity, rate);
            }
            else if (CurrentSeverity != Severity.None)
            {
                if (holdLeft > 0) holdLeft--;
                else Hide();
            }
        }

        Severity Classify(float rate)
        {
            if (rate >= criticalRate) return Severity.Critical;
            if (rate >= highRate) return Severity.High;
            if (rate >= moderateRate) return Severity.Moderate;
            if (rate >= lowRate) return Severity.Low;
            return Severity.None;
        }

        PlantDeathCause PickCause()
        {
            int leader = 0;
            for (int c = 1; c < windowByCause.Length; c++)
                if (windowByCause[c] > windowByCause[leader]) leader = c;

            // Keep the current cause unless the new leader is clearly ahead, so two close causes don't make the icon flicker.
            if (CurrentSeverity != Severity.None && leader != (int)CurrentCause
                && windowByCause[leader] - windowByCause[(int)CurrentCause] < causeSwitchMargin * windowDeaths)
                return CurrentCause;

            return (PlantDeathCause)leader;
        }

        void Show(PlantDeathCause cause, Severity severity, float rate)
        {
            bool changed = cause != CurrentCause || severity != CurrentSeverity || !Mathf.Approximately(rate, CurrentRate);
            CurrentCause = cause;
            CurrentSeverity = severity;
            CurrentRate = rate;

            CauseVisual v = VisualFor(cause);
            string title = v != null && !string.IsNullOrEmpty(v.title) ? v.title : cause.ToString();
            baseColor = ColorFor(rate);

            if (alertRoot != null && !alertRoot.activeSelf) alertRoot.SetActive(true);
            if (icon != null)
            {
                if (v != null && v.icon != null) icon.sprite = v.icon;
                icon.color = baseColor;
            }
            if (label != null)
            {
                label.text = $"{title}  -{rate * 100f:0.#}%";
                label.color = baseColor;
            }
            if (tooltip != null)
            {
                tooltip.title = $"{title} ({severity})";
                tooltip.body = $"{(v != null ? v.advice : "")}\n\n{windowDeaths} plant tiles lost recently ({rate * 100f:0.#}% of the population).";
            }

            if (changed) AlertChanged?.Invoke(cause, severity, rate);
        }

        void Hide()
        {
            bool changed = CurrentSeverity != Severity.None;
            CurrentSeverity = Severity.None;
            CurrentRate = 0f;
            if (alertRoot != null) alertRoot.SetActive(false);
            if (changed) AlertChanged?.Invoke(CurrentCause, Severity.None, 0f);
        }

        Color ColorFor(float rate)
        {
            if (!blendColors)
            {
                switch (Classify(rate))
                {
                    case Severity.Critical: return criticalColor;
                    case Severity.High: return highColor;
                    case Severity.Moderate: return moderateColor;
                    default: return lowColor;
                }
            }

            if (rate >= criticalRate) return criticalColor;
            if (rate >= highRate) return Color.Lerp(highColor, criticalColor, Mathf.InverseLerp(highRate, criticalRate, rate));
            if (rate >= moderateRate) return Color.Lerp(moderateColor, highColor, Mathf.InverseLerp(moderateRate, highRate, rate));
            return Color.Lerp(lowColor, moderateColor, Mathf.InverseLerp(lowRate, moderateRate, rate));
        }

        CauseVisual VisualFor(PlantDeathCause cause)
        {
            if (causeVisuals == null) return null;
            for (int i = 0; i < causeVisuals.Length; i++)
                if (causeVisuals[i] != null && causeVisuals[i].cause == cause) return causeVisuals[i];
            return null;
        }

        // Starting text for every cause; sprites are assigned in the Inspector.
        static CauseVisual[] DefaultVisuals() => new[]
        {
            new CauseVisual { cause = PlantDeathCause.Unknown, title = "Plants Dying",
                advice = "Plants are dying back. Check the field views to see which conditions changed." },
            new CauseVisual { cause = PlantDeathCause.Cold, title = "Too Cold",
                advice = "It is too cold for the plants. Darker pigmentation absorbs more sunlight and warms the land, and CO2 in the air traps heat." },
            new CauseVisual { cause = PlantDeathCause.Heat, title = "Too Hot",
                advice = "It is too hot for the plants. Lighter pigmentation reflects sunlight, and higher transpiration cools the land." },
            new CauseVisual { cause = PlantDeathCause.Drought, title = "Drought",
                advice = "The soil is too dry. Transpiration pulls water out of the ground, so lower transpiration keeps more of it in the soil." },
            new CauseVisual { cause = PlantDeathCause.Waterlogged, title = "Waterlogged",
                advice = "The ground is saturated. Higher transpiration moves more water out of the soil and into the air." },
            new CauseVisual { cause = PlantDeathCause.Darkness, title = "Too Little Light",
                advice = "Not enough sunlight reaches the plants. Very light pigmentation reflects light away; darker leaves absorb more of it." },
            new CauseVisual { cause = PlantDeathCause.Scorch, title = "Too Much Light",
                advice = "The plants are getting more sunlight than they can handle. Lighter pigmentation reflects some of it away." },
            new CauseVisual { cause = PlantDeathCause.CarbonStarved, title = "CO2 Starved",
                advice = "Every plant locks CO2 into its biomass. With this much growth, the air no longer has enough CO2 to keep them alive." },
            new CauseVisual { cause = PlantDeathCause.CarbonOverload, title = "Too Much CO2",
                advice = "CO2 levels are above what the plants can tolerate. More plant cover pulls carbon out of the air." },
            new CauseVisual { cause = PlantDeathCause.DryAir, title = "Dry Air",
                advice = "The air is too dry. Higher transpiration releases water vapour that raises humidity." },
            new CauseVisual { cause = PlantDeathCause.Humid, title = "Too Humid",
                advice = "The air holds too much moisture. Lower transpiration puts less water vapour into the air." },
        };
    }
}
