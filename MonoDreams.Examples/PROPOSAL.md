# RFC 001: Dialogue System Redesign

## Abstract

This RFC proposes a complete redesign of the dialogue system for MonoDreams using Entity-Component-System (ECS) architecture. The goal is to create a modular, extensible dialogue system that integrates seamlessly with the existing ECS framework while providing intuitive interfaces for game designers and developers.

## Motivation

The current dialogue system implementation has several limitations:
1. It doesn't fully embrace the ECS architecture
2. It mixes concerns between rendering, input handling, and dialogue state management
3. It's tightly coupled to specific UI implementations
4. It lacks a clear separation between the dialogue content and its presentation

By redesigning the dialogue system with a clean ECS approach, we can improve modularity, testability, and extensibility while making it easier for developers to integrate dialogues into their games.

## General Design

The proposed dialogue system follows these design principles:
1. **Separation of concerns** - Clear distinction between dialogue content, state management, and presentation
2. **Data-driven** - Dialogue content stored in a format that's easy to author and modify
3. **Event-based communication** - Components communicate via events to maintain loose coupling
4. **Progressive disclosure** - Simple interfaces for common use cases, with extensibility for advanced features

### High-Level Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│  Dialogue Data  │───►│  Dialogue State │───►│   Presentation  │
│   Components    │    │    Components   │    │   Components    │
└─────────────────┘    └─────────────────┘    └─────────────────┘
        ▲                      │                      │
        │                      ▼                      ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│ Content Loading │    │ State Management│    │      Input      │
│     Systems     │    │     Systems     │    │     Systems     │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

### Core Components

1. **DialogueContent** - Contains the actual dialogue data (text, choices, etc.)
2. **DialogueState** - Manages the current state of a dialogue (active node, choices, etc.)
3. **DialogueTrigger** - Identifies entities that can trigger dialogues
4. **DialoguePresenter** - Handles the visual presentation of dialogues

### Core Systems

1. **DialogueContentSystem** - Loads and manages dialogue content
2. **DialogueStateSystem** - Updates dialogue state based on inputs and triggers
3. **DialoguePresentationSystem** - Renders the dialogue UI
4. **DialogueInputSystem** - Processes player input for dialogue advancement and choices

### Message Types

1. **DialogueStartMessage** - Signals the start of a dialogue
2. **DialogueAdvanceMessage** - Signals advancement to the next dialogue node
3. **DialogueChoiceMessage** - Signals a choice selection in a dialogue
4. **DialogueEndMessage** - Signals the end of a dialogue

## Detailed Design

### Components

#### DialogueContent Component

```csharp
public class DialogueContent
{
    // The source program containing dialogue nodes
    public YarnProgram Program { get; set; }
    
    // Optional default starting node
    public string DefaultStartNode { get; set; } = "Start";
    
    // Localization settings
    public string Language { get; set; } = "en";
    
    // Dynamic variable evaluation (for conditions and text substitution)
    public IVariableStorage VariableStorage { get; set; }
}
```

#### DialogueState Component

```csharp
public enum DialogueNodeType
{
    Line,
    Options,
    Command,
    End
}

public class DialogueState
{
    // Whether dialogue is currently active
    public bool IsActive { get; set; } = false;
    
    // The current node being processed
    public string CurrentNode { get; set; }
    
    // The type of the current node
    public DialogueNodeType CurrentNodeType { get; set; }
    
    // Current text to display
    public string CurrentText { get; set; }
    
    // Current speaker name (if any)
    public string CurrentSpeaker { get; set; }
    
    // Options for choice nodes
    public List<string> CurrentOptions { get; set; } = new();
    
    // Currently selected option index
    public int SelectedOptionIndex { get; set; } = 0;
    
    // Whether waiting for player input
    public bool WaitingForInput { get; set; } = false;
}
```

#### DialogueTrigger Component

```csharp
public enum TriggerType
{
    Proximity,
    Interaction,
    Automatic
}

public class DialogueTrigger
{
    // How this dialogue is triggered
    public TriggerType Type { get; set; } = TriggerType.Interaction;
    
    // Which node to start dialogue from
    public string StartNode { get; set; } = "Start";
    
    // Interaction key (for Interaction trigger type)
    public Keys InteractionKey { get; set; } = Keys.E;
    
    // Trigger radius (for Proximity trigger type)
    public float ProximityRadius { get; set; } = 2.0f;
    
    // Whether this trigger can be used multiple times
    public bool OneTimeOnly { get; set; } = false;
    
    // Whether this trigger has been used
    public bool HasBeenUsed { get; set; } = false;
}
```

