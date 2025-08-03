namespace MonoDreams.Examples.Component;

public class OvertakingOpportunity
{
    public float StartPercentage { get; set; } // Start position in spline percentage
    public float EndPercentage { get; set; }   // End position in spline percentage
    public float StraightLength { get; set; }  // Length of the straight section in world units
    public float EntrySpeed { get; set; }      // Speed at the entry of the opportunity
    public float ExitSpeed { get; set; }       // Speed at the exit/braking point
    public float SpeedDifferential { get; set; } // How much speed is lost at the braking point
    public float Quality { get; set; }         // Calculated quality score of the overtaking opportunity
}

public class VelocityProfileComponent
{
    public float[] VelocityProfile = [0f, 0f, 0f, 0f];
    public float[] DistanceProfile { get; set; } = Array.Empty<float>();  // Distance at each point
    public float TotalTrackLength { get; set; }  // Total track length in world units

    // Track statistics
    public float MaxSpeed { get; set; }
    public float MinSpeed { get; set; }
    public float AverageSpeed { get; set; }
    public bool StatsCalculated { get; set; }

    // Overtaking opportunities 
    public List<OvertakingOpportunity> OvertakingOpportunities { get; set; } = new();
    public int OvertakingOpportunityCount => OvertakingOpportunities.Count;
    public float BestOvertakingQuality => OvertakingOpportunities.Count > 0 ? OvertakingOpportunities.Max(o => o.Quality) : 0;
}