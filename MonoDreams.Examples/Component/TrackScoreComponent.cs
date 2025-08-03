namespace MonoDreams.Examples.Component;

public class TrackScoreComponent(int[] thresholds)
{
    public int[] ScoreThresholds { get; } = thresholds;
    public int TopSpeed { get; private set; }
    public int OvertakingSpots { get; private set; }
    public int Score { get; private set; }
    public Grade? CurrentGrade { get; private set; }
    
    public void UpdateScore(int topSpeed, int overtakingSpots)
    {
        TopSpeed = topSpeed / 5;
        OvertakingSpots = overtakingSpots;
        Score = TopSpeed * OvertakingSpots;
        CurrentGrade = CalculateGrade();
    }
    
    private Grade? CalculateGrade()
    {
        if (Score >= ScoreThresholds[3])
            return Grade.Platinum;
        if (Score >= ScoreThresholds[2])
            return Grade.Gold;
        if (Score >= ScoreThresholds[1])
            return Grade.Silver;
        if (Score >= ScoreThresholds[0])
            return Grade.Bronze;
        return null;
    }
    
    public enum Grade
    {
        Platinum,
        Gold,
        Silver,
        Bronze
    }
}

