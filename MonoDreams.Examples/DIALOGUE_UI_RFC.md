# RFC: Modular Dialogue UI Rendering via Generic Systems

**Status:** Proposed
**Author:** Gemini
**Date:** 2025-04-06

## Abstract

This document proposes a method for rendering dialogue user interfaces within a DefaultECS-based game engine by leveraging existing, generic rendering systems (`DrawSystem`, `TextSystem`) instead of implementing a dedicated `DialogueDrawSystem`. This is achieved through a configuration component (`DialogueUIComponent`), supporting drawing/text components (`DrawInfoComponent`, `TextInfoComponent`), and a synchronization system (`DialogueUISyncSystem`).

## Motivation

Current or potential implementations might use a single, monolithic system (`DialogueDrawSystem`) to handle all aspects of drawing a dialogue box (background, text, emotes, indicators). While functional, this approach lacks modularity and reusability.

By decoupling the dialogue *state* and *configuration* from the actual *rendering*, we gain several benefits:

1.  **Modularity:** The core dialogue logic (e.g., YarnSpinner integration) becomes independent of the specific rendering implementation.
2.  **Reusability:** Existing `DrawSystem` and `TextSystem`, likely already used for other game elements, can be reused for the dialogue UI, reducing code duplication.
3.  **Flexibility:** Different visual styles or rendering techniques for the dialogue UI can be achieved by modifying the `DialogueUISyncSystem` or the data it generates, without altering the core dialogue or rendering systems.
4.  **Maintainability:** Changes to rendering logic are localized to the generic systems or the sync system, simplifying updates.

## Specification

This proposal relies on the interaction of the following components and systems:

1.  **`DialogueUIComponent` (Struct):**
    * Holds the *configuration* and *dynamic state* of the dialogue UI.
    * Contains asset *names* (strings) for textures (background, emote, indicator) and fonts.
    * Defines layout properties (e.g., `BackgroundBounds`, relative positions for elements, colors, text wrap width).
    * Stores dynamic state updated by the dialogue logic system (e.g., `IsVisible`, `CurrentLineText`, `CurrentOptions`, `ShowNextIndicator`).
    * **Does NOT contain direct references** to loaded `Texture2D` or `SpriteFont` assets.

2.  **`Position` (Component - Assumed):**
    * Standard engine component defining the absolute world/screen position (`Vector2`) of an entity.
    * The `DialogueUISyncSystem` sets this based on `DialogueUIComponent.BackgroundBounds.Location`.

