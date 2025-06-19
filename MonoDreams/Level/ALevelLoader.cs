using DefaultEcs;

namespace MonoDreams.Level;

public abstract class ALevelLoader(World world)
{
    protected World World = world;

    public int CurrentLevel { get; protected set; }

    public abstract void LoadLevel(int index);

    public void ReloadLevel(World world)
    {
        World = world;
        LoadLevel(CurrentLevel);
    }

    public void LoadNextLevel(World world)
    {
        World = world;
        LoadLevel(CurrentLevel + 1);
    }
}