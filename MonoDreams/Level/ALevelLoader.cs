using DefaultEcs;
using Microsoft.Xna.Framework.Content;
using MonoDreams.Renderer;

namespace MonoDreams.Level;

public abstract class ALevelLoader(World world, ContentManager content, ResolutionIndependentRenderer renderer)
{
    protected World World = world;
    protected readonly ContentManager Content = content;
    protected readonly ResolutionIndependentRenderer Renderer = renderer;

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