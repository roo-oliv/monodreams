using System;
using System.Diagnostics;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class HudSystem : AEntitySetSystem<GameState>
{
    private readonly ResolutionIndependentRenderer _renderer;
    private readonly Camera _camera;
    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;
    private (float value, float time) _minFps = (0f, -1000f);
    private (float value, float time) _maxFps = (0f, -1000f);
    private (float value, float time) _minFrametime = (0f, -1000f);
    private (float value, float time) _maxFrametime = (0f, -1000f);
    private (float value, float time) _maxHVel = (0f, -1000f);
    private (float value, float time) _minHVel = (0f, -1000f);
    private Stopwatch stopwatch = new();
    private const int Scale = 10;

    public HudSystem(ResolutionIndependentRenderer renderer, Camera camera, SpriteBatch batch, SpriteFont font, World world)
        : base(world.GetEntities().With<PlayerInput>().AsSet(), true)
    {
        _renderer = renderer;
        _camera = camera;
        _batch = batch;
        _font = font;
    }

    private void DrawText(string text, float x, float y, Color foreColor, float scale)
    {
        _batch.DrawString(
            _font,
            text,
            new Vector2(x, y),
            foreColor,
            0.0f,
            new Vector2((_font.MeasureString(text).X / 2 * scale - _font.MeasureString(text).X / 2), (_font.MeasureString(text).Y / 2 * scale - _font.MeasureString(text).Y / 2)),
            scale * Scale,
            SpriteEffects.None,
            0.0f);
    }

    protected override void PreUpdate(GameState state)
    {
        // _renderer.BeginDraw();
        _batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearWrap,
            DepthStencilState.None,
            RasterizerState.CullNone, 
            null,
            _camera.GetViewTransformationMatrix());
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var body = ref entity.Get<DynamicBody>();
        ref var position = ref entity.Get<Position>();
        ref var playerInput = ref entity.Get<PlayerInput>();
        var x = _camera.Position.X + 120 * Scale;
        var y = _camera.Position.Y - 105 * Scale;
        var textSlot = 0;
        var spacing = 4 * Scale;
        var scale = Vector2.One;
        var lastHVelWindow = 3f;
        
        if (!stopwatch.IsRunning)
        {
            stopwatch.Start();
        }
        else
        {
            stopwatch.Stop();
            double frameTime = stopwatch.Elapsed.TotalSeconds;
            var fps = (float) (1 / frameTime);
            _minFps = fps < _minFps.value || state.TotalTime - _minFps.time > 10 ? (fps, state.TotalTime) : _minFps;
            _maxFps = fps > _maxFps.value || state.TotalTime - _maxFps.time > 10 ? (fps, state.TotalTime) : _maxFps;
            _minFrametime = frameTime < _minFrametime.value || state.TotalTime - _minFrametime.time > 10 ? ((float) frameTime, state.TotalTime) : _minFrametime;
            _maxFrametime = frameTime > _maxFrametime.value || state.TotalTime - _maxFrametime.time > 10 ? ((float) frameTime, state.TotalTime) : _maxFrametime;
            
            DrawText("FPS: " + fps, x, y + textSlot++ * spacing, Color.White, 0.05f);
            DrawText("Min FPS (10s): " + _minFps.value, x, y + textSlot++ * spacing, Color.White, 0.05f);
            DrawText("Max FPS (10s): " + _maxFps.value, x, y + textSlot++ * spacing, Color.White, 0.05f);
            DrawText("Frametime: " + state.Time, x, y + textSlot++ * spacing, Color.White, 0.05f);
            DrawText("Min Frametime (10s): " + _minFrametime.value, x, y + textSlot++ * spacing, Color.White, 0.05f);
            DrawText("Max Frametime (10s): " + _maxFrametime.value, x, y + textSlot++ * spacing, Color.White, 0.05f);
            
            stopwatch.Reset();
            stopwatch.Start();
        }
        
        // var fps = 1 / state.Time;
        // _minFps = fps < _minFps.value || state.TotalTime - _minFps.time > 60 ? (fps, state.TotalTime) : _minFps;
        // _maxFps = fps > _maxFps.value || state.TotalTime - _maxFps.time > 60 ? (fps, state.TotalTime) : _maxFps;
        // _minFrametime = state.Time < _minFrametime.value || state.TotalTime - _minFrametime.time > 60 ? (state.Time, state.TotalTime) : _minFrametime;
        // _maxFrametime = state.Time > _maxFrametime.value || state.TotalTime - _maxFrametime.time > 60 ? (state.Time, state.TotalTime) : _maxFrametime;
        // _batch.DrawString(_font, "FPS: " + fps, new Vector2(x, y + textSlot++ * spacing), Color.White);
        // _batch.DrawString(_font, "Min FPS (60s): " + _minFps.value, new Vector2(x, y + textSlot++ * spacing), Color.White);
        // _batch.DrawString(_font, "Max FPS (60s): " + _maxFps.value, new Vector2(x, y + textSlot++ * spacing), Color.White);
        // _batch.DrawString(_font, "Frametime: " + state.Time, new Vector2(x, y + textSlot++ * spacing), Color.White);
        // _batch.DrawString(_font, "Min Frametime (60s): " + _minFrametime.value, new Vector2(x, y + textSlot++ * spacing), Color.White);
        // _batch.DrawString(_font, "Max Frametime (60s): " + _maxFrametime.value, new Vector2(x, y + textSlot++ * spacing), Color.White);
        // _batch.DrawString(_font, "Time: " + state.Time, new Vector2(XTextPosition, 150), Color.White);
        // _batch.DrawString(_font, "LastTime: " + state.LastTime, new Vector2(XTextPosition, 220), Color.White);
        DrawText("TotalTime: " + state.TotalTime, x, y + textSlot++ * spacing, Color.White, 0.05f);
        DrawText("CurrentLocation: " + position.CurrentLocation, x, y + textSlot++ * spacing, Color.White, 0.05f);
        DrawText("LastLocation: " + position.LastLocation, x, y + textSlot++ * spacing, Color.White, 0.05f);
        var velocity = (position.CurrentLocation - position.LastLocation) / state.Time;
        _minHVel = velocity.X < _minHVel.value || state.TotalTime - _minHVel.time > lastHVelWindow ? (velocity.X, state.TotalTime) : _minHVel;
        _maxHVel = velocity.X > _maxHVel.value || state.TotalTime - _maxHVel.time > lastHVelWindow ? (velocity.X, state.TotalTime) : _maxHVel;
        DrawText("Calculated Velocity: " + velocity, x, y + textSlot++ * spacing, Color.White, 0.05f);
        DrawText("Min HVelocity (3s window): " + _minHVel.value, x, y + textSlot++ * spacing, Color.White, 0.05f);
        DrawText("Max HVelocity (3s window): " + _maxHVel.value, x, y + textSlot++ * spacing, Color.White, 0.05f);
        DrawText("Player Gravity: " + body.Gravity, x, y + textSlot++ * spacing, Color.White, 0.05f);
        DrawText("IsRiding: " + body.IsRiding, x, y + textSlot++ * spacing, Color.White, 0.05f);
        DrawText("IsJumping: " + body.IsJumping, x, y + textSlot++ * spacing, Color.White, 0.05f);
        DrawText("playerInput.Jump.Active: " + playerInput.Jump.Active, x, y + textSlot++ * spacing, Color.White, 0.05f);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
}