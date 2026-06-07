using System.Collections.Concurrent;
using Inferior.Core.Math;

namespace Inferior.Gameplay.SensorData
{
    // Models detonating EMP bombs and their effects on sensors and noise sources.

    public static class EMP
    {
        // Main thread enqueues; sim thread drains in Tick() before touching _activeEMPs.
        private static readonly ConcurrentQueue<(DVec3 position, double strength)> _pending   = new();
        private static readonly List<(DVec3 position, double level)>               _activeEMPs = new();

        /// <summary>Safe to call from any thread (e.g. weapon systems on the main thread).</summary>
        public static void DetonateEMP(DVec3 position, double strength = 100)
            => _pending.Enqueue((position, strength));

        public static double EMLevelAt(DVec3 point)
        {
            double total = 0;
            foreach (var (pos, level) in _activeEMPs)
            {
                double distSq = (pos - point).LengthSquared;
                if (distSq < 1e-6) distSq = 1e-6; // avoid singularity at source
                total += level / distSq; // simple inverse-square falloff
            }
            return total;
        }


        // Called from sim thread each tick to update EMP states over time.
        public static void Tick(double deltaTime)
        {
            while (_pending.TryDequeue(out var d))
                _activeEMPs.Add(d);

            for (int i = _activeEMPs.Count - 1; i >= 0; i--)
            {
                var (pos, level) = _activeEMPs[i];
                level -= deltaTime * 20; // decay rate (20 units per second)
                if (level <= 0)
                    _activeEMPs.RemoveAt(i);
                else
                    _activeEMPs[i] = (pos, level);
            }
        }

    }
}