#### DialoguePresenter Component

```csharp
public class DialoguePresenter
{
    // Visual presentation settings
    public BitmapFont Font { get; set; }
    public Texture2D DialogueBoxTexture { get; set; }
    public Texture2D PortraitTexture { get; set; }
    
    // Layout settings
    public Rectangle DialogueBoxBounds { get; set; }
    public Rectangle PortraitBounds { get; set; }
    
    // Text display settings
    public Color TextColor { get; set; } = Color.White;
    public float TextSpeed { get; set; } = 1.0f;
    
    // Whether text should be revealed gradually
    public bool RevealTextGradually { get; set; } = true;
    
    // How much text has been revealed (0.0-1.0)
    public float TextRevealProgress { get; set; } = 0.0f;
}
```

### Systems

#### DialogueContentSystem

Responsible for loading dialogue content and making it available to the dialogue state system.

```csharp
public class DialogueContentSystem : AEntitySetSystem<GameState>
{
    private World _world;
    
    public DialogueContentSystem(World world) 
        : base(world.GetEntities().With<DialogueContent>().AsSet())
    {
        _world = world;
    }
    
    protected override void Update(GameState state, in Entity entity)
    {
        // Only process entities that have content but haven't been initialized
        var content = entity.Get<DialogueContent>();
        
        // If the entity also has a state component, initialize from content
        if (entity.Has<DialogueState>())
        {
            // Initialize dialogue state from content if needed
        }
    }
}
```

#### DialogueStateSystem

Updates the state of active dialogues based on input events and dialogue content.

```csharp
public class DialogueStateSystem : AEntitySetSystem<GameState>
{
    private World _world;
    
    public DialogueStateSystem(World world)
        : base(world.GetEntities().With<DialogueState>().With<DialogueContent>().AsSet())
    {
        _world = world;
        
        // Subscribe to dialogue events
        world.Subscribe<DialogueAdvanceMessage>(OnAdvanceDialogue);
        world.Subscribe<DialogueChoiceMessage>(OnDialogueChoice);
    }
    
    protected override void Update(GameState state, in Entity entity)
    {
        var dialogueState = entity.Get<DialogueState>();
        
        // Only process active dialogues
        if (!dialogueState.IsActive) return;
        
        // Handle state transitions based on current node type
        switch (dialogueState.CurrentNodeType)
        {
            case DialogueNodeType.Line:
                // Handle line nodes
                break;
            case DialogueNodeType.Options:
                // Handle option nodes
                break;
            case DialogueNodeType.Command:
                // Handle command nodes
                break;
            case DialogueNodeType.End:
                // Handle end nodes
                dialogueState.IsActive = false;
                _world.Publish(new DialogueEndMessage(entity));
                break;
        }
    }
    
    private void OnAdvanceDialogue(in DialogueAdvanceMessage message)
    {
        // Advance dialogue to next node
    }
    
    private void OnDialogueChoice(in DialogueChoiceMessage message)
    {
        // Process choice selection
    }
}
```

#### DialogueTriggerSystem

Detects when dialogues should be triggered based on player proximity, interaction, or other conditions.

