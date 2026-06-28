# RFC: Modular YarnSpinner Integration for DefaultECS

**Status:** Proposed
**Author:** Gemini
**Date:** 2025-04-06

## Abstract

This document proposes a standard, modular approach for integrating the YarnSpinner narrative scripting language into a DefaultECS-based game engine. It defines core ECS components, implementations of YarnSpinner interfaces (`DialogueUIBehaviour`, `VariableStorageBehaviour`), and a central system (`YarnDialogueSystem`). Crucially, it decouples game-specific command handling via an `ICommandHandler` interface, allowing developers to extend dialogue capabilities without modifying the core integration module.

## Motivation

YarnSpinner is a popular and powerful tool for writing interactive dialogue. Integrating it effectively into an ECS architecture requires careful consideration of state management, UI interaction, variable storage, and command execution. A monolithic approach embedding game logic directly into the integration is brittle and hard to reuse.

This proposal aims to:

1.  **Standardize Integration:** Provide a clear pattern for using YarnSpinner with DefaultECS.
2.  **Promote Modularity:** Separate the core YarnSpinner runtime logic from game-specific concerns like UI rendering (handled by a separate mechanism, see RFC: Modular Dialogue UI Rendering) and command implementation.
3.  **Enable Extensibility:** Allow developers to easily add custom commands (e.g., `<<give_item>>`, `<<play_sound>>`, `<<start_quest>>`) triggered from Yarn scripts without altering the core Yarn integration systems.
4.  **Leverage ECS:** Utilize ECS components for managing dialogue state and associating dialogue behaviour with entities.

## Specification

The integration consists of the following parts:

1.  **Core Components:**
    * `YarnInteractableComponent` (Struct): Attached to entities (e.g., NPCs) that can initiate dialogues. Stores the `YarnProjectAssetName` and a `DefaultStartNode`.
    * `DialogueTriggerZoneComponent` (Struct): Attached to trigger entities. Stores a `TargetYarnNode` and a reference (`Entity`) to the `TargetNpcEntity` associated with the dialogue.
    * `DialogueRunnerComponent` (Class): Added *temporarily* to the entity managing an *active* dialogue session (typically the NPC/owner). Holds the `Yarn.Unity.DialogueRunner` instance, references to the specific `EcsDialogueUI`, `EcsVariableStorage`, and `EcsCommandDispatcher` instances for that session, and state like the `CurrentNode`.

2.  **YarnSpinner Interface Implementations:**
    * `EcsVariableStorage` (Class, inherits `VariableStorageBehaviour`): Implements Yarn's variable storage interface. Uses a simple dictionary by default but can be extended to interact with ECS components for variable access. A new instance can be created per dialogue session for isolated storage, or a shared instance could be used.
    * `EcsDialogueUI` (Class, inherits `DialogueUIBehaviour`): Implements Yarn's UI interface. It **does not draw directly**. Instead, it receives lines, options, and commands from the `DialogueRunner` and updates the state of a `DialogueUIComponent` on a designated UI entity. It signals back to the `DialogueRunner` (via callbacks) when the player advances lines or selects options, based on input processed by `YarnDialogueSystem`. It delegates command execution to an `EcsCommandDispatcher`.

3.  **Command Handling Abstraction:**
    * `ICommandHandler` (Interface): Defines the contract for game-specific command handlers. Requires implementers to provide `CommandName` (string) and `HandleCommand(params...)` (bool method).
    * `EcsCommandDispatcher` (Class): Created per dialogue session. Takes a collection of `ICommandHandler` implementations. Its `Dispatch` method finds the appropriate handler based on the command name and invokes its `HandleCommand` method, passing necessary context (parameters, owner entity, world, content manager).

4.  **Core System:**
    * `YarnDialogueSystem` (System, `AEntitySystem`):
        * Manages entities with `DialogueRunnerComponent`.
        * Provides a public `StartDialogue` method:
            * Takes owner entity, project asset name, start node, UI entity.
            * Loads the Yarn Project/Program (implementation specific).
            * Creates instances of `EcsVariableStorage`, `EcsDialogueUI`, and `EcsCommandDispatcher` (passing registered `ICommandHandler`s to the dispatcher).
            * Creates and configures a `Yarn.Unity.DialogueRunner`.
            * Adds a `DialogueRunnerComponent` to the owner entity.
            * Calls `runner.StartDialogue()`.
        * Provides a `StopDialogue` method.
        * In its `Update` method:
            * Checks for input (via an `InputState` helper) if the dialogue is waiting for line continuation or option selection.
            * Calls `SignalLineContinue()` or `SignalOptionSelected()` on the `EcsDialogueUI` instance associated with the active `DialogueRunnerComponent`.
            * Cleans up `DialogueRunnerComponent` when `runner.IsDialogueRunning` becomes false.

5.  **Trigger Integration:**
    * A separate system (e.g., `TriggerCollisionResolutionSystem`) detects relevant events (e.g., player entering a `DialogueTriggerZoneComponent`).
    * This system calls `YarnDialogueSystem.StartDialogue`, providing the target NPC (owner), the specific Yarn node from the trigger, the Yarn project from the NPC, and the dialogue UI entity.

