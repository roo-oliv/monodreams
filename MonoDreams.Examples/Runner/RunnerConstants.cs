using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Runner;

public static class RunnerConstants
{
    // Player
    public const float PlayerRadius = 12f;
    public const float PlayerRunSpeed = 350f;
    public const float TreadmillDragSpeed = 120f;
    public const float PlayerJumpSpeed = -350f;
    public const float WorldGravity = 800f;
    public const float MaxFallVelocity = 800f;
    public const float PlayerRollSpeed = 5f;
    public const float FallGravityMultiplier = 2.0f;
    public const float JumpCutGravityMultiplier = 2.5f;

    // Treadmill
    public const int TreadmillSegmentCount = 30;
    public const float TreadmillSegmentWidth = 20f;
    public const float TreadmillSegmentHeight = 6f;
    public const float TreadmillSegmentGap = 2f;
    public const float TreadmillY = 100f;
    public const float TreadmillScrollSpeed = 120f;

    // Spawning
    public const float CharmSize = 10f;
    public const float ObstacleSize = 14f;

    // Spawn Point
    public static float SpawnPointX => TreadmillTotalWidth + 50f;
    public const float SpawnPointBaseY = 50f;
    public const float SpawnPointRadius = 8f;
    public const float SpawnPointAmplitude = 50f;
    public const float SpawnPointFrequency = 6.0f;
    public static readonly Color SpawnPointColor = new(80, 80, 80);
    public const float SpawnInterval = 1.5f;
    public const float ObstacleSpawnChance = 0.3f;

    // Boundaries
    public const float LeftBoundary = -30f;
    public static float CleanupX => -TreadmillSegmentWidth;
    public const float FallDeathY = 300f;

    // Camera
    public const float CameraZoom = 2.0f;
    public static readonly Vector2 CameraPosition = new(375f, 50f);

    // Treadmill bottom row
    public const float BottomRowGap = 4f;

    // Colors
    public static readonly Color PlayerColor = Color.White;
    public static readonly Color TreadmillColor = new(100, 100, 100);
    public static readonly Color TreadmillBottomColor = new(60, 60, 60);
    public static readonly Color CharmColor = Color.Gold;
    public static readonly Color ObstacleColor = Color.Red;
    public static readonly Color GameOverColor = Color.Red;
    public static readonly Color ScoreColor = Color.White;

    // HUD
    public static readonly Vector2 ScorePosition = new(10, 10);
    public const float ScoreTextScale = 0.2f;

    // Collider sizes (derived)
    public static readonly Point PlayerColliderSize = new((int)(PlayerRadius * 2), (int)(PlayerRadius * 2));
    public static readonly Point PlayerColliderOffset = new(-(int)PlayerRadius, -(int)PlayerRadius);

    // Treadmill total width
    public static float TreadmillTotalWidth =>
        TreadmillSegmentCount * (TreadmillSegmentWidth + TreadmillSegmentGap);

    // Player start position (centered on treadmill)
    public static Vector2 PlayerStartPosition => new(TreadmillTotalWidth / 2f, TreadmillY - PlayerRadius * 2 - 2);
}
