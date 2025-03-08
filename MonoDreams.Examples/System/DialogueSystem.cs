using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Dialogue;
using MonoDreams.Examples.Message;
using MonoDreams.State;
using MonoDreams.YarnSpinner;

namespace MonoDreams.Examples.System;

public class DialogueSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly RenderTarget2D _renderTarget2D;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly DialogueRunner _dialogueRunner;
    private DialogueZoneComponent? _dialogueZone;

    public DialogueSystem(World world, RenderTarget2D renderTarget2D, GraphicsDevice graphicsDevice, DialogueRunner dialogueRunner)
    {
        _world = world;
        _renderTarget2D = renderTarget2D;
        _graphicsDevice = graphicsDevice;
        _dialogueRunner = dialogueRunner;
        _world.Subscribe(this);
    }

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void Update(GameState state)
    {
        if (_dialogueZone == null) return;
        
        // var textLine = _dialogueRunner.GetLine(_dialogueZone.YarnNodeName);
        // Objects.Dialogue.Create(_world, textLine, DEFAULT_EMOTE_TEXTURE, DEFAULT_DIALOGUE_FONT, DEFAULT_DIALOGUE_BOX_TEXTURE, _renderTarget2D, _graphicsDevice, _dialogueZone.YarnNodeName, _dialogueRunner);
    }

    [Subscribe]
    public void On(in CollisionMessage message)
    {
        if (message.Type != CollisionType.Dialogue) return;
        _dialogueZone = message.CollidingEntity.Get<DialogueZoneComponent>();
    }
}