## Implementation Proposal

*(Includes complete code snippets based on the previous refactoring)*

**1. Core Components:**

```csharp
// Defined in: YourGameNamespace.Yarn
using DefaultEcs;
using System;
using Yarn.Unity; // Or core YarnSpinner types if not using Unity package directly

namespace YourGameNamespace.Yarn
{
    /// <summary>
    /// Component attached to an entity (e.g., NPC) that can initiate Yarn dialogues.
    /// </summary>
    public struct YarnInteractableComponent
    {
        /// <summary>Asset name for the compiled Yarn project (.yarnc or custom format).</summary>
        public string YarnProjectAssetName;
        /// <summary>The default Yarn node to start when interacting directly with this entity.</summary>
        public string DefaultStartNode;
    }

    /// <summary>
    /// Component defining a trigger zone that starts a specific dialogue when the player enters.
    /// </summary>
    public struct DialogueTriggerZoneComponent
    {
        /// <summary>The Yarn node to start when this trigger is activated.</summary>
        public string TargetYarnNode;
        /// <summary>Reference to the NPC entity associated with this dialogue/zone.</summary>
        public Entity TargetNpcEntity; // Must be set after entity creation
        /// <summary>Internal state: Tracks if the player is currently inside the zone.</summary>
        public bool PlayerInside;
    }

    /// <summary>
    /// Component added temporarily to an entity (usually the NPC or a manager)
    /// when a Yarn dialogue is actively running. Holds the state and handlers.
    /// </summary>
    public class DialogueRunnerComponent // Class because DialogueRunner is a class
    {
        public Yarn.Unity.DialogueRunner Runner; // The core YarnSpinner runner instance
        public string CurrentNode; // The node that was started
        public bool IsRunning => Runner?.IsDialogueRunning ?? false; // Convenience property
        public Action OnDialogueComplete; // Optional callback Action when dialogue finishes naturally

        // Specific handler instances created for this dialogue session
        public EcsDialogueUI DialogueUIHandler;
        public EcsVariableStorage VariableStorageHandler;
        public EcsCommandDispatcher CommandDispatcherHandler;

        // References needed by handlers/system
        public Entity DialogueOwner; // e.g., The NPC entity speaking
        public Entity DialogueUIEntity; // The entity holding the DialogueUIComponent visual state
    }
}
```

**2. YarnSpinner Interface Implementations:**

