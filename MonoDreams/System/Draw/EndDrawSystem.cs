using System;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

public class EndDrawSystem(SpriteBatch batch) : ISystem<GameState>
{
    public void Update(GameState state) => batch.End();

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}