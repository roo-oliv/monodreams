namespace MonoDreams.Examples.Component;

public enum StatType
{
    MaxSpeed,
    MinSpeed,
    AverageSpeed,
    OvertakingOpportunities,
    BestOvertakingQuality
}

public class StatComponent(StatType type)
{
    public StatType Type { get; } = type;
}
