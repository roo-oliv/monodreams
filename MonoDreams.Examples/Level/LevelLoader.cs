using DefaultEcs;
using Microsoft.Xna.Framework.Content;
using MonoDreams.Examples.Level.Levels;
using MonoDreams.Level;
using MonoDreams.Renderer;

namespace MonoDreams.Examples.Level;

public class LevelLoader(World world, ContentManager content, ResolutionIndependentRenderer renderer) : ALevelLoader(world, content, renderer)
{
    public override void LoadLevel(int index)
    {
        CurrentLevel = index;
        ILevel level = index switch
        {
            0 => new Level0(Content, Renderer),
            1 => new Level1(Content, Renderer),
            _ => new Level0(Content, Renderer)
        };
        level.Load(World);
    }
}