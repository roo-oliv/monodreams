using System;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Content;
using MonoDreams.State;

namespace MonoDreams.Screen;

public interface IGameScreen : IDisposable
{
    public ISystem<GameState> System { get; }
    public void Load(ContentManager content);
}