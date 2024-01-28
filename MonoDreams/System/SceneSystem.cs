using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using MonoDreams.Component;
using MonoDreams.Level;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class SceneSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly ContentManager _content;

    private int _ballCount;
    private int _brickCount;

    public SceneSystem(World world, ContentManager content)
    {
        _world = world;
        _content = content;
        var level = new Level2(_world, _content);
        level.Load();
    }

    public bool IsEnabled { get; set; } = true;

    public void Update(GameState state)
    {
    }

    public void Dispose() { }
}