using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftGet
{
    /// <summary>Global bandwidth limiter shared by every connection.</summary>
    public class SpeedLimiter
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly object gate = new object();
        double next;
        public Func<long> GetLimitBytesPerSec;

        public async Task WaitAsync(int bytes, CancellationToken ct)
        {
            long limit = GetLimitBytesPerSec == null ? 0 : GetLimitBytesPerSec();
            if (limit <= 0) return;
            double delay;
            lock (gate)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (next < now) next = now;
                next += (double)bytes / limit;
                delay = next - now;
            }
            if (delay > 0.05) await Task.Delay(TimeSpan.FromSeconds(delay), ct).ConfigureAwait(false);
        }
    }
}
