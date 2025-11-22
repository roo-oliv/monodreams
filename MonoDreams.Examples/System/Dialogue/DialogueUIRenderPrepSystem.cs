using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Dialogue;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

// For Math.Min/Max if needed for text slicing (though text is passed whole here)

namespace MonoDreams.Examples.System.Dialogue;

/// <summary>
/// Prepares DrawElements for the Dialogue UI based on DialogueUIStateComponent.
/// Populates the DrawComponent of the Dialogue UI entity.
/// </summary>
[With(typeof(DialogueUIStateComponent), typeof(Transform), typeof(DrawComponent))]
public sealed class DialogueUIRenderPrepSystem : AEntitySetSystem<GameState>
{
    // Define layer depth constants for UI elements for clarity
    private const float DIALOGUE_BOX_LAYER_OFFSET = -0.02f;
    private const float DIALOGUE_EMOTE_LAYER_OFFSET = -0.01f;
    private const float DIALOGUE_TEXT_LAYER_OFFSET = 0.00f; // Highest logically
    private const float DIALOGUE_INDICATOR_LAYER_OFFSET = 0.00f;

    public DialogueUIRenderPrepSystem(World world)
        : base(world) // Run sequentially by default
    { }

    protected override void Update(GameState state, in Entity entity)
    {
        // Get components
        ref readonly var transform = ref entity.Get<Transform>();
        ref readonly var uiState = ref entity.Get<DialogueUIStateComponent>();
        ref var drawComponent = ref entity.Get<DrawComponent>();

        // // Clear previous drawables *related to this UI*
        // // More robust: Tag DrawElements by system/type or use a dedicated Clear system before prep systems.
        // // Simple approach: Assume this system manages all DrawElements on this specific UI entity.
        // drawComponent.Drawables.Clear();

        // Only proceed if the UI is active
        if (!uiState.IsActive)
        {
            return;
        }

        // --- Base layer depth for UI ---
        // Assuming UI layer is distinct. Get base depth for UI elements.
        // You might have a helper or constant for this. Let's assume UI layer corresponds to depth 0.5f
        float baseLayerDepth = 0.5f; // Example: Higher values are further back with SpriteSortMode.FrontToBack

        Vector2 basePos = transform.Position;

        // --- 1. Prepare Dialogue Box (9-Patch) ---
        if (uiState.DialogueBoxTexture != null)
        {
            // Use the helper method to generate the 9 DrawElements for the 9-patch
            AddNinePatchDrawElements(
                ref drawComponent,
                uiState.DialogueBoxTexture,
                uiState.DialogueBoxNinePatch,
                basePos + uiState.DialogueBoxOffset,
                uiState.DialogueBoxSize,
                Color.White, // Example color, could be configurable
                baseLayerDepth + DIALOGUE_BOX_LAYER_OFFSET, // Box behind other elements
                RenderTargetID.UI);
        }
            
        // // --- 2. Prepare Speaker Emote ---
        // if (uiState.SpeakerEmoteTexture != null)
        // {
        //     drawComponent.Drawables.Add(new DrawElement
        //     {
        //         Type = DrawElementType.Sprite,
        //         Target = RenderTargetID.UI,
        //         Texture = uiState.SpeakerEmoteTexture,
        //         Position = basePos + uiState.EmoteOffset,
        //         SourceRectangle = null, // Draw whole texture
        //         Size = new Vector2(uiState.SpeakerEmoteTexture.Width * 4, uiState.SpeakerEmoteTexture.Height * 4),
        //         Color = Color.White,
        //         LayerDepth = baseLayerDepth + DIALOGUE_EMOTE_LAYER_OFFSET
        //     });
        // }
                        
        // // --- 3. Prepare Dialogue Text ---
        // if (!string.IsNullOrEmpty(uiState.CurrentText) && uiState.DialogueFont != null)
        // {
        //     // drawComponent.Drawables.Add(new DrawElement
        //     // {
        //     //     Type = DrawElementType.Text,
        //     //     Target = RenderTargetID.UI,
        //     //     Text = uiState.CurrentText, // Pass the whole text for now
        //     //     Font = uiState.DialogueFont,
        //     //     Position = basePos + uiState.TextOffset, // Top-left position of text area
        //     //     Color = uiState.TextColor,
        //     //     LayerDepth = baseLayerDepth + DIALOGUE_TEXT_LAYER_OFFSET // Text on top
        //     // });
        //     BitmapFont font = uiState.DialogueFont;
        //     string textToDraw = uiState.CurrentText;
        //     Vector2 textDrawPos = basePos + uiState.TextOffset; // Top-left anchor for text
        //     Color textColor = uiState.TextColor;
        //     float textLayerDepth = baseLayerDepth + DIALOGUE_TEXT_LAYER_OFFSET;
        //
        //     // Get glyphs for the *entire* string. We'll render only the visible ones later.
        //     // Note: This calculates layout for the whole string. Might optimize later if needed.
        //     var glyphs = font.GetGlyphs(textToDraw, textDrawPos); // Get glyphs relative to textDrawPos
        //
        //     // Determine how many glyphs to actually draw based on text reveal state (from DialogueUpdateSystem)
        //     // Simplistic approach: Render glyphs whose character index is less than VisibleCharacterCount
        //     // More complex logic might be needed for multi-glyph characters or precise timing.
        //     var glyphIndex = 0;
        //     foreach (var glyph in glyphs)
        //     {
        //         // Only draw glyph if its corresponding character index is visible
        //         if (glyphIndex >= uiState.VisibleCharacterCount) continue;
        //         glyphIndex++; // Increment for each glyph processed
        //
        //         // Each glyph is drawn as a sprite
        //         drawComponent.Drawables.Add(new DrawElement {
        //             Type = DrawElementType.Sprite,
        //             Target = RenderTargetID.UI,
        //             Texture = glyph.Character.TextureRegion.Texture, // Texture page for this glyph
        //             SourceRectangle = glyph.Character.TextureRegion.Bounds, // Source rect within the texture page
        //             Position = glyph.Position, // Position calculated by GetGlyphs() relative to textDrawPos
        //             Size = glyph.Character.TextureRegion.Bounds.Size.ToVector2(), // Use glyph size
        //             Color = textColor,
        //             LayerDepth = textLayerDepth
        //         });
        //     }
        // }
                                    
        // // --- 4. Prepare Next Indicator ---
        // if (uiState.ShowNextIndicator && uiState.NextIndicatorTexture != null)
        // {
        //     drawComponent.Drawables.Add(new DrawElement
        //     {
        //         Type = DrawElementType.Sprite,
        //         Target = RenderTargetID.UI,
        //         Texture = uiState.NextIndicatorTexture,
        //         Position = basePos + uiState.NextIndicatorOffset,
        //         SourceRectangle = null,
        //         Size = new Vector2(uiState.NextIndicatorTexture.Width, uiState.NextIndicatorTexture.Height) * 4,
        //         Color = Color.White,
        //         LayerDepth = baseLayerDepth + DIALOGUE_INDICATOR_LAYER_OFFSET // Same level as text?
        //     });
        // }
    }
        
