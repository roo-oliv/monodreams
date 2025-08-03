namespace MonoDreams.Examples.Component;

public enum StatType
{
    TopSpeed,
    MinSpeed,
    AverageSpeed,
    OvertakingSpots,
    BestOvertakingQuality,
    LapTime,
    Score,
    Grade,
}

public class StatComponent(StatType type)
{
    public StatType Type { get; } = type;
}