```csharp
public class DialogueTriggerSystem : AEntitySetSystem<GameState>
{
    private World _world;
    private readonly Entity _player;
    
    public DialogueTriggerSystem(World world, Entity player)
        : base(world.GetEntities().With<DialogueTrigger>().With<Position>().AsSet())
    {
        _world = world;
        _player = player;
    }
    
    protected override void Update(GameState state, in Entity entity)
    {
        var trigger = entity.Get<DialogueTrigger>();
        
        // Skip if the trigger is one-time-only and already used
        if (trigger.OneTimeOnly && trigger.HasBeenUsed) return;
        
        // Check if trigger conditions are met
        bool shouldTrigger = false;
        
        switch (trigger.Type)
        {
            case TriggerType.Proximity:
                // Check if player is within proximity radius
                if (entity.Has<Position>() && _player.Has<Position>())
                {
                    var triggerPos = entity.Get<Position>().Current;
                    var playerPos = _player.Get<Position>().Current;
                    
                    float distance = Vector2.Distance(triggerPos, playerPos);
                    shouldTrigger = distance <= trigger.ProximityRadius;
                }
                break;
                
            case TriggerType.Interaction:
                // Check if player is close and interaction key is pressed
                if (entity.Has<Position>() && _player.Has<Position>())
                {
                    var triggerPos = entity.Get<Position>().Current;
                    var playerPos = _player.Get<Position>().Current;
                    
                    float distance = Vector2.Distance(triggerPos, playerPos);
                    shouldTrigger = distance <= trigger.ProximityRadius && 
                                    Keyboard.GetState().IsKeyDown(trigger.InteractionKey);
                }
                break;
                
            case TriggerType.Automatic:
                // Always trigger
                shouldTrigger = true;
                break;
        }
        
        if (shouldTrigger)
        {
            // Mark trigger as used if one-time-only
            if (trigger.OneTimeOnly)
            {
                trigger.HasBeenUsed = true;
            }
            
            // Create or get dialogue entity
            Entity dialogueEntity;
            if (entity.Has<DialogueContent>())
            {
                dialogueEntity = entity;
            }
            else
            {
                // Create a new dialogue entity
                dialogueEntity = _world.CreateEntity();
                dialogueEntity.Set(new DialogueContent());
                dialogueEntity.Set(new DialogueState());
            }
            
            // Publish start message
            _world.Publish(new DialogueStartMessage(
                dialogueEntity,
                trigger.StartNode
            ));
        }
    }
}
```

#### DialoguePresentationSystem

Handles the visual presentation of active dialogues.

```csharp
public class DialoguePresentationSystem : AEntitySetSystem<GameState>
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    
    public DialoguePresentationSystem(World world, GraphicsDevice graphicsDevice)
        : base(world.GetEntities().With<DialogueState>().With<DialoguePresenter>().AsSet())
    {
        _graphicsDevice = graphicsDevice;
        _spriteBatch = new SpriteBatch(graphicsDevice);
    }
    
    protected override void Update(GameState state, in Entity entity)
    {
        var dialogueState = entity.Get<DialogueState>();
        var presenter = entity.Get<DialoguePresenter>();
        
        // Only render active dialogues
        if (!dialogueState.IsActive) return;
        
        // Update text reveal animation
        if (presenter.RevealTextGradually && presenter.TextRevealProgress < 1.0f)
        {
            presenter.TextRevealProgress += state.Time.DeltaTime * presenter.TextSpeed;
            if (presenter.TextRevealProgress > 1.0f)
            {
                presenter.TextRevealProgress = 1.0f;
            }
        }
    }
    
    public void Draw(GameState state)
    {
        _spriteBatch.Begin();
        
        foreach (var entity in GetEntities())
        {
            var dialogueState = entity.Get<DialogueState>();
            var presenter = entity.Get<DialoguePresenter>();
            
            // Only render active dialogues
            if (!dialogueState.IsActive) continue;
            
            // Draw dialogue box
            _spriteBatch.Draw(
                presenter.DialogueBoxTexture, 
                presenter.DialogueBoxBounds, 
                Color.White);
            
            // Draw portrait if available
            if (presenter.PortraitTexture != null)
            {
                _spriteBatch.Draw(
                    presenter.PortraitTexture,
                    presenter.PortraitBounds,
                    Color.White);
            }
            
            // Draw text (with reveal animation if enabled)
            string textToShow = dialogueState.CurrentText;
            if (presenter.RevealTextGradually && presenter.TextRevealProgress < 1.0f)
            {
                int charCount = (int)(textToShow.Length * presenter.TextRevealProgress);
                textToShow = textToShow.Substring(0, charCount);
            }
            
            _spriteBatch.DrawString(
                presenter.Font,
                textToShow,
                new Vector2(presenter.DialogueBoxBounds.X + 20, presenter.DialogueBoxBounds.Y + 20),
                presenter.TextColor);
            
            // Draw options if any
            if (dialogueState.CurrentNodeType == DialogueNodeType.Options)
            {
                for (int i = 0; i < dialogueState.CurrentOptions.Count; i++)
                {
                    Color optionColor = i == dialogueState.SelectedOptionIndex 
                        ? Color.Yellow 
                        : Color.White;
                    
                    _spriteBatch.DrawString(
                        presenter.Font,
                        dialogueState.CurrentOptions[i],
                        new Vector2(
                            presenter.DialogueBoxBounds.X + 40, 
                            presenter.DialogueBoxBounds.Y + 80 + i * 30),
                        optionColor);
                }
            }
            
            // Draw input prompt
            if (dialogueState.WaitingForInput)
            {
                _spriteBatch.DrawString(
                    presenter.Font,
                    "Press Space to continue",
                    new Vector2(
                        presenter.DialogueBoxBounds.Right - 200,
                        presenter.DialogueBoxBounds.Bottom - 30),
                    Color.LightGray);
            }
        }
        
        _spriteBatch.End();
    }
}
```