```csharp
// Defined in: YourGameNamespace.Yarn
using DefaultEcs;
using Microsoft.Xna.Framework.Input; // For InputState
using System;
using System.Collections.Generic;
using System.Linq;
using Yarn.Unity; // Or core YarnSpinner types
using YourGameNamespace.Input; // Assumed InputState helper
using YourGameNamespace.UI; // For DialogueUIComponent access (defined in previous RFC)

namespace YourGameNamespace.Yarn
{
    /// <summary>
    /// DefaultECS implementation of YarnSpinner's VariableStorageBehaviour.
    /// Stores and retrieves Yarn variables ($variableName).
    /// </summary>
    public class EcsVariableStorage : VariableStorageBehaviour
    {
        private Dictionary<string, Yarn.Value> _variables = new Dictionary<string, Yarn.Value>();
        private readonly World _world; // Optional: For accessing ECS state

        public EcsVariableStorage(World world) { _world = world; }

        public override void SetValue(string variableName, Yarn.Value value) {
            // Could potentially write to ECS components here based on variableName
            _variables[variableName] = value;
        }

        public override Yarn.Value GetValue(string variableName) {
            // Could potentially read from ECS components here first
            return _variables.TryGetValue(variableName, out Yarn.Value value) ? value : Yarn.Value.NULL;
        }
        public override void Clear() { _variables.Clear(); }
        public override bool Contains(string variableName) => _variables.ContainsKey(variableName);
        public override void ResetToDefaults() { Clear(); /* Add initial values if needed */ }

        // Required by interface, often used for initialization or saving/loading
        public override void SetAllVariables(Dictionary<string, float> floats, Dictionary<string, string> strings, Dictionary<string, bool> bools, bool clear = true) {
             if (clear) Clear();
             // Note the '$' prefix convention for Yarn variables
             foreach(var pair in floats) SetValue("$"+pair.Key, new Yarn.Value(pair.Value));
             foreach(var pair in strings) SetValue("$"+pair.Key, new Yarn.Value(pair.Value));
             foreach(var pair in bools) SetValue("$"+pair.Key, new Yarn.Value(pair.Value));
        }
         // Required by interface, often used for saving/loading
        public override (Dictionary<string, float> floats, Dictionary<string, string> strings, Dictionary<string, bool> bools) GetAllVariables()
        {
            var floats = _variables.Where(p => p.Value.Type == Yarn.Value.ValueType.Number).ToDictionary(p => p.Key.TrimStart('$'), p => p.Value.AsNumber);
            var strings = _variables.Where(p => p.Value.Type == Yarn.Value.ValueType.String).ToDictionary(p => p.Key.TrimStart('$'), p => p.Value.AsString);
            var bools = _variables.Where(p => p.Value.Type == Yarn.Value.ValueType.Bool).ToDictionary(p => p.Key.TrimStart('$'), p => p.Value.AsBool);
            return (floats, strings, bools);
        }
    }

    /// <summary>
    /// DefaultECS implementation of YarnSpinner's DialogueUIBehaviour.
    /// Handles displaying lines, options, and dispatching commands by interacting
    /// with the DialogueUIComponent on a designated UI entity.
    /// </summary>
    public class EcsDialogueUI : DialogueUIBehaviour
    {
        private readonly World _world;
        private readonly InputState _inputState; // For convenience? Or should input be solely in YarnDialogueSystem? Let's keep it in system.
        private Entity _dialogueUIEntity;
        private Action _onLineFinishDisplaying;
        private Action<int> _onOptionSelected;
        public EcsCommandDispatcher CommandDispatcher { get; set; } // Injected dispatcher

        // Constructor might not need InputState if YarnDialogueSystem handles input checks
        public EcsDialogueUI(World world /*, InputState inputState */) { _world = world; /* _inputState = inputState; */ }

        /// <summary>Links this handler instance to the specific ECS entity that holds the visual UI component.</summary>
        public void SetTargetUIEntity(Entity uiEntity) { _dialogueUIEntity = uiEntity; }

        /// <summary>Called by Yarn Spinner to display a line of dialogue.</summary>
        public override DialogueInterruptAction RunLine(Yarn.Line line, ILineLocalisationProvider localisationProvider, Action onLineFinishDisplaying)
        {
            if (!_dialogueUIEntity.IsAlive || !_dialogueUIEntity.Has<DialogueUIComponent>()) return DialogueInterruptAction.StopDialogue;
            ref var ui = ref _dialogueUIEntity.Get<DialogueUIComponent>();

            string displayText = localisationProvider?.GetLocalisedTextForLine(line) ?? line.ID;
            var parts = displayText.Split(':', 2); // Simple speaker:text format handling

            ui.SpeakerName = parts.Length > 1 ? parts[0].Trim() : null;
            ui.CurrentLineText = parts.Length > 1 ? parts[1].Trim() : displayText.Trim();
            if (ui.CurrentOptions == null) ui.CurrentOptions = new List<string>(); else ui.CurrentOptions.Clear();
            ui.IsVisible = true;
            ui.ShowNextIndicator = true; // Assume instant display, ready for next input
            ui.WaitingForOptionSelection = false;

            _onLineFinishDisplaying = onLineFinishDisplaying; // Store callback for YarnDialogueSystem to trigger
            return DialogueInterruptAction.PauseAction; // Tell Yarn to wait
        }

        /// <summary>Called by YarnDialogueSystem when player input signals to continue.</summary>
        public void SignalLineContinue()
        {
            if (!_dialogueUIEntity.IsAlive || !_dialogueUIEntity.Has<DialogueUIComponent>()) return;
            ref var ui = ref _dialogueUIEntity.Get<DialogueUIComponent>();
            ui.ShowNextIndicator = false; // Hide prompt
            _onLineFinishDisplaying?.Invoke(); // Signal Yarn
            _onLineFinishDisplaying = null;
        }

        /// <summary>Called by Yarn Spinner to display a set of player choices.</summary>
        public override void RunOptions(Yarn.OptionSet optionSet, ILineLocalisationProvider localisationProvider, Action<int> onOptionSelected)
        {
            if (!_dialogueUIEntity.IsAlive || !_dialogueUIEntity.Has<DialogueUIComponent>()) return;
            ref var ui = ref _dialogueUIEntity.Get<DialogueUIComponent>();

            if (ui.CurrentOptions == null) ui.CurrentOptions = new List<string>(); else ui.CurrentOptions.Clear();

            var availableOptions = optionSet.Options.Where(o => o.IsAvailable).ToList();
            foreach (var option in availableOptions) {
                ui.CurrentOptions.Add(localisationProvider?.GetLocalisedTextForLine(option.Line) ?? option.Line.ID);
            }

            if (ui.CurrentOptions.Count == 0) {
                Console.WriteLine("Warning: No available options to display in RunOptions.");
                onOptionSelected(-1); // Signal no option could be chosen
                return;
            }

            _onOptionSelected = onOptionSelected; // Store callback
            ui.IsVisible = true;
            ui.ShowNextIndicator = false;
            ui.WaitingForOptionSelection = true; // Flag for YarnDialogueSystem
        }

         /// <summary>Called by YarnDialogueSystem when player input selects an option index.</summary>
         public void SignalOptionSelected(int optionIndex)
         {
             if (!_dialogueUIEntity.IsAlive || !_dialogueUIEntity.Has<DialogueUIComponent>()) return;
             ref var ui = ref _dialogueUIEntity.Get<DialogueUIComponent>();

             if (optionIndex >= 0 && optionIndex < (ui.CurrentOptions?.Count ?? 0)) {
                 ui.WaitingForOptionSelection = false;
                 _onOptionSelected?.Invoke(optionIndex); // Signal Yarn
                 _onOptionSelected = null;
                 ui.CurrentOptions?.Clear(); // Clear options from UI state
             } else {
                  Console.WriteLine($"Warning: SignalOptionSelected called with invalid index {optionIndex}.");
             }
         }

         /// <summary>Called by Yarn Spinner for commands like "<<command param>>".</summary>
         public override DialogueInterruptAction RunCommand(Yarn.Command command, Action onCommandComplete)
         {
             CommandDispatcher?.Dispatch(command); // Delegate to injected dispatcher
             onCommandComplete(); // Assume commands complete instantly for now
             return DialogueInterruptAction.ContinueExecution; // Tell Yarn to continue immediately
         }

         /// <summary>Called by Yarn Spinner when the dialogue officially starts.</summary>
         public override void DialogueStarted()
         {
             if (!_dialogueUIEntity.IsAlive || !_dialogueUIEntity.Has<DialogueUIComponent>()) return;
             ref var ui = ref _dialogueUIEntity.Get<DialogueUIComponent>();
             ui.IsVisible = true; ui.CurrentLineText = ""; ui.CurrentOptions?.Clear();
             ui.ShowNextIndicator = false; ui.WaitingForOptionSelection = false;
             Console.WriteLine("Dialogue Started.");
         }

         /// <summary>Called by Yarn Spinner when the current dialogue session ends.</summary>
         public override void DialogueComplete()
         {
             if (!_dialogueUIEntity.IsAlive || !_dialogueUIEntity.Has<DialogueUIComponent>()) return;
             ref var ui = ref _dialogueUIEntity.Get<DialogueUIComponent>();
             ui.IsVisible = false; ui.CurrentLineText = ""; ui.SpeakerName = null;
             ui.CurrentOptions?.Clear(); ui.ShowNextIndicator = false; ui.WaitingForOptionSelection = false;
             _onLineFinishDisplaying = null; _onOptionSelected = null; // Clear callbacks
             Console.WriteLine("Dialogue Complete.");
             // YarnDialogueSystem will remove the DialogueRunnerComponent
         }
         // Other overrides like NodeStart, NodeComplete can be added if needed for logic hooks
    }
}
```

