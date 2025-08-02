namespace MonoDreams.Examples.Component;

public enum StatType
{
    MaxSpeed,
    MinSpeed,
    AverageSpeed
}

public class StatComponent(StatType type)
{
    public StatType Type { get; } = type;
}
