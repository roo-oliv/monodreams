using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.YarnSpinner;
using MonoGame.Extended.BitmapFonts;
using Yarn;

namespace MonoDreams.Examples.Dialogue;

public class DialogueRunner
{
    private Yarn.Dialogue _dialogue;
    private IVariableStorage _variableStorage;
    private readonly World _world;
    private readonly BitmapFont _dialogueFont;
    private readonly Texture2D _emoteTexture;
    private readonly Texture2D _dialogueBoxTexture;
    private readonly RenderTarget2D _renderTarget;
    private readonly GraphicsDevice _graphicsDevice;
    private Entity? _currentDialogueEntity;

    public DialogueRunner(World world, BitmapFont dialogueFont, Texture2D emoteTexture, Texture2D dialogueBoxTexture,
        RenderTarget2D renderTarget,
        GraphicsDevice graphicsDevice,
        IVariableStorage? variableStorage = null)
    {
        _variableStorage = variableStorage ?? new InMemoryVariableStorage();
        _dialogue = new Yarn.Dialogue(_variableStorage);
        _world = world;
        _dialogueFont = dialogueFont;
        _emoteTexture = emoteTexture;
        _dialogueBoxTexture = dialogueBoxTexture;
        _renderTarget = renderTarget;
        _graphicsDevice = graphicsDevice;
    }
    
    protected void HandleLine(string line, string characterName)
    {
        _currentDialogueEntity = Objects.Dialogue.Create(
            _world, 
            line, 
            _emoteTexture, 
            _dialogueFont, 
            _dialogueBoxTexture, 
            _renderTarget, 
            _graphicsDevice
        );
        
        // Trigger any game state changes or events based on the dialogue
        // ...
    }
}