**3. Command Handling Abstraction:**

```csharp
// Defined in: YourGameNamespace.Yarn
using DefaultEcs;
using Microsoft.Xna.Framework.Content;
using System;
using System.Collections.Generic;
using System.Linq; // For Skip

namespace YourGameNamespace.Yarn
{
    /// <summary>
    /// Interface for game-specific Yarn command handlers.
    /// Implement this interface to define how your game reacts to commands like "<<my_command param>>".
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>
        /// Gets the name of the command this handler processes (e.g., "give_item").
        /// Should be lowercase for case-insensitive matching.
        /// </summary>
        string CommandName { get; }

        /// <summary>
        /// Executes the command logic.
        /// </summary>
        /// <param name="parameters">The parameters passed to the command in the Yarn script.</param>
        /// <param name="dialogueOwner">The entity primarily associated with the current dialogue (e.g., the speaker).</param>
        /// <param name="world">The DefaultECS World, for accessing/modifying entities and components.</param>
        /// <param name="content">The Monogame ContentManager, for loading assets if needed by the command.</param>
        /// <returns>True if the command was successfully handled according to the handler's logic, false otherwise (e.g., wrong parameters, failed execution).</returns>
        bool HandleCommand(string[] parameters, Entity dialogueOwner, World world, ContentManager content);
    }

    /// <summary>
    /// Handles dispatching Yarn commands to registered ICommandHandler implementations.
    /// This class itself is part of the core Yarn integration, but it uses external handlers.
    /// </summary>
    public class EcsCommandDispatcher
    {
        private readonly World _world;
        private readonly ContentManager _content;
        private readonly Dictionary<string, ICommandHandler> _handlerMap; // Quick lookup map
        private Entity _dialogueOwner; // The entity associated with the current dialogue

        /// <summary> Creates a dispatcher using a collection of game-specific command handlers. </summary>
        public EcsCommandDispatcher(World world, ContentManager content, IEnumerable<ICommandHandler> commandHandlers)
        {
            _world = world;
            _content = content;

            // Build a lookup map for faster dispatching, handling potential duplicates
            _handlerMap = new Dictionary<string, ICommandHandler>(StringComparer.OrdinalIgnoreCase);
            if (commandHandlers != null) {
                foreach (var handler in commandHandlers) {
                    if (handler != null && !string.IsNullOrEmpty(handler.CommandName)) {
                        if (!_handlerMap.TryAdd(handler.CommandName, handler)) {
                             Console.WriteLine($"[CmdDispatch] Warning: Duplicate command handler registered for '{handler.CommandName}'. Only the first one found will be used.");
                        }
                    } else {
                         Console.WriteLine($"[CmdDispatch] Warning: Skipping invalid command handler (null or empty CommandName).");
                    }
                }
            }
            Console.WriteLine($"[CmdDispatch] Initialized with {_handlerMap.Count} unique command handlers.");
        }

        /// <summary>Links this dispatcher instance to the entity primarily associated with the dialogue.</summary>
        public void SetDialogueOwner(Entity owner) { _dialogueOwner = owner; }

        /// <summary>Parses and executes a Yarn command by finding and calling the appropriate registered handler.</summary>
        /// <returns>True if a handler was found and successfully executed (according to the handler's return value), False otherwise.</returns>
        public bool Dispatch(Yarn.Command command)
        {
            string[] parts = command.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false; // Empty command

            string commandName = parts[0];
            string[] parameters = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();

            if (_handlerMap.TryGetValue(commandName, out var handler)) {
                // Console.WriteLine($"[CmdDispatch] Dispatching '{commandName}' to handler {handler.GetType().Name}.");
                try {
                    // Execute the handler's logic and return its success status
                    bool handlerSuccess = handler.HandleCommand(parameters, _dialogueOwner, _world, _content);
                    if (!handlerSuccess) {
                        Console.WriteLine($"[CmdDispatch] Handler for '{commandName}' reported failure.");
                    }
                    return handlerSuccess; // Return the handler's success status
                } catch (Exception ex) {
                    Console.WriteLine($"[CmdDispatch] Error executing command handler for '{command.Text}': {ex.Message}\n{ex.StackTrace}");
                    return false; // Indicate handler failed via exception
                }
            } else {
                 Console.WriteLine($"[CmdDispatch] Warning: No command handler registered for '{commandName}'.");
                return false; // No handler found
            }
        }
    }
}
```

