using UnityEngine;
using UnityEngine.UI;

namespace WorldSim
{
    // Goes on the coverage bar. Fills the chosen Image by how far the victory hold timer has run.
    // Each gain eases in over one slow tick, so the bar trails the timer by a tick and moves smoothly.
    public class VictoryProgressBar : MonoBehaviour
    {
        [Tooltip("Image to fill. Can be anywhere in the scene.")]
        [SerializeField] Image fill;
        [Tooltip("Optional. Found automatically if left empty.")]
        [SerializeField] WorldBootstrap bootstrap;

        TickEngine engine;
        float from, to, t = 1f;

        void Awake()
        {
            if (bootstrap == null) bootstrap = FindFirstObjectByType<WorldBootstrap>();
            if (bootstrap != null) engine = bootstrap.GetComponent<TickEngine>();
            if (fill != null) fill.type = Image.Type.Filled;
        }

        void Update()
        {
            if (fill == null || engine == null || bootstrap.EndGame == null) return;
            EndGameSystem endGame = bootstrap.EndGame;

            // Hide the fill once the game has ended and the player chose Keep Playing
            if (endGame.KeptPlaying)
            {
                if (fill.gameObject.activeSelf) fill.gameObject.SetActive(false);
                return;
            }

            float target = endGame.VictoryProgress;
            if (target < to) { from = to = target; t = 1f; }                       // progress reset: drop at once
            else if (target > to) { from = fill.fillAmount; to = target; t = 0f; } // new gain: ease in over one slow tick

            float slowTickSeconds = engine.SlowGroupInterval / Mathf.Max(engine.GetTickRate(), 0.01f);
            t = Mathf.Min(1f, t + Time.deltaTime / slowTickSeconds);
            fill.fillAmount = Mathf.Lerp(from, to, t);
        }
    }
}
