using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

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
            presenter.TextRevealProgress += state.Time * presenter.TextSpeed;
            if (presenter.TextRevealProgress > 1.0f)
            {
                presenter.TextRevealProgress = 1.0f;
            }
        }
    }
    
    // protected override void Draw(GameState state)
    // {
    //     _spriteBatch.Begin();
    //     
    //     foreach (var entity in GetEntities())
    //     {
    //         var dialogueState = entity.Get<DialogueState>();
    //         var presenter = entity.Get<DialoguePresenter>();
    //         
    //         // Only render active dialogues
    //         if (!dialogueState.IsActive) continue;
    //         
    //         // Draw dialogue box
    //         _spriteBatch.Draw(
    //             presenter.DialogueBoxTexture, 
    //             (Rectangle)presenter.DialogueBoxBounds, 
    //             Color.White);
    //         
    //         // Draw portrait if available
    //         if (presenter.PortraitTexture != null)
    //         {
    //             _spriteBatch.Draw(
    //                 presenter.PortraitTexture,
    //                 (Rectangle)presenter.PortraitBounds,
    //                 Color.White);
    //         }
    //         
    //         // Draw text (with reveal animation if enabled)
    //         string textToShow = dialogueState.CurrentText;
    //         if (presenter.RevealTextGradually && presenter.TextRevealProgress < 1.0f)
    //         {
    //             int charCount = (int)(textToShow.Length * presenter.TextRevealProgress);
    //             textToShow = textToShow.Substring(0, charCount);
    //         }
    //         
    //         _spriteBatch.DrawString(
    //             presenter.Font,
    //             textToShow,
    //             new Vector2(presenter.DialogueBoxBounds.X + 20, presenter.DialogueBoxBounds.Y + 20),
    //             presenter.TextColor);
    //         
    //         // Draw options if any
    //         if (dialogueState.CurrentNodeType == DialogueNodeType.Options)
    //         {
    //             for (int i = 0; i < dialogueState.CurrentOptions.Count; i++)
    //             {
    //                 Color optionColor = i == dialogueState.SelectedOptionIndex 
    //                     ? Color.Yellow 
    //                     : Color.White;
    //                 
    //                 _spriteBatch.DrawString(
    //                     presenter.Font,
    //                     (string)dialogueState.CurrentOptions[i],
    //                     new Vector2(
    //                         presenter.DialogueBoxBounds.X + 40, 
    //                         presenter.DialogueBoxBounds.Y + 80 + i * 30),
    //                     optionColor);
    //             }
    //         }
    //         
    //         // Draw input prompt
    //         if (dialogueState.WaitingForInput)
    //         {
    //             _spriteBatch.DrawString(
    //                 presenter.Font,
    //                 "Press Space to continue",
    //                 new Vector2(
    //                     presenter.DialogueBoxBounds.Right - 200,
    //                     presenter.DialogueBoxBounds.Bottom - 30),
    //                 Color.LightGray);
    //         }
    //     }
    //     
    //     _spriteBatch.End();
    // }
}