#### DialogueInputSystem

Processes player input for dialogue interaction.

```csharp
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
```

### Messages

#### DialogueStartMessage

```csharp
public readonly struct DialogueStartMessage
{
    public readonly Entity DialogueEntity;
    public readonly string StartNode;
    
    public DialogueStartMessage(Entity dialogueEntity, string startNode)
    {
        DialogueEntity = dialogueEntity;
        StartNode = startNode;
    }
}
```

#### DialogueAdvanceMessage

```csharp
public readonly struct DialogueAdvanceMessage
{
    public readonly Entity DialogueEntity;
    
    public DialogueAdvanceMessage(Entity dialogueEntity)
    {
        DialogueEntity = dialogueEntity;
    }
}
```

#### DialogueChoiceMessage

```csharp
public readonly struct DialogueChoiceMessage
{
    public readonly Entity DialogueEntity;
    public readonly int ChoiceIndex;
    
    public DialogueChoiceMessage(Entity dialogueEntity, int choiceIndex)
    {
        DialogueEntity = dialogueEntity;
        ChoiceIndex = choiceIndex;
    }
}
```

#### DialogueEndMessage

```csharp
public readonly struct DialogueEndMessage
{
    public readonly Entity DialogueEntity;
    
    public DialogueEndMessage(Entity dialogueEntity)
    {
        DialogueEntity = dialogueEntity;
    }
}
```

## Integration with Existing Systems

### Collision System Integration

The dialogue system integrates with the existing collision system to detect when players enter dialogue triggers:

```
┌────────────────┐    ┌─────────────────┐    ┌────────────────┐
│  BoxCollider   │───►│ CollisionMessage│───►│ DialogueTrigger│
│   Component    │    │                 │    │   Component    │
└────────────────┘    └─────────────────┘    └────────────────┘
                                                     │
                                                     ▼
                                             ┌────────────────┐
                                             │DialogueStartMsg│
                                             │                │
                                             └────────────────┘
```

When a collision is detected between a player and an entity with a `DialogueTrigger` component, the collision system will publish a `CollisionMessage`. The `DialogueTriggerSystem` subscribes to these messages and checks if the collision should trigger a dialogue.

### Level Editor Integration

The dialogue system is designed to work seamlessly with the upcoming level editor:

```
┌────────────────┐    ┌─────────────────┐    ┌────────────────┐
│  Level Editor  │───►│ Dialogue Trigger│───►│ Dialogue Asset │
│                │    │     Zone        │    │                │
└────────────────┘    └─────────────────┘    └────────────────┘
```

Level designers can place dialogue trigger zones in the level, assign a dialogue asset (Yarn script), and configure trigger conditions (proximity, interaction, etc.). The dialogue system will automatically load and play the dialogue when triggered.

## Usage Examples

### Creating an NPC with dialogue

```csharp
// Create an NPC entity
var npc = world.CreateEntity();

// Add position component
npc.Set(new Position(new Vector2(100, 100)));

// Add collision component for trigger detection
npc.Set(new BoxCollider(new Rectangle(0, 0, 32, 32)));

// Add dialogue trigger component
npc.Set(new DialogueTrigger
{
    Type = TriggerType.Interaction,
    StartNode = "NPC_Greeting",
    ProximityRadius = 3.0f,
    InteractionKey = Keys.E
});

// Add dialogue content component
npc.Set(new DialogueContent
{
    Program = Content.Load<YarnProgram>("Dialogues/npc_dialogue"),
    DefaultStartNode = "NPC_Greeting",
    Language = "en"
});

// Add visual representation (not part of dialogue system)
npc.Set(new Sprite
{
    Texture = Content.Load<Texture2D>("Characters/npc"),
    SourceRectangle = new Rectangle(0, 0, 32, 32)
});
```

