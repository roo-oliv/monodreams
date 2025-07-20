using System;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Content;
using MonoDreams.State;

namespace MonoDreams.Screen;

public interface IGameScreen : IDisposable
{
    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public void Load(ScreenController screenController, ContentManager content);
}