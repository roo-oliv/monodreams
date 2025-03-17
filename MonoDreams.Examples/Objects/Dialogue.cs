using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Screens;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Objects;

public static class Dialogue
{
    public static Entity Create(
        World world, 
        string text, 
        Texture2D emoteTexture, 
        BitmapFont font, 
        Texture2D dialogBoxTexture, 
        RenderTarget2D renderTarget, 
        GraphicsDevice graphicsDevice, 
        Color textColor = default,
        Enum boxDrawLayer = null, 
        Enum textDrawLayer = null)
    {
        // If text color wasn't specified, use white
        if (textColor == default)
        {
            textColor = Color.White;
        }

        // Create the text entity
        var textEntity = world.CreateEntity();
        textEntity.Set(new EntityInfo(EntityType.Interface));
        textEntity.Set(new DynamicText(
            renderTarget, 
            text, 
            font,
            revealingSpeed: 20,
            drawLayer: DreamGameScreen.DrawLayer.UIElements,
            maxLineWidth: 300
        ));
        textEntity.Set(new Position(new Vector2(-1150, -900)));
        
        // Create the dialogue box background
        var dialogBox = world.CreateEntity();
        dialogBox.Set(new EntityInfo(EntityType.Interface));
        dialogBox.Set(new Position(new Vector2(-1400, -1100)));
        dialogBox.Set(
            new DrawInfo(
                renderTarget,
                dialogBoxTexture,
                new Point(3300, 720),
                layer: boxDrawLayer,
                ninePatchInfo: new NinePatchInfo(
                    96,
                    new Rectangle(0, 0, 23, 23),
                    new Rectangle(23, 0, 1, 23),
                    new Rectangle(24, 0, 23, 23),
                    new Rectangle(0, 23, 23, 1),
                    new Rectangle(23, 23, 1, 1),
                    new Rectangle(24, 23, 23, 1),
                    new Rectangle(0, 24, 23, 23),
                    new Rectangle(23, 24, 1, 23),
                    new Rectangle(24, 24, 23, 23)
                    ),
                graphicsDevice: graphicsDevice
                )
            );
        
        // Create the character emote/portrait
        var emote = world.CreateEntity();
        emote.Set(new EntityInfo(EntityType.Interface));
        emote.Set(new Position(new Vector2(-1900, -1000)));
        emote.Set(new DrawInfo(renderTarget, emoteTexture, new Point(550, 550), layer: boxDrawLayer));
        
        // Store child entities to allow for cleanup
        textEntity.Set(new DialogueUIComponent
        {
            DialogueBox = dialogBox,
            EmoteBox = emote
        });
        
        return textEntity;
    }
    
    public static Entity CreateOptions(
        World world, 
        string[] options, 
        BitmapFont font, 
        Texture2D optionBoxTexture, 
        RenderTarget2D renderTarget, 
        GraphicsDevice graphicsDevice, 
        int selectedOption = 0,
        Enum boxDrawLayer = null)
    {
        // Create parent entity for options
        var optionsEntity = world.CreateEntity();
        optionsEntity.Set(new EntityInfo(EntityType.Interface));
        
        // Create options entities
        var optionEntities = new Entity[options.Length];
        
        for (int i = 0; i < options.Length; i++)
        {
            // Create option text
            var optionText = world.CreateEntity();
            optionText.Set(new EntityInfo(EntityType.Interface));
            
            // Highlight selected option
            var color = i == selectedOption ? Color.Yellow : Color.White;
            
            optionText.Set(new DynamicText(
                renderTarget,
                options[i],
                font,
                revealingSpeed: 0,
                drawLayer: DreamGameScreen.DrawLayer.UIElements,
                maxLineWidth: 300
            ));
            
            // Position options one below the other
            optionText.Set(new Position(new Vector2(-1150, -600 + (i * 150))));
            
            optionEntities[i] = optionText;
        }
        
        // Store child entities to allow for cleanup
        optionsEntity.Set(new DialogueOptionsComponent
        {
            OptionEntities = optionEntities
        });
        
        return optionsEntity;
    }
}