    /// <summary>
    /// Calculates and adds the 9 DrawElements needed to render a 9-patch sprite.
    /// Assumes NinePatchInfo contains margin definitions relative to the source texture.
    /// </summary>
    private void AddNinePatchDrawElements(ref DrawComponent drawComp, Texture2D texture, NinePatchInfo ninePatchDef, Vector2 pos, Vector2 size, Color color, float layerDepth, RenderTargetID target)
    {
        // Assuming texture is the full source for the 9-patch
        int texWidth = texture.Width;
        int texHeight = texture.Height;
        
        // Extract margins (assuming they define the stretchable center)
        int marginLeft = 16; // ninePatchDef.MarginLeft;
        int marginTop = 16; // ninePatchDef.MarginTop;
        int marginRight = 16; // ninePatchDef.MarginRight;
        int marginBottom = 28; // ninePatchDef.MarginBottom;
        
        // Calculate dimensions of fixed and stretchable areas from the source texture
        int fixedWidthLeft = marginLeft;
        int fixedWidthRight = marginRight;
        int fixedHeightTop = marginTop;
        int fixedHeightBottom = marginBottom;
        int stretchableWidthSource = texWidth - marginLeft - marginRight;
        int stretchableHeightSource = texHeight - marginTop - marginBottom;
        
        // Calculate dimensions of destination areas
        int fixedWidthDest = fixedWidthLeft + fixedWidthRight;
        int fixedHeightDest = fixedHeightTop + fixedHeightBottom;
        int stretchableWidthDest = Math.Max(0, (int)size.X - fixedWidthDest);
        int stretchableHeightDest = Math.Max(0, (int)size.Y - fixedHeightDest);
        
        // Define Source Rectangles (relative to the texture)
        Rectangle srcTL = new Rectangle(0, 0, fixedWidthLeft, fixedHeightTop);
        Rectangle srcT = new Rectangle(marginLeft, 0, stretchableWidthSource, fixedHeightTop);
        Rectangle srcTR = new Rectangle(texWidth - fixedWidthRight, 0, fixedWidthRight, fixedHeightTop);
        Rectangle srcL = new Rectangle(0, marginTop, fixedWidthLeft, stretchableHeightSource);
        Rectangle srcC = new Rectangle(marginLeft, marginTop, stretchableWidthSource, stretchableHeightSource);
        Rectangle srcR = new Rectangle(texWidth - fixedWidthRight, marginTop, fixedWidthRight, stretchableHeightSource);
        Rectangle srcBL = new Rectangle(0, texHeight - fixedHeightBottom, fixedWidthLeft, fixedHeightBottom);
        Rectangle srcB = new Rectangle(marginLeft, texHeight - fixedHeightBottom, stretchableWidthSource, fixedHeightBottom);
        Rectangle srcBR = new Rectangle(texWidth - fixedWidthRight, texHeight - fixedHeightBottom, fixedWidthRight, fixedHeightBottom);
        
        // Define Destination Positions and Sizes (relative to pos)
        Vector2 posTL = pos;
        Vector2 posT = pos + new Vector2(fixedWidthLeft, 0);
        Vector2 posTR = pos + new Vector2(fixedWidthLeft + stretchableWidthDest, 0);
        Vector2 posL = pos + new Vector2(0, fixedHeightTop);
        Vector2 posC = pos + new Vector2(fixedWidthLeft, fixedHeightTop);
        Vector2 posR = pos + new Vector2(fixedWidthLeft + stretchableWidthDest, fixedHeightTop);
        Vector2 posBL = pos + new Vector2(0, fixedHeightTop + stretchableHeightDest);
        Vector2 posB = pos + new Vector2(fixedWidthLeft, fixedHeightTop + stretchableHeightDest);
        Vector2 posBR = pos + new Vector2(fixedWidthLeft + stretchableWidthDest, fixedHeightTop + stretchableHeightDest);
        
        Vector2 sizeTL = new Vector2(fixedWidthLeft, fixedHeightTop);
        Vector2 sizeT = new Vector2(stretchableWidthDest, fixedHeightTop);
        Vector2 sizeTR = new Vector2(fixedWidthRight, fixedHeightTop);
        Vector2 sizeL = new Vector2(fixedWidthLeft, stretchableHeightDest);
        Vector2 sizeC = new Vector2(stretchableWidthDest, stretchableHeightDest);
        Vector2 sizeR = new Vector2(fixedWidthRight, stretchableHeightDest);
        Vector2 sizeBL = new Vector2(fixedWidthLeft, fixedHeightBottom);
        Vector2 sizeB = new Vector2(stretchableWidthDest, fixedHeightBottom);
        Vector2 sizeBR = new Vector2(fixedWidthRight, fixedHeightBottom);
        
        // Create DrawElements - Ensure sizes are non-negative
        if (sizeTL.X > 0 && sizeTL.Y > 0) AddElement(ref drawComp, texture, target, posTL, sizeTL, srcTL, color, layerDepth); // Top Left
        if (sizeT.X > 0 && sizeT.Y > 0)   AddElement(ref drawComp, texture, target, posT, sizeT, srcT, color, layerDepth);    // Top
        if (sizeTR.X > 0 && sizeTR.Y > 0) AddElement(ref drawComp, texture, target, posTR, sizeTR, srcTR, color, layerDepth); // Top Right
        if (sizeL.X > 0 && sizeL.Y > 0)   AddElement(ref drawComp, texture, target, posL, sizeL, srcL, color, layerDepth);    // Left
        if (sizeC.X > 0 && sizeC.Y > 0)   AddElement(ref drawComp, texture, target, posC, sizeC, srcC, color, layerDepth);    // Center
        if (sizeR.X > 0 && sizeR.Y > 0)   AddElement(ref drawComp, texture, target, posR, sizeR, srcR, color, layerDepth);    // Right
        if (sizeBL.X > 0 && sizeBL.Y > 0) AddElement(ref drawComp, texture, target, posBL, sizeBL, srcBL, color, layerDepth); // Bottom Left
        if (sizeB.X > 0 && sizeB.Y > 0)   AddElement(ref drawComp, texture, target, posB, sizeB, srcB, color, layerDepth);    // Bottom
        if (sizeBR.X > 0 && sizeBR.Y > 0) AddElement(ref drawComp, texture, target, posBR, sizeBR, srcBR, color, layerDepth); // Bottom Right
    }

    // Small helper to reduce repetition when adding elements
    private void AddElement(ref DrawComponent drawComp, Texture2D texture, RenderTargetID target, Vector2 pos, Vector2 size, Rectangle srcRect, Color color, float layerDepth)
    {
        // drawComp.Drawables.Add(new DrawElement
        // {
        //     Type = DrawElementType.Sprite,
        //     Target = target,
        //     Texture = texture,
        //     Position = pos,
        //     Size = size,
        //     SourceRectangle = srcRect,
        //     Color = color,
        //     LayerDepth = layerDepth
        // });
    }
}