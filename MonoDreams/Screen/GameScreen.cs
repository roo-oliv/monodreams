using System;
using DefaultEcs.System;
using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using MonoDreams.State;

namespace MonoDreams.Screen;

public interface IGameScreen : IDisposable
{
    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public void Update(GameTime gameTime);
    public void Draw(GameTime gameTime);
    public void Load(ScreenController screenController, ContentManager content);
}