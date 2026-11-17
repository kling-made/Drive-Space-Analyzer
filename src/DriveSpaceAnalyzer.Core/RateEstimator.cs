namespace DriveSpaceAnalyzer.Core;

/// <summary>Smoothed directory throughput. A directory is not a unit of disk I/O.</summary>
public sealed class RateEstimator
{
    private TimeSpan previousTime;
    private long previousCount;
    private double rate;
    private double variation;
    private int samples;
    public TimeSpan? Update(long processed, long total, TimeSpan analysisElapsed)
    {
        var seconds = (analysisElapsed - previousTime).TotalSeconds;
        if (seconds >= .2)
        {
            var sample = (processed - previousCount) / seconds;
            if (samples == 0) rate = sample;
            else
            {
                variation = .25 * Math.Abs(sample - rate) + .75 * variation;
                rate = .25 * sample + .75 * rate;
            }
            samples++; previousCount = processed; previousTime = analysisElapsed;
        }
        if (samples < 3 || analysisElapsed.TotalSeconds < 2 || processed < 3 || rate <= 0
            || variation > rate || processed >= total) return null;
        var estimate = (total - processed) / rate;
        return double.IsFinite(estimate) && estimate < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(estimate) : null;
    }
}
