using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Sprite;

namespace MonoDreams.EntityFactory;

public class PlayerFactory
{
    private readonly World _world;
    private readonly ContentManager _content;
    private readonly int _scale;

    public PlayerFactory(World world, ContentManager content, int scale)
    {
        _world = world;
        _content = content;
        _scale = scale;
    }

    public void CreatePlayer(Vector2 position)
    {
        var player = _world.CreateEntity();
        player.Set(new Position(position));
        var frameData = AnimationData.LoadAnimationData("Content/Main Characters/Virtual Guy/Idle.json");
        frameData.PopulateEntityAnimation(
            player,
            _content.Load<Texture2D>("Main Characters/Virtual Guy/Idle (32x32)"),
            new Rectangle(0, 0, 12 * _scale, 12 * _scale));
        player.Set(new PlayerInput());
        player.Set(new DynamicBody());
        player.Set(new MovementController());
        player.Set<Solid>(default);
    }
}