### Creating a dialogue trigger zone

```csharp
// Create a trigger zone entity
var triggerZone = world.CreateEntity();

// Add position component
triggerZone.Set(new Position(new Vector2(200, 200)));

// Add collision component for trigger detection
triggerZone.Set(new BoxCollider(new Rectangle(0, 0, 64, 64), passive: true));

// Add dialogue trigger component
triggerZone.Set(new DialogueTrigger
{
    Type = TriggerType.Proximity,
    StartNode = "Area_Discovery",
    ProximityRadius = 5.0f,
    OneTimeOnly = true
});

// Add dialogue content component
triggerZone.Set(new DialogueContent
{
    Program = Content.Load<YarnProgram>("Dialogues/area_dialogue"),
    DefaultStartNode = "Area_Discovery",
    Language = "en"
});
```

### Adding the dialogue systems to the game

```csharp
// Assuming you have a Game class
protected override void Initialize()
{
    base.Initialize();
    
    // Create the ECS world
    var world = new World();
    
    // Create the player entity
    var player = CreatePlayer(world);
    
    // Add dialogue systems
    _systems = new SystemCollection(new[] 
    {
        // Existing systems...
        
        // Dialogue systems
        new DialogueContentSystem(world),
        new DialogueTriggerSystem(world, player),
        new DialogueStateSystem(world),
        new DialogueInputSystem(world),
        new DialoguePresentationSystem(world, GraphicsDevice)
    });
}

protected override void Update(GameTime gameTime)
{
    var state = new GameState
    {
        Time = new TimeState
        {
            DeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds,
            TotalTime = (float)gameTime.TotalGameTime.TotalSeconds
        }
    };
    
    _systems.Update(state);
    
    base.Update(gameTime);
}

protected override void Draw(GameTime gameTime)
{
    base.Draw(gameTime);
    
    // Get the dialogue presentation system
    var dialoguePresentationSystem = _systems.Systems
        .OfType<DialoguePresentationSystem>()
        .FirstOrDefault();
    
    // Draw dialogues on top of the game
    dialoguePresentationSystem?.Draw(new GameState());
}
```

## Benefits and Trade-offs

### Benefits

1. **Modularity** - The system is composed of distinct components and systems that can be easily swapped or extended
2. **ECS Integration** - Fully embraces the ECS architecture, making it consistent with the rest of the engine
3. **Separation of Concerns** - Clearly separates dialogue content, state, triggers, and presentation
4. **Extensibility** - New dialogue features can be added by creating new components or systems
5. **Designer-Friendly** - Easy to integrate with level editors and other design tools

### Trade-offs

1. **Complexity** - The ECS approach introduces more boilerplate compared to a simpler OOP design
2. **Performance** - Multiple systems and message passing may have a small performance overhead
3. **Learning Curve** - Developers need to understand ECS concepts to work with the system

## Implementation Plan

1. **Phase 1:** Core components and systems
   - Implement DialogueContent and DialogueState components
   - Implement DialogueContentSystem and DialogueStateSystem
   - Add basic message types

2. **Phase 2:** Integration with external dialogue system
   - Add Yarn Spinner integration
   - Implement variable storage and script loading

3. **Phase 3:** Triggers and input handling
   - Implement DialogueTrigger component
   - Implement DialogueTriggerSystem
   - Implement DialogueInputSystem

4. **Phase 4:** Presentation layer
   - Implement DialoguePresenter component
   - Implement DialoguePresentationSystem
   - Add text animation and styling features

5. **Phase 5:** Level editor integration
   - Add dialogue trigger zone tools to level editor
   - Create visual dialogue flow editor

## Conclusion

This RFC proposes a comprehensive dialogue system redesign using ECS architecture that will integrate well with the existing MonoDreams engine. The system is designed to be modular, extensible, and easy to use for both developers and game designers. By clearly separating dialogue content, state, triggers, and presentation, the system provides flexibility while maintaining clean architecture.

The proposed system supports both programmatic dialogue creation and level editor integration, making it suitable for a wide range of game development workflows. The event-based communication between components ensures loose coupling and makes the system more testable and maintainable. 