using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Examples.Dialogue;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.Objects;
using MonoDreams.Examples.Component;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public enum DialogueState
{
    Inactive,
    Line,
    Options,
    Complete
}

public class DialogueSystem : ISystem<GameState>, IDisposable
{
    private readonly World _world;
    private readonly DialogueRunner _dialogueRunner;
    private KeyboardState _currentKeyboardState;
    private KeyboardState _previousKeyboardState;
    private bool _isDialogueActive;
    private int _selectedOptionIndex;
    private IDisposable _collisionSubscription;
    
    public bool IsEnabled { get; set; } = true;
    
    public DialogueSystem(World world, DialogueRunner dialogueRunner, object inputState)
    {
        _world = world;
        _dialogueRunner = dialogueRunner;
        _currentKeyboardState = Keyboard.GetState();
        _previousKeyboardState = _currentKeyboardState;
        
        // Subscribe to collision events
        _collisionSubscription = _world.Subscribe<CollisionMessage>(OnCollision);
        
        // Subscribe to dialogue events
        _dialogueRunner.OnDialogueComplete += HandleDialogueComplete;
    }

    public void Dispose()
    {
        // Unsubscribe from events
        _collisionSubscription.Dispose();
        _dialogueRunner.OnDialogueComplete -= HandleDialogueComplete;
        GC.SuppressFinalize(this);
    }

    public void Update(GameState state)
    {
        // Update keyboard state
        _previousKeyboardState = _currentKeyboardState;
        _currentKeyboardState = Keyboard.GetState();
        
        if (!_isDialogueActive && _dialogueRunner.IsRunning)
        {
            _isDialogueActive = true;
        }
        else if (_isDialogueActive && !_dialogueRunner.IsRunning)
        {
            _isDialogueActive = false;
        }
        
        if (!_isDialogueActive)
        {
            // Check for player interaction with dialogue zones
            return;
        }
        
        // Handle input for dialogue
        if (_dialogueRunner.IsWaitingForOptionSelection)
        {
            HandleOptionSelection();
        }
        else
        {
            HandleDialogueContinuation();
        }
    }
    
    private void OnCollision(in CollisionMessage message)
    {
        // Check if player collided with a dialogue zone
        if (message.Type == CollisionType.Dialogue)
        {
            // Determine which entity is the player and which is the zone
            Entity playerEntity, zoneEntity;
            
            // Check if the base entity has a DialogueZoneComponent
            if (message.BaseEntity.Has<DialogueZoneComponent>())
            {
                zoneEntity = message.BaseEntity;
                playerEntity = message.CollidingEntity;
            }
            else
            {
                zoneEntity = message.CollidingEntity;
                playerEntity = message.BaseEntity;
            }
            
            HandleDialogueZoneCollision(playerEntity, zoneEntity);
        }
    }
    
    private void HandleDialogueZoneCollision(Entity playerEntity, Entity dialogueZoneEntity)
    {
        if (_isDialogueActive) return;
        
        // Check if player pressed the interaction key
        if (IsKeyPressed(Keys.E))
        {
            // Try to get DialogueZone component first
            if (dialogueZoneEntity.Has<DialogueZone>())
            {
                var dialogueZone = dialogueZoneEntity.Get<DialogueZone>();
                
                // Start dialogue with the specified node
                string nodeName = "HelloWorld"; // Default node name
                if (!string.IsNullOrEmpty(dialogueZone.NodeName))
                {
                    nodeName = dialogueZone.NodeName;
                }
                
                Console.WriteLine($"Starting dialogue with node: {nodeName}");
                _dialogueRunner.StartDialogue(nodeName);
            }
            // Try DialogueZoneComponent as fallback
            else if (dialogueZoneEntity.Has<DialogueZoneComponent>())
            {
                var dialogueZoneComponent = dialogueZoneEntity.Get<DialogueZoneComponent>();
                
                // Start dialogue with the specified node
                string nodeName = "HelloWorld"; // Default node name
                if (!string.IsNullOrEmpty(dialogueZoneComponent.YarnNodeName))
                {
                    nodeName = dialogueZoneComponent.YarnNodeName;
                }
                
                Console.WriteLine($"Starting dialogue with node: {nodeName}");
                _dialogueRunner.StartDialogue(nodeName);
            }
        }
    }
    
    private void HandleOptionSelection()
    {
        // Handle up/down navigation
        if (IsKeyPressed(Keys.Up) || IsKeyPressed(Keys.W))
        {
            _selectedOptionIndex = Math.Max(0, _selectedOptionIndex - 1);
        }
        else if (IsKeyPressed(Keys.Down) || IsKeyPressed(Keys.S))
        {
            _selectedOptionIndex = Math.Min(3, _selectedOptionIndex + 1); // Assuming max 4 options
        }
        
        // Handle selection
        if (IsKeyPressed(Keys.Space) || IsKeyPressed(Keys.Enter))
        {
            _dialogueRunner.SelectOption(_selectedOptionIndex);
        }
    }
    
    private void HandleDialogueContinuation()
    {
        if (IsKeyPressed(Keys.Space) || IsKeyPressed(Keys.Enter))
        {
            _dialogueRunner.Continue();
        }
        else if (IsKeyPressed(Keys.Escape))
        {
            // TODO: Add a way to skip dialogue
        }
    }
    
    private void HandleDialogueComplete()
    {
        _isDialogueActive = false;
        _selectedOptionIndex = 0;
    }
    
    private bool IsKeyPressed(Keys key)
    {
        return _currentKeyboardState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);
    }
}