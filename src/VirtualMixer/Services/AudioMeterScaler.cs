namespace VirtualMixer.Services;

public static class AudioMeterScaler
{
    /// <summary>
    /// Maps linear WASAPI peak (often very small) to a 0..1 range for UI meters.
    /// </summary>
    public static float ToDisplayLevel(float linearPeak)
    {
        if (linearPeak <= 0f)
        {
            return 0f;
        }

        const float floor = 0.000_001f;
        const float minDb = -50f;
        var db = 20f * MathF.Log10(MathF.Max(linearPeak, floor));
        var normalized = (db - minDb) / -minDb;
        return Math.Clamp(normalized, 0f, 1f);
    }
}
