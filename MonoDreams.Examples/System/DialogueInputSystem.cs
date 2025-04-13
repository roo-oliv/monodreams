using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Message;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class DialogueInputSystem : AEntitySetSystem<GameState>
{
    private readonly World _world;
    private KeyboardState _currentKeyboardState;
    private KeyboardState _previousKeyboardState;
    
    public DialogueInputSystem(World world)
        : base(world.GetEntities().With<DialogueState>().AsSet())
    {
        _world = world;
        _currentKeyboardState = Keyboard.GetState();
        _previousKeyboardState = _currentKeyboardState;
    }
    
    protected override void PreUpdate(GameState state)
    {
        _previousKeyboardState = _currentKeyboardState;
        _currentKeyboardState = Keyboard.GetState();
    }
    
    protected override void Update(GameState state, in Entity entity)
    {
        var dialogueState = entity.Get<DialogueState>();
        
        // Only process active dialogues waiting for input
        if (!dialogueState.IsActive || !dialogueState.WaitingForInput) return;
        
        // Handle different input based on node type
        switch (dialogueState.CurrentNodeType)
        {
            case DialogueNodeType.Line:
                // Advance dialogue when space is pressed
                if (IsKeyPressed(Keys.Space) || IsKeyPressed(Keys.Enter))
                {
                    _world.Publish(new DialogueAdvanceMessage(entity));
                }
                break;
                
            case DialogueNodeType.Options:
                // Navigate options with arrow keys
                if (IsKeyPressed(Keys.Up) || IsKeyPressed(Keys.W))
                {
                    dialogueState.SelectedOptionIndex = Math.Max(0, dialogueState.SelectedOptionIndex - 1);
                }
                else if (IsKeyPressed(Keys.Down) || IsKeyPressed(Keys.S))
                {
                    dialogueState.SelectedOptionIndex = Math.Min(
                        dialogueState.CurrentOptions.Count - 1, 
                        dialogueState.SelectedOptionIndex + 1);
                }
                
                // Select option with space or enter
                if (IsKeyPressed(Keys.Space) || IsKeyPressed(Keys.Enter))
                {
                    _world.Publish(new DialogueChoiceMessage(
                        entity, 
                        dialogueState.SelectedOptionIndex));
                }
                break;
        }
    }
    
    private bool IsKeyPressed(Keys key)
    {
        return _currentKeyboardState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);
    }
}