**4. Core System:**

```csharp
// Defined in: YourGameNamespace.Yarn
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Input; // For Keys enum
using System;
using System.Collections.Generic;
using System.Linq;
using Yarn.Unity; // Or core types
using YourGameNamespace.Input; // Assumed InputState helper
using YourGameNamespace.UI; // For DialogueUIComponent
using YourGameNamespace.Components; // For InputDisabledComponent check

namespace YourGameNamespace.Yarn
{
    /// <summary>
    /// DefaultECS System responsible for managing active Yarn dialogues.
    /// It handles starting/stopping dialogues, processing input for lines/options,
    /// and coordinating with the YarnSpinner library components.
    /// </summary>
    [With(typeof(DialogueRunnerComponent))] // Only process entities that have an active dialogue runner
    public partial class YarnDialogueSystem : AEntitySystem<GameTime>
    {
        private readonly InputState _inputState;
        private readonly ContentManager _contentManager;
        private readonly World _world;
        private readonly IReadOnlyList<ICommandHandler> _commandHandlers; // Store command handlers
        private readonly Dictionary<string, YarnProject> _loadedYarnProjects = new Dictionary<string, YarnProject>(); // Cache

        /// <summary> Creates the YarnDialogueSystem. </summary>
        /// <param name="world">The DefaultECS world.</param>
        /// <param name="inputState">The game's input state helper.</param>
        /// <param name="contentManager">The Monogame ContentManager.</param>
        /// <param name="commandHandlers">A collection of game-specific command handlers.</param>
        public YarnDialogueSystem(
            World world,
            InputState inputState,
            ContentManager contentManager,
            IEnumerable<ICommandHandler> commandHandlers)
            : base(world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _inputState = inputState ?? throw new ArgumentNullException(nameof(inputState));
            _contentManager = contentManager ?? throw new ArgumentNullException(nameof(contentManager));
            _commandHandlers = commandHandlers?.ToList() ?? new List<ICommandHandler>(); // Store handlers
        }

        /// <summary> Starts a new Yarn dialogue session. </summary>
        /// <param name="ownerEntity">The entity initiating the dialogue (e.g., NPC).</param>
        /// <param name="yarnProjectAsset">The asset name of the Yarn Project to use.</param>
        /// <param name="startNode">The specific node within the project to start execution from.</param>
        /// <param name="uiEntity">The entity holding the DialogueUIComponent for displaying the dialogue.</param>
        /// <param name="onComplete">Optional Action to call when this specific dialogue finishes naturally.</param>
        public void StartDialogue(Entity ownerEntity, string yarnProjectAsset, string startNode, Entity uiEntity, Action onComplete = null)
        {
            // --- Validation ---
            if (!ownerEntity.IsAlive) { Console.WriteLine("Error: Cannot start dialogue, owner entity is invalid."); return; }
            if (!uiEntity.IsAlive || !uiEntity.Has<DialogueUIComponent>()) { Console.WriteLine("Error: Cannot start dialogue, UI entity is invalid or missing DialogueUIComponent."); return; }
            if (string.IsNullOrEmpty(yarnProjectAsset)) { Console.WriteLine("Error: Cannot start dialogue, Yarn project asset name is missing."); return; }
            if (string.IsNullOrEmpty(startNode)) { Console.WriteLine("Error: Cannot start dialogue, start node name is missing."); return; }

            // --- Prevent Overlapping Dialogues (Simple Check) ---
            var activeRunners = World.GetEntities().With<DialogueRunnerComponent>().AsSet();
            if (activeRunners.Count > 0) {
                 Console.WriteLine($"Warning: Tried to start dialogue '{startNode}' for {ownerEntity}, but another dialogue is already running ({activeRunners.GetEntities()[0]}). Aborting.");
                 activeRunners.Dispose();
                 return;
            }
            activeRunners.Dispose();

            // --- Load Yarn Project (or Program) ---
            YarnProject yarnProject = GetOrLoadYarnProject(yarnProjectAsset); // Needs implementation
            if (yarnProject == null) {
                Console.WriteLine($"Error: Failed to load Yarn Project '{yarnProjectAsset}'. Cannot start dialogue.");
                return;
            }

            // --- Create Handlers for this Session ---
            var variableStorage = new EcsVariableStorage(_world);
            var dialogueUI = new EcsDialogueUI(_world /*, _inputState */); // Pass input state if needed by UI directly
            var commandDispatcher = new EcsCommandDispatcher(_world, _contentManager, _commandHandlers); // Pass registered handlers

            // --- Link Handlers ---
            dialogueUI.SetTargetUIEntity(uiEntity);
            dialogueUI.CommandDispatcher = commandDispatcher; // Give UI access to dispatcher
            commandDispatcher.SetDialogueOwner(ownerEntity); // Link dispatcher to owner

            // --- Create and Configure DialogueRunner ---
            var runner = new Yarn.Unity.DialogueRunner {
                yarnProject = yarnProject,
                variableStorage = variableStorage,
                dialogueUI = dialogueUI
                // If runner supports command dispatcher directly, assign it here.
            };

            // --- Create and Add Runner Component to Owner ---
            var runnerComponent = new DialogueRunnerComponent {
                Runner = runner,
                CurrentNode = startNode,
                DialogueOwner = ownerEntity,
                DialogueUIEntity = uiEntity,
                DialogueUIHandler = dialogueUI,
                VariableStorageHandler = variableStorage,
                CommandDispatcherHandler = commandDispatcher,
                OnDialogueComplete = onComplete
            };
            ownerEntity.Set(runnerComponent); // Add the component to the NPC/owner

            // --- Start the Dialogue ---
            Console.WriteLine($"Starting dialogue node '{startNode}' on entity {ownerEntity} using UI {uiEntity}.");
            try {
                 runner.StartDialogue(startNode);
            } catch (Exception ex) {
                 Console.WriteLine($"Error starting Yarn dialogue '{startNode}': {ex.Message}\n{ex.StackTrace}");
                 ownerEntity.Remove<DialogueRunnerComponent>(); // Clean up if start failed
            }
        }

        /// <summary> Stops the dialogue currently running on the specified entity, if any. </summary>
        public void StopDialogue(Entity ownerEntity)
        {
            if (ownerEntity.Has<DialogueRunnerComponent>()) {
                Console.WriteLine($"Stopping dialogue explicitly for entity {ownerEntity}.");
                ref var runnerComp = ref ownerEntity.Get<DialogueRunnerComponent>();
                runnerComp.Runner?.StopDialogue(); // Tell Yarn Spinner to stop
                runnerComp.DialogueUIHandler?.DialogueComplete(); // Ensure UI cleanup happens
                ownerEntity.Remove<DialogueRunnerComponent>(); // Remove component to stop processing
            }
        }

        /// <summary> Helper method to load/cache YarnProjects. Needs implementation. </summary>
        private YarnProject GetOrLoadYarnProject(string assetName)
        {
            if (_loadedYarnProjects.TryGetValue(assetName, out var project)) {
                return project;
            }
            // --- Loading Logic (Needs Your Implementation) ---
            // Example using ContentManager assuming custom pipeline for YarnProject:
            try {
                 // project = _contentManager.Load<YarnProject>(assetName);
                 // if (project != null) {
                 //    _loadedYarnProjects.Add(assetName, project);
                 //    Console.WriteLine($"Loaded and cached Yarn Project: {assetName}");
                 //    return project;
                 // }
                 throw new NotImplementedException($"YarnProject loading for asset '{assetName}' is not implemented.");
            } catch (Exception ex) {
                 Console.WriteLine($"Failed to load Yarn Project '{assetName}': {ex.Message}");
                 return null; // Loading failed
            }
        }

        /// <summary> Processes active dialogues each frame: checks for input, signals YarnSpinner. </summary>
        [Update] // DefaultECS Source Generator attribute
        private void Update(GameTime gameTime, in Entity entity, ref DialogueRunnerComponent runnerComp) // Use 'in Entity' and 'ref Component'
        {
            var runner = runnerComp.Runner;
            var uiHandler = runnerComp.DialogueUIHandler;
            var uiEntity = runnerComp.DialogueUIEntity;

            // --- Cleanup Check ---
            // If runner stopped processing, or UI entity is gone, remove the component
            if (runner == null || !runner.IsDialogueRunning || !uiEntity.IsAlive || !uiEntity.Has<DialogueUIComponent>()) {
                // Console.WriteLine($"Dialogue runner stopped or UI invalid for {entity}. Cleaning up.");
                runnerComp.OnDialogueComplete?.Invoke(); // Call completion callback if dialogue finished naturally
                entity.Remove<DialogueRunnerComponent>(); // Remove component to stop processing this entity
                // Ensure UI is hidden if DialogueComplete didn't catch it
                 if (uiEntity.IsAlive && uiEntity.Has<DialogueUIComponent>()) {
                     ref var uiComp = ref uiEntity.Get<DialogueUIComponent>();
                     if (uiComp.IsVisible) uiComp.IsVisible = false;
                 }
                return;
            }

            // --- Input Processing ---
            // Get the UI component state to check flags
            ref var ui = ref uiEntity.Get<DialogueUIComponent>();

            // Check if input should be processed (e.g., player not stunned)
            // Assumes InputDisabledComponent might be added to the player or dialogue owner
            bool canProcessInput = !runnerComp.DialogueOwner.Has<InputDisabledComponent>()
                                && !World.GetEntities().With<PlayerComponent>().With<InputDisabledComponent>().AsSet().Any(); // Check player too?

            if (canProcessInput) {
                 // 1. Check for Line Continuation Input
                 if (ui.ShowNextIndicator) { // Are we waiting to continue the current line?
                     if (_inputState.IsActionPressed()) { // Use your game's "confirm/action" button check
                         // Console.WriteLine("Input detected: Continue line.");
                         uiHandler.SignalLineContinue(); // Tell the UI handler (and Yarn) to proceed
                     }
                 }
                 // 2. Check for Option Selection Input
                 else if (ui.WaitingForOptionSelection) { // Are we waiting for the player to choose an option?
                     int optionCount = ui.CurrentOptions?.Count ?? 0;
                     if (optionCount > 0) {
                         int selectedOption = -1;
                         // --- Input method examples ---
                         // a) Number keys 1-N:
                         if (_inputState.IsKeyPressed(Keys.D1)) selectedOption = 0;
                         if (_inputState.IsKeyPressed(Keys.D2)) selectedOption = 1;
                         if (_inputState.IsKeyPressed(Keys.D3)) selectedOption = 2;
                         // ... add checks up to optionCount ...

                         // b) Up/Down + Action (requires UI state for current selection index)
                         // if (_inputState.IsUpPressed()) { /* ui.SelectedOptionIndex--; */ }
                         // if (_inputState.IsDownPressed()) { /* ui.SelectedOptionIndex++; */ }
                         // if (_inputState.IsActionPressed()) { selectedOption = ui.SelectedOptionIndex; }

                         if (selectedOption != -1 && selectedOption < optionCount) {
                             // Console.WriteLine($"Input detected: Select option {selectedOption}.");
                             uiHandler.SignalOptionSelected(selectedOption); // Tell UI handler the choice
                         }
                     }
                 }
            }
            // --- End Input Processing ---

            // Note: Yarn Spinner's DialogueRunner advances state internally when its callbacks
            // (_onLineFinishDisplaying, _onOptionSelected) are invoked via our Signal* methods.
            // We typically DON'T call runner.Continue() manually in the Update loop.
        }
    }
}
```

