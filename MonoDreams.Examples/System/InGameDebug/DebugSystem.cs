using DefaultEcs;
using DefaultEcs.System;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Debug;
using MonoDreams.Examples.Component;
using MonoDreams.LegacyComponents;
using MonoDreams.Message;
using MonoDreams.State;
using MonoGame.ImGuiNet;
using Position = MonoDreams.Component.Position;

namespace MonoDreams.Examples.System.InGameDebug;

public class DebugSystem : AComponentSystem<GameState, DebugUI>
{
    private readonly ImGuiRenderer _guiRenderer;
    private List<CollisionMessage> _collisions = [];
    // private List<PlayerTouchMessage> _touches = [];
    private readonly World _world;
    private readonly Game _game;
    private readonly SpriteBatch _batch;

    public DebugSystem(World world, Game game, SpriteBatch batch) : base(world)
    {
        _world = world;
        _game = game;
        _batch = batch;
        _guiRenderer = new ImGuiRenderer(game);
        _world.Subscribe(this);
    }

    [Subscribe]
    private void On(in CollisionMessage message) => _collisions.Add(message);

    // [Subscribe]
    // private void On(in PlayerTouchMessage message) => _touches.Add(message);

    protected override void Update(GameState state, ref DebugUI debugUI)
    {
        _guiRenderer.RebuildFontAtlas();
        _batch.GraphicsDevice.SetRenderTarget(debugUI.DebugRenderTarget);
        _batch.GraphicsDevice.Clear(Color.Transparent);
        _guiRenderer.BeforeLayout(state.GameTime.current);

        _game.IsMouseVisible = true;
        
        var player = _world.GetEntities().With<PlayerState>().AsEnumerable().First();
        ImGui.Begin("PlayerState", ImGuiWindowFlags.Modal);
        
        ImGui.Text($"Position: {player.Get<Position>().Current}");
        // ImGui.Text($"Bounds: {player.Get<Collidable>().Bounds}");
        
        // ImGui.SetNextItemOpen(true);
        // if (ImGui.TreeNode("State"))
        // {
        //     var playerState = player.Get<PlayerState>();
        //     ImGui.Text($"Movement: {playerState.Movement}");
        //     ImGui.Text($"Grabbing: {playerState.Grabbing}");
        //     ImGui.Text($"Riding: {playerState.Riding}");
        //     ImGui.TreePop();
        // }
        //
        // ImGui.SetNextItemOpen(true);
        // if (ImGui.TreeNode("Dynamic Body"))
        // {
        //     var dynamicBody = player.Get<DynamicBody>();
        //     ImGui.Text($"Gravity: {dynamicBody.Gravity}");
        //     ImGui.Text($"IsJumping: {dynamicBody.IsJumping}");
        //     ImGui.Text($"IsRiding: {dynamicBody.IsRiding}");
        //     ImGui.Text($"IsSliding: {dynamicBody.IsSliding}");
        //     ImGui.TreePop();
        // }
        //
        // ImGui.SetNextItemOpen(true);
        // if (ImGui.TreeNode("Movement Controller"))
        // {
        //     var movementController = player.Get<MovementController>();
        //     ImGui.Text($"Velocity: {movementController.Velocity}");
        //     ImGui.TreePop();
        // }
        
        ImGui.End();
        
        ImGui.Begin("Collision Messages", ImGuiWindowFlags.Modal);
        foreach (var collision in _collisions)
        {
            ImGui.SetNextItemOpen(true);
            if (ImGui.TreeNode("Collision"))
            {
                ImGui.Text($"Base Entity: {collision.BaseEntity}");
                ImGui.Text($"Colliding Entity: {collision.CollidingEntity}");
                ImGui.Text($"Contact Time: {collision.ContactTime}");
                ImGui.TreePop();
            }
        }
        ImGui.End();
        
        // ImGui.Begin("PlayerTouch Messages", ImGuiWindowFlags.Modal);
        // foreach (var touch in _touches)
        // {
        //     ImGui.SetNextItemOpen(true);
        //     if (ImGui.TreeNode("Touch"))
        //     {
        //         ImGui.Text($"TouchingEntity: {touch.TouchingEntity}");
        //         ImGui.Text($"Side: {touch.Side}");
        //         ImGui.TreePop();
        //     }
        // }
        // ImGui.End();
        
        _guiRenderer.AfterLayout();
        _batch.GraphicsDevice.SetRenderTarget(null);
        _collisions.Clear();
        // _touches.Clear();
    }
}