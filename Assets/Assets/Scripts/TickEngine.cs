using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldSim
{
    // Anything that runs once per simulation tick implements this.
    public interface ITickSystem
    {
        void Tick(WorldGrid grid, int tickNumber);
    }
    public class TickEngine : MonoBehaviour
    {
        [SerializeField] float ticksPerSecond = 8f;
        [SerializeField] int slowGroupInterval = 3;
        public bool Paused;
        public WorldGrid Grid { get; private set; }
        public int TickNumber { get; private set; }

        // Called after every tick (fast + slow, when it ran) finishes. Use this to trigger a redraw.
        public event Action OnTick;
        readonly List<ITickSystem> systems = new List<ITickSystem>();
        readonly List<ITickSystem> slowSystems = new List<ITickSystem>();
        float timer;
        public void Init(WorldGrid grid)
        {
            Grid = grid;
        }
        // Main group: runs every tick. Order matters (e.g. water before temperature).
        public void AddSystem(ITickSystem system) => systems.Add(system);
        public void RemoveSystem(ITickSystem system) => systems.Remove(system);

        // Slow group: runs every slowGroupInterval ticks, in its own order.
        public void AddSlowSystem(ITickSystem system) => slowSystems.Add(system);
        public void RemoveSlowSystem(ITickSystem system) => slowSystems.Remove(system);

        void Update()
        {
            if (Paused || Grid == null) return;

            timer += Time.deltaTime;
            float tickLength = 1f / ticksPerSecond;

            // Catches up if a frame took longer than one tick
            int safety = 5;
            while (timer >= tickLength && safety-- > 0)
            {
                timer -= tickLength;
                RunOneTick();
            }
        }
        void RunOneTick()
        {
            foreach (ITickSystem system in systems)
                system.Tick(Grid, TickNumber);

            if (TickNumber % slowGroupInterval == 0)
            {
                foreach (ITickSystem system in slowSystems)
                    system.Tick(Grid, TickNumber);
            }

            TickNumber++;
            OnTick?.Invoke();
        }
    }
}