3.  **`DrawInfoComponent` (Component - Assumed):**
    * Generic engine component processed by `DrawSystem`.
    * Contains a `List<DrawElement>` where each `DrawElement` specifies:
        * `TextureAssetName` (string)
        * `Texture` (`Texture2D`, loaded by `DrawSystem`)
        * `RelativePosition` (`Vector2`, offset from the entity's `Position`)
        * Other drawing parameters (`Color`, `SourceRect`, `Scale`, `LayerDepth`, etc.).
    * May include a flag like `NeedsAssetLoading`.

4.  **`TextInfoComponent` (Component - Assumed):**
    * Generic engine component processed by `TextSystem`.
    * Contains a `List<TextElement>` where each `TextElement` specifies:
        * `FontAssetName` (string)
        * `Font` (`SpriteFont`, loaded by `TextSystem`)
        * `Text` (string)
        * `RelativePosition` (`Vector2`, offset from the entity's `Position`)
        * Other text parameters (`Color`, `WrapWidth`, `Scale`, `LayerDepth`, etc.).
    * May include a flag like `NeedsAssetLoading`.

5.  **`DialogueUISyncSystem` (System):**
    * An `AEntitySystem` that processes entities with `DialogueUIComponent`.
    * Runs *after* the dialogue logic system (e.g., `YarnDialogueSystem`) has updated the `DialogueUIComponent` state.
    * **Responsibilities:**
        * Reads the `DialogueUIComponent` state.
        * If `IsVisible` is true:
            * Ensures the entity has `Position`, `DrawInfoComponent`, and `TextInfoComponent`.
            * Clears the `Elements` lists in `DrawInfoComponent` and `TextInfoComponent`.
            * Populates the `Elements` lists based on the `DialogueUIComponent` configuration and state (background, emote, text, options, indicator), setting appropriate asset names, relative positions, colors, text content, wrap widths, and layer depths.
            * Sets the `NeedsAssetLoading` flag if components were newly added or if asset names changed (optional optimization).
        * If `IsVisible` is false:
            * Removes `DrawInfoComponent` and `TextInfoComponent` from the entity (or clears their lists).

6.  **`DrawSystem` (System - Assumed):**
    * Generic engine system that processes entities with `Position` and `DrawInfoComponent`.
    * Handles loading textures specified by `TextureAssetName` (if not already loaded and `NeedsAssetLoading` is true).
    * Draws all `DrawElement`s in the `DrawInfoComponent.Elements` list using `SpriteBatch`, applying relative positions, colors, scaling, layer depth, etc.

7.  **`TextSystem` (System - Assumed):**
    * Generic engine system that processes entities with `Position` and `TextInfoComponent`.
    * Handles loading fonts specified by `FontAssetName` (if not already loaded and `NeedsAssetLoading` is true).
    * Draws all `TextElement`s in the `TextInfoComponent.Elements` list using `SpriteBatch`, applying relative positions, colors, wrapping, layer depth, etc.

## Implementation Proposal

*(Includes relevant code snippets)*

**1. Assumed Generic Components:**

```csharp
// Defined in: YourGameNamespace.Components
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace YourGameNamespace.Components
{
    // Absolute position in the world/screen
    public struct Position { public Vector2 Value; }

    // --- Draw Info Component (Handles Multiple Textures) ---
    public struct DrawElement // Represents a single texture to draw for an entity
    {
        public string TextureAssetName; // Asset name loaded by DrawSystem
        public Texture2D Texture; // Loaded texture (DrawSystem might handle loading)
        public Vector2 RelativePosition; // Offset from the entity's main Position
        public Rectangle? SourceRect;
        public Color Color;
        public float Rotation;
        public Vector2 Origin;
        public Vector2 Scale;
        public SpriteEffects Effects;
        public float LayerDepth; // For sorting within SpriteBatch

        // Default constructor or static factory might be useful
        public static DrawElement Default => new DrawElement { Color = Color.White, Scale = Vector2.One };
    }
    public struct DrawInfoComponent // Holds all textures for one entity
    {
        // List allows multiple textures (background, emote, indicator) on one entity
        public List<DrawElement> Elements;
        // Flag used by DialogueUISyncSystem and DrawSystem
        public bool NeedsAssetLoading;

        public static DrawInfoComponent Default => new DrawInfoComponent { Elements = new List<DrawElement>(4), NeedsAssetLoading = true };
    }


    // --- Text Info Component (Handles Multiple Text Strings) ---
     public struct TextElement // Represents a single string to draw for an entity
    {
        public string FontAssetName; // Asset name loaded by TextSystem
        public SpriteFont Font; // Loaded font (TextSystem might handle loading)
        public string Text;
        public Vector2 RelativePosition; // Offset from the entity's main Position
        public Color Color;
        public float Rotation;
        public Vector2 Origin;
        public Vector2 Scale;
        public SpriteEffects Effects;
        public float LayerDepth; // For sorting
        public float WrapWidth; // Optional: Max width before wrapping

        public static TextElement Default => new TextElement { Color = Color.White, Scale = Vector2.One };
    }
     public struct TextInfoComponent // Holds all text for one entity
    {
        // List allows multiple text blocks (speaker, line, options) on one entity
        public List<TextElement> Elements;
        // Flag used by DialogueUISyncSystem and TextSystem
        public bool NeedsAssetLoading;

        public static TextInfoComponent Default => new TextInfoComponent { Elements = new List<TextElement>(5), NeedsAssetLoading = true };
    }
     // Other components like PlayerComponent, Collider etc. are assumed to exist elsewhere
}
```

**2. Refactored DialogueUIComponent:**

```csharp
// Defined in: YourGameNamespace.UI
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics; // For Color, Rectangle
using System.Collections.Generic;

namespace YourGameNamespace.UI
{
    /// <summary>
    /// Component holding configuration and dynamic state for a dialogue interface.
    /// Does NOT hold direct asset references (Texture2D, SpriteFont).
    /// Relies on DialogueUISyncSystem to create DrawInfoComponent/TextInfoComponent elements.
    /// </summary>
    public struct DialogueUIComponent
    {
        // --- Configuration (Set by Builder) ---
        public string BackgroundAssetName;
        public string EmoteAssetName; // Optional
        public string NextIndicatorAssetName; // Optional
        public string FontAssetName; // Font used for all text elements

        public Rectangle BackgroundBounds; // Defines overall position and size anchor
        public Vector2 TextRelativePosition; // Relative to BackgroundBounds.Location
        public Vector2 EmoteRelativePosition; // Relative to BackgroundBounds.Location
        public Vector2 NextIndicatorRelativePosition; // Relative to BackgroundBounds.Location
        public Color TextColor;
        public Color SpeakerNameColor; // Can be different
        public float TextWrapWidth;

        // --- Dynamic State (Managed by Dialogue System) ---
        public bool IsVisible;
        public string SpeakerName;
        public string CurrentLineText;
        public List<string> CurrentOptions;
        public bool ShowNextIndicator;
        public bool WaitingForOptionSelection;

        // --- Helper for Initialization ---
        public static DialogueUIComponent Default => new DialogueUIComponent
        {
            TextColor = Color.White,
            SpeakerNameColor = Color.Yellow, // Default speaker color
            IsVisible = false,
            CurrentOptions = new List<string>(),
            BackgroundBounds = new Rectangle(50, 450, 700, 100), // Example default bounds
            TextRelativePosition = new Vector2(15, 15),         // Example default text offset
            EmoteRelativePosition = new Vector2(-60, -10),       // Example default emote offset
            NextIndicatorRelativePosition = new Vector2(660, 70),// Example default indicator offset
            TextWrapWidth = 670 // Example default wrap width based on bounds/text offset
        };
    }
    // DialogueUIBuilder class (as defined previously) would also live here,
    // responsible for setting the asset names and layout properties in DialogueUIComponent.
}
```

**3. DialogueUISyncSystem:**

```csharp
// Defined in: YourGameNamespace.Systems
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics; // For Color
using System;
using System.Collections.Generic; // For List manipulation
using YourGameNamespace.Components; // Position, DrawInfoComponent, TextInfoComponent, DrawElement, TextElement
using YourGameNamespace.UI; // DialogueUIComponent

namespace YourGameNamespace.Systems
{
    /// <summary>
    /// Synchronizes the state of a DialogueUIComponent with generic
    /// DrawInfoComponent and TextInfoComponent lists on the same entity.
    /// This allows using standard DrawSystem and TextSystem for rendering the dialogue UI.
    /// Should run AFTER YarnDialogueSystem updates the DialogueUIComponent.
    /// </summary>
    [With(typeof(DialogueUIComponent))] // Process entities that have the dialogue config/state
    public partial class DialogueUISyncSystem : AEntitySystem<GameTime>
    {
        // Layer depths for sorting draw/text elements (adjust values as needed)
        private const float LayerDepthBackground = 0.5f;
        private const float LayerDepthEmote = 0.51f;
        private const float LayerDepthText = 0.52f;
        private const float LayerDepthOptions = 0.52f; // Same as text or slightly above/below?
        private const float LayerDepthIndicator = 0.53f;

        public DialogueUISyncSystem(World world) : base(world) { }

        // Using source generator for efficient component access
        [Update]
        private void Update(GameTime gameTime, in Entity entity, in DialogueUIComponent ui)
        {
            // --- Ensure Position Component ---
            // The UI's absolute position is determined by the BackgroundBounds.Location
            Vector2 absolutePosition = new Vector2(ui.BackgroundBounds.X, ui.BackgroundBounds.Y);
            // SetOrUpdate the Position component
            entity.Set(new Position { Value = absolutePosition });

            // --- Handle Visibility ---
            if (!ui.IsVisible)
            {
                // If not visible, remove the drawing components to prevent rendering
                // Alternatively, clear the Elements lists if components should persist
                entity.Remove<DrawInfoComponent>();
                entity.Remove<TextInfoComponent>();
                return; // Nothing more to do if invisible
            }

            // --- Ensure DrawInfoComponent and TextInfoComponent Exist ---
            // Use GetOrSetDefault to add components with default values if they don't exist
            // Default includes initializing the list and setting NeedsAssetLoading = true
            ref var drawInfo = ref entity.GetOrSet(() => DrawInfoComponent.Default);
            ref var textInfo = ref entity.GetOrSet(() => TextInfoComponent.Default);

            // Check if components were *just* added by GetOrSetDefault
            // We only need to set NeedsAssetLoading = true when components are first added
            // or if asset names *change* (more complex check not implemented here).
            // Simple approach: Assume assets might need loading if lists are empty/just created.
            bool needsLoad = drawInfo.Elements.Count == 0 || textInfo.Elements.Count == 0;
            drawInfo.NeedsAssetLoading = needsLoad || drawInfo.NeedsAssetLoading;
            textInfo.NeedsAssetLoading = needsLoad || textInfo.NeedsAssetLoading;

            // Clear previous elements before adding new ones for this frame
            drawInfo.Elements.Clear();
            textInfo.Elements.Clear();


            // --- Populate DrawInfoComponent Elements ---

            // 1. Background
            if (!string.IsNullOrEmpty(ui.BackgroundAssetName))
            {
                drawInfo.Elements.Add(new DrawElement {
                    TextureAssetName = ui.BackgroundAssetName,
                    RelativePosition = Vector2.Zero, // Background uses entity's absolute position
                    Color = Color.White,
                    Scale = Vector2.One, // Assume DrawSystem handles stretching/9-patch based on bounds
                    LayerDepth = LayerDepthBackground
                    // SourceRect might be needed depending on DrawSystem's implementation
                });
            }

            // 2. Emote
            if (!string.IsNullOrEmpty(ui.EmoteAssetName))
            {
                 drawInfo.Elements.Add(new DrawElement {
                    TextureAssetName = ui.EmoteAssetName,
                    RelativePosition = ui.EmoteRelativePosition, // Use relative offset from config
                    Color = Color.White,
                    Scale = Vector2.One,
                    LayerDepth = LayerDepthEmote
                 });
            }

            // 3. Next Indicator (only if shown)
            if (ui.ShowNextIndicator && !string.IsNullOrEmpty(ui.NextIndicatorAssetName))
            {
                // Optional pulsing effect via color alpha
                float pulse = (float)(Math.Sin(gameTime.TotalGameTime.TotalSeconds * 5.0) + 1.0) / 2.0f; // Ranges 0 to 1
                Color indicatorColor = Color.White * (0.6f + pulse * 0.4f); // Pulse between 60% and 100% alpha

                drawInfo.Elements.Add(new DrawElement {
                    TextureAssetName = ui.NextIndicatorAssetName,
                    RelativePosition = ui.NextIndicatorRelativePosition,
                    Color = indicatorColor,
                    Scale = Vector2.One,
                    LayerDepth = LayerDepthIndicator
                });
            }


            // --- Populate TextInfoComponent Elements ---
            if (string.IsNullOrEmpty(ui.FontAssetName)) {
                // Log warning or skip if no font is specified
                Console.WriteLine($"Warning: Entity {entity} has visible DialogueUIComponent but no FontAssetName set.");
                return;
            }

            // 1. Speaker Name (if present)
            if (!string.IsNullOrEmpty(ui.SpeakerName))
            {
                // Calculate position relative to main text position
                // This is still tricky without font metrics. Assume fixed offset or TextSystem handles layout.
                Vector2 speakerNameRelativePos = new Vector2(
                    ui.TextRelativePosition.X,
                    ui.TextRelativePosition.Y - 20 // Example: 20 pixels above main text baseline (adjust!)
                );
                 textInfo.Elements.Add(new TextElement {
                     FontAssetName = ui.FontAssetName,
                     Text = ui.SpeakerName,
                     RelativePosition = speakerNameRelativePos,
                     Color = ui.SpeakerNameColor, // Use specific speaker color
                     Scale = Vector2.One,
                     LayerDepth = LayerDepthText
                     // No wrap width for speaker name usually
                 });
            }

            // 2. Main Dialogue Line
            if (!string.IsNullOrEmpty(ui.CurrentLineText))
            {
                 textInfo.Elements.Add(new TextElement {
                     FontAssetName = ui.FontAssetName,
                     Text = ui.CurrentLineText,
                     RelativePosition = ui.TextRelativePosition, // Use relative offset from config
                     Color = ui.TextColor,
                     WrapWidth = ui.TextWrapWidth, // Apply wrapping
                     Scale = Vector2.One,
                     LayerDepth = LayerDepthText
                 });
            }

            // 3. Options (if waiting for selection)
            if (ui.WaitingForOptionSelection && ui.CurrentOptions != null && ui.CurrentOptions.Count > 0)
            {
                 // Calculate starting Y position below the main text
                 // This remains the most complex part without a layout engine or font metrics.
                 // Using a simple fixed offset per line as a basic approach.
                 float optionLineHeight = 25; // Estimate line height including spacing (adjust!)
                 float startY = ui.TextRelativePosition.Y + optionLineHeight; // Start one line below main text pos
                 // A better estimate might use ui.Font.LineSpacing if font was loaded, but it's not.
                 // Or calculate based on wrapped main text height if possible.

                 float optionX = ui.TextRelativePosition.X + 10; // Indent options

                 for (int i = 0; i < ui.CurrentOptions.Count; i++)
                 {
                     string optionDisplay = $"{i + 1}. {ui.CurrentOptions[i]}";
                     textInfo.Elements.Add(new TextElement {
                         FontAssetName = ui.FontAssetName,
                         Text = optionDisplay,
                         RelativePosition = new Vector2(optionX, startY + (i * optionLineHeight)), // Position each option line
                         Color = ui.TextColor,
                         WrapWidth = ui.TextWrapWidth - 15, // Wrap options too, slightly narrower
                         Scale = Vector2.One,
                         LayerDepth = LayerDepthOptions
                     });
                 }
            }
        }
    }
}
```

*Usage*

**ExampleEntity Setup:**
Create a single entity for the dialogue UI. Use DialogueUIBuilder to configure and add the DialogueUIComponent.

```csharp
// In game initialization
var uiBuilder = new DialogueUIBuilder(); // ContentManager optional
_dialogueUIEntity = _world.CreateEntity();
_dialogueUIEntity.Set(uiBuilder
    .SetBackground("UI/dialogue_box_texture", new Rectangle(50, 450, 700, 100))
    .SetFont("Fonts/DialogueFont") // Asset name for TextSystem
    .SetTextColor(Color.Black)
    .SetNextIndicator("UI/next_indicator", new Vector2(660, 70)) // Asset name
    // ... other configurations ...
    .Build());
// Position, DrawInfoComponent, TextInfoComponent will be added/managed by DialogueUISyncSystem
```

**System Setup:**

Ensure DialogueUISyncSystem runs after the system that updates DialogueUIComponent (e.g., YarnDialogueSystem) and before DrawSystem and TextSystem in the update loop.

```csharp
// In system setup
_updateSystems = new SequentialSystem<GameTime>(
    // ... input, collision, other logic ...
    _yarnSystem, // Updates DialogueUIComponent state
    _dialogueUISyncSystem, // Reads state, updates Draw/Text components
    // ... other update systems ...
);

_drawSystems = new SequentialSystem<GameTime>(
    // Assumes DrawSystem and TextSystem are part of the engine
    _drawSystem, // Processes DrawInfoComponent
    _textSystem // Processes TextInfoComponent
    // ... other draw systems ...
);
```

**Runtime Flow:**
Dialogue system (e.g., Yarn) sets DialogueUIComponent.IsVisible = true, updates CurrentLineText, etc.
DialogueUISyncSystem runs, detects IsVisible = true. It adds/updates Position, clears and repopulates DrawInfoComponent.Elements and TextInfoComponent.Elements based on the current DialogueUIComponent state and configuration (asset names, relative positions, text content). Sets NeedsAssetLoading if components were new.DrawSystem runs. If NeedsAssetLoading is true, it loads textures specified in DrawElement.TextureAssetName. It then iterates through DrawInfoComponent.Elements and draws each texture using SpriteBatch at Position.Value + DrawElement.RelativePosition.TextSystem runs. If NeedsAssetLoading is true, it loads fonts specified in TextElement.FontAssetName. It then iterates through TextInfoComponent.Elements, performs text wrapping if WrapWidth is set, and draws each text block using SpriteBatch at Position.Value + TextElement.RelativePosition.
When dialogue ends, the dialogue system sets IsVisible = false.DialogueUISyncSystem detects IsVisible = false and removes DrawInfoComponent and TextInfoComponent from the UI entity, causing DrawSystem and TextSystem to no longer render it.

*Backwards Compatibility*
This proposal replaces a dedicated DialogueDrawSystem with a synchronization approach. It is not backwards compatible with code expecting the old system. However, it increases compatibility with engines that already utilize generic DrawSystem and TextSystem based on components holding lists of elements. Adopting this requires implementing DialogueUISyncSystem and ensuring the generic rendering systems can handle the specified DrawInfoComponent and TextInfoComponent structures (including asset loading and list processing).Security ConsiderationsNone perceived specific to this rendering approach. Asset names are defined by the developer during UI setup via the DialogueUIBuilder.Open IssuesLayout Precision: Calculating accurate relative positions for text elements (especially options appearing below wrapped text) within DialogueUISyncSystem is difficult without access to SpriteFont.MeasureString. The current implementation uses estimates. This limitation might necessitate:Passing ContentManager or loaded SpriteFont references to DialogueUISyncSystem (reduces decoupling).
Enhancing the generic TextSystem to perform layout calculations based on the list of TextElements and their properties (preferred approach for modularity).Simplifying the required layout (e.g., fixed number of lines for main text, fixed positions for options).Performance: Clearing and repopulating List<DrawElement> and List<TextElement> every frame while the UI is visible might incur some overhead (garbage collection pressure due to list resizing, iteration cost). For typical dialogue UIs, this is likely negligible. If profiling indicates an issue for highly complex or rapidly