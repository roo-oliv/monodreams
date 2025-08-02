namespace MonoDreams.Examples.Component;

public class VelocityProfileComponent
{
    public float[] VelocityProfile = [0f, 0f, 0f, 0f];

    // Track statistics
    public float MaxSpeed { get; set; }
    public float MinSpeed { get; set; }
    public float AverageSpeed { get; set; }
    public bool StatsCalculated { get; set; }
}