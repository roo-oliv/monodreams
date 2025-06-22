using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Message.Level;
using MonoDreams.Examples.Objects;
using MonoDreams.Examples.Screens;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Level;

public class EntitySpawnSystem : ISystem<GameState>
{
    private const float CHARACTERS_BASE_LAYER_DEPTH = 0.1f; // Closest top layer depth for tiles
    private const float CHARACTERS_LAYER_DEPTH_STEP = 0.001f; // Small increment per character layer
    
    public bool IsEnabled { get; set; } = true;
    private readonly World _world;
    private readonly ContentManager _content;
    private readonly IReadOnlyDictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly Texture2D _charactersTileset;
    private readonly Texture2D _square;
    private float _currentLayerDepth;

    public EntitySpawnSystem(World world, ContentManager content, IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets)
    {
        _world = world;
        _content = content;
        _renderTargets = renderTargets;
        _charactersTileset = content.Load<Texture2D>("Characters");
        _square =  _content.Load<Texture2D>("square");
        _world.Subscribe<EntitySpawnRequest>(OnEntitySpawnRequest);
        _currentLayerDepth = CHARACTERS_BASE_LAYER_DEPTH;
    }

    [Subscribe]
    private void OnEntitySpawnRequest(in EntitySpawnRequest request)
    {
        switch (request.Identifier)
        {
            case "Player":
                CreatePlayerEntity(request);
                break;
            case "Enemy":
                CreateNPCEntity(request);
                break;
        }
        _currentLayerDepth += CHARACTERS_LAYER_DEPTH_STEP;
    }

    private void CreatePlayerEntity(EntitySpawnRequest request)
    {
        var entity = _world.CreateEntity();

        var drawComponent = new DrawComponent();
        entity.Set(drawComponent);
        drawComponent.Drawables.Add(
            new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = RenderTargetID.Main,
                Texture = _charactersTileset,
                Position = request.Position,
                SourceRectangle = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y, request.Layer._GridSize, request.Layer._GridSize),
                Color = Color.White * request.Layer._Opacity,
                Size = new Vector2(request.Layer._GridSize, request.Layer._GridSize), // Use GridSize for destination size
                LayerDepth = _currentLayerDepth,
                // Rotation, Origin, Scale, Effects usually default for tiles
            });
        
        // entity.Set(new EntityInfo(EntityType.Player));
        // entity.Set(new PlayerState());
        entity.Set(new InputControlled());
        entity.Set(new Position(request.Position));
        entity.Set(new BoxCollider(new Rectangle(Point.Zero, Constants.PlayerSize)));
        entity.Set(new RigidBody());
        entity.Set(new Velocity());
    }

    private void CreateNPCEntity(EntitySpawnRequest request)
    {
        var entity = _world.CreateEntity();
        
        var drawComponent = new DrawComponent();
        entity.Set(drawComponent);
        drawComponent.Drawables.Add(
            new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = RenderTargetID.Main,
                Texture = _charactersTileset,
                Position = request.Position,
                SourceRectangle = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y, request.Layer._GridSize, request.Layer._GridSize),
                Color = Color.White * request.Layer._Opacity,
                Size = new Vector2(request.Layer._GridSize, request.Layer._GridSize), // Use GridSize for destination size
                LayerDepth = _currentLayerDepth,
                // Rotation, Origin, Scale, Effects usually default for tiles
            });
        
        // entity.Set(new EntityInfo(EntityType.NPC));
        entity.Set(new Position(request.Position));
        entity.Set(new BoxCollider(new Rectangle(Point.Zero, Constants.PlayerSize)));
        entity.Set(new RigidBody());
        entity.Set(new Velocity());
    }

    public void Dispose()
    {
    }

    public void Update(GameState state)
    {
    }
}