using DefaultEcs;

namespace MonoDreams.Examples.Component;

public class TrackGradeDisplayComponent(Dictionary<TrackScoreComponent.Grade, (Entity label, Entity threshold)> displays)
{
    public Dictionary<TrackScoreComponent.Grade, (Entity label, Entity threshold)> Displays { get; } = displays;
}