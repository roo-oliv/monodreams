using Microsoft.Xna.Framework; // For Point (for dimensions) and Vector2 (for offset)

namespace MonoDreams.Examples.Component.Level;

/// <summary>
/// Represents the processed IntGrid data for a level, used for collision detection.
/// This is intended to be stored as a world component.
/// </summary>
public class LevelCollisionGrid
{
    /// <summary>
    /// The actual grid data. Each value represents the collision type of a cell.
    /// Example: 0 = passable, 1 = solid. Other values can represent different materials.
    /// </summary>
    public readonly int[,] Grid;

    /// <summary>
    /// Width of the grid in cells.
    /// </summary>
    public readonly int Width;

    /// <summary>
    /// Height of the grid in cells.
    /// </summary>
    public readonly int Height;

    /// <summary>
    /// Size of each grid cell in pixels.
    /// </summary>
    public readonly int CellSize;

    /// <summary>
    /// The world position (top-left corner) of the grid.
    /// This can be used to translate world coordinates to grid coordinates.
    /// </summary>
    public readonly Vector2 WorldOffset;

    public LevelCollisionGrid(int width, int height, int cellSize, Vector2 worldOffset)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        WorldOffset = worldOffset;
        Grid = new int[width, height]; // Initialize with passable (0)
    }

    /// <summary>
    /// Gets the collision value at the specified cell coordinates.
    /// Returns 0 (passable) if coordinates are out of bounds.
    /// </summary>
    public int GetValue(int cellX, int cellY)
    {
        if (cellX >= 0 && cellX < Width && cellY >= 0 && cellY < Height)
        {
            return Grid[cellX, cellY];
        }
        return 0; // Default to passable for out-of-bounds
    }

    /// <summary>
    /// Sets the collision value at the specified cell coordinates.
    /// Does nothing if coordinates are out of bounds.
    /// </summary>
    public void SetValue(int cellX, int cellY, int value)
    {
        if (cellX >= 0 && cellX < Width && cellY >= 0 && cellY < Height)
        {
            Grid[cellX, cellY] = value;
        }
    }

    /// <summary>
    /// Converts world coordinates to grid cell coordinates.
    /// </summary>
    public Point WorldToCell(Vector2 worldPosition)
    {
        var localPosition = worldPosition - WorldOffset;
        int cellX = (int)(localPosition.X / CellSize);
        int cellY = (int)(localPosition.Y / CellSize);
        return new Point(cellX, cellY);
    }

    /// <summary>
    /// Gets the collision value at the given world coordinates.
    /// </summary>
    public int GetValueAtWorldPosition(Vector2 worldPosition)
    {
        Point cell = WorldToCell(worldPosition);
        return GetValue(cell.X, cell.Y);
    }
}