**5. Example Command Handler:**

```csharp
// Defined in: YourGameNamespace.Commands (Game-specific code)
using YourGameNamespace.Yarn; // ICommandHandler
using YourGameNamespace.Components; // Assumed game components like PlayerComponent, InventoryComponent
using DefaultEcs;
using Microsoft.Xna.Framework.Content;
using System.Collections.Generic; // For Dictionary access
using System; // For int.TryParse

namespace YourGameNamespace.Commands
{
    /// <summary>
    /// Example handler for the "<<give_item item_id amount>>" command.
    /// </summary>
    public class GiveItemHandler : ICommandHandler
    {
        // Command name should be lowercase for reliable matching
        public string CommandName => "give_item";

        /// <summary> Handles the give_item command logic. </summary>
        /// <returns>True if item was successfully given, false otherwise.</returns>
        public bool HandleCommand(string[] parameters, Entity dialogueOwner, World world, ContentManager content)
        {
            // 1. Validate Parameters
            if (parameters.Length < 2) {
                Console.WriteLine($"Error: <<{CommandName}>> requires item_id and amount.");
                return false; // Indicate failure: wrong parameters
            }
            string itemId = parameters[0];
            if (!int.TryParse(parameters[1], out int amount) || amount <= 0) { // Ensure positive amount
                 Console.WriteLine($"Error: <<{CommandName}>> amount '{parameters[1]}' is not a valid positive integer.");
                return false; // Indicate failure: bad amount parameter
            }

            // 2. Execute Logic (Find player and add item)
            // Use EntitySet for potentially better performance if queried often, but requires disposal
            using var playerSet = world.GetEntities().With<PlayerComponent>().With<InventoryComponent>().AsSet();
            bool success = false;
            if (playerSet.Count > 0)
            {
                // Assume single player for simplicity
                ref var inventory = ref playerSet.GetEntities()[0].Get<InventoryComponent>();
                // Ensure inventory dictionary exists
                if (inventory.Items == null) inventory.Items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Add item (or update count)
                inventory.Items.TryGetValue(itemId, out int currentAmount);
                inventory.Items[itemId] = currentAmount + amount;
                success = true; // Command logic succeeded

                Console.WriteLine($"[CmdHandler:{CommandName}] Gave {amount} of '{itemId}' to player. New total: {inventory.Items[itemId]}");
            } else {
                Console.WriteLine($"Error: <<{CommandName}>> cannot find Player with InventoryComponent.");
                success = false; // Indicate failure: player/inventory not found
            }
            // playerSet is disposed automatically by 'using' statement

            // 3. Return Success Status
            return success;
        }
    }

    // --- Example: Dummy Quest Manager and Handler ---
    public static class YourQuestManager {
        private static HashSet<string> activeQuests = new HashSet<string>();
        public static bool StartQuest(string id) {
            if (string.IsNullOrEmpty(id) || activeQuests.Contains(id)) return false; // Invalid ID or already active
            activeQuests.Add(id); Console.WriteLine($"[QuestManager] Quest '{id}' started."); return true;
        }
        public static bool IsQuestActive(string id) => activeQuests.Contains(id);
    }

    public class StartQuestHandler : ICommandHandler {
         public string CommandName => "start_quest";
         public bool HandleCommand(string[] parameters, Entity dialogueOwner, World world, ContentManager content) {
              if (parameters.Length < 1) { Console.WriteLine($"Error: <<{CommandName}>> requires quest_id."); return false; }
              string questId = parameters[0];
              bool questStarted = YourQuestManager.StartQuest(questId); // Call game's quest logic
              if (questStarted) Console.WriteLine($"[CmdHandler:{CommandName}] Quest '{questId}' successfully started.");
              else Console.WriteLine($"[CmdHandler:{CommandName}] Quest '{questId}' could not be started (e.g., already active).");
              return questStarted; // Return success status from quest system
         }
     }
     // Add more handlers as needed...
}
```

Usage ExampleImplement ICommandHandler: Create classes implementing ICommandHandler for each custom command your game needs (e.g., PlaySoundHandler, StartQuestHandler, MoveNPCHandler). Place these in your game-specific code, perhaps a YourGameNamespace.Commands namespace.Instantiate Handlers: Create instances of your command handlers, potentially passing dependencies like sound managers.
```
// In your game's initialization logic
var commandHandlers = new List<ICommandHandler> {
    new GiveItemHandler(),
    new StartQuestHandler(),
    // new PlaySoundHandler(mySoundManager) // Example with dependency
};
```
Instantiate YarnDialogueSystem: Pass the list of command handlers to its constructor when setting up your ECS systems.
```
// In your system setup
_yarnSystem = new YarnDialogueSystem(_world, _inputState, _content, commandHandlers);
Trigger Dialogue: Use another system (e.g., TriggerCollisionResolutionSystem, or an interaction system) to call _yarnSystem.StartDialogue(...) when appropriate. Ensure you pass the correct owner entity (NPC), project asset name, start node, and the designated dialogue UI entity.// Inside TriggerCollisionResolutionSystem or similar
// ... when player enters trigger for 'npcEntity' ...
ref var npcInteractable = ref npcEntity.Get<YarnInteractableComponent>();
ref var triggerZone = ref triggerEntity.Get<DialogueTriggerZoneComponent>(); // Assuming triggerEntity is the zone

_yarnSystem.StartDialogue(
    ownerEntity: npcEntity,
    yarnProjectAsset: npcInteractable.YarnProjectAssetName,
    startNode: triggerZone.TargetYarnNode, // Node specific to this trigger
    uiEntity: _dialogueUIEntity // The shared UI entity
);
```
Yarn Script: Use the commands defined by your handlers in your .yarn files.
```
title:NPC_Bob_Greeting
---
Bob: Welcome to my shop! Need anything?
Bob: <<give_item potion 1>> Here, take this potion on the house.
Bob: <<start_quest find_widget>> Actually, maybe you could help me find my lost widget?
===
```
Backwards Compatibility
This proposal defines a new integration pattern. It's not directly compatible with other ad-hoc YarnSpinner integrations but provides a clear structure for new implementations or refactoring existing ones. The core dependency is on the YarnSpinner-CSharp library structure (e.g., DialogueRunner, DialogueUIBehaviour). Migrating would involve replacing custom dialogue logic with this system and implementing game commands via ICommandHandler. The decoupling of commands makes future changes to game logic easier without altering the core Yarn integration.Security ConsiderationsCommand Parameter Validation: Command handlers (ICommandHandler implementations) must rigorously validate and sanitize string parameters received from Yarn scripts before using them. Assume parameters can be anything. Parse numbers carefully, check ranges, validate IDs against known lists (items, quests, sounds), etc., to prevent errors, crashes, or potential exploits. Never trust input directly from the script without validation.Resource Loading: Handlers loading assets (e.g., sounds, textures) based on parameters must validate those parameters to prevent loading arbitrary or excessive resources. Use allow-lists or predefined mappings where possible instead of directly using parameter strings as asset paths.Open IssuesYarnProject Loading: The mechanism for loading the YarnProject (or core Yarn.Program) outside of the Unity environment needs a concrete implementation specific to the engine's content pipeline (e.g., custom Monogame Content Processor/Importer). The GetOrLoadYarnProject method in YarnDialogueSystem requires this implementation. Developers need clear guidance on how to prepare and load their compiled Yarn data.Asynchronous Commands: The current ICommandHandler interface and EcsDialogueUI.RunCommand assume synchronous execution. Commands that need to wait for an action (e.g., an animation to finish, a sound to play, a fade-out) before the dialogue continues would require a more complex mechanism. Potential solutions involve:EcsDialogueUI.RunCommand returning DialogueInterruptAction.PauseAction.The command handler starting an asynchronous task or creating a temporary "waiting" entity/component.Another system monitoring the task/entity