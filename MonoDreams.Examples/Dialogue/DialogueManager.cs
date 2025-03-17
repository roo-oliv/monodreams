using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using Yarn;
using System;

namespace MonoDreams.Examples.Dialogue;

/// <summary>
/// A singleton class that manages dialogue state throughout the game
/// </summary>
public class DialogueManager
{
    private static DialogueManager _instance;
    
    // Singleton access
    public static DialogueManager Instance => _instance ??= new DialogueManager();
    
    // The dialogue runner
    private DialogueRunner _dialogueRunner;
    
    // Private constructor to prevent direct instantiation
    private DialogueManager() {}
    
    // Initialize the dialogue manager
    public void Initialize(
        World world,
        BitmapFont dialogueFont,
        Texture2D emoteTexture,
        Texture2D dialogueBoxTexture,
        RenderTarget2D renderTarget,
        GraphicsDevice graphicsDevice)
    {
        _dialogueRunner = new DialogueRunner(
            world,
            dialogueFont,
            emoteTexture,
            dialogueBoxTexture,
            renderTarget,
            graphicsDevice
        );
    }
    
    // Get the dialogue runner
    public DialogueRunner GetDialogueRunner()
    {
        if (_dialogueRunner == null)
        {
            throw new InvalidOperationException("DialogueManager has not been initialized!");
        }
        return _dialogueRunner;
    }
    
    // Get the variable storage
    private InMemoryVariableStorage GetVariableStorage()
    {
        var runner = GetDialogueRunner();
        return runner.GetVariableStorage() as InMemoryVariableStorage;
    }
    
    // Set character colors
    public void SetCharacterColor(string characterName, Color color)
    {
        GetDialogueRunner().SetCharacterColor(characterName, color);
    }
    
    // Load emote textures
    public void LoadEmoteTexture(string characterName, Texture2D texture)
    {
        GetDialogueRunner().LoadEmoteTexture(characterName, texture);
    }
    
    // Access to variable state
    public bool GetBoolValue(string variableName)
    {
        var variableStorage = GetVariableStorage();
        if (variableStorage != null && variableStorage.TryGetValue<bool>(variableName, out var value))
        {
            return value;
        }
        return false;
    }
    
    public float GetFloatValue(string variableName)
    {
        var variableStorage = GetVariableStorage();
        if (variableStorage != null && variableStorage.TryGetValue<float>(variableName, out var value))
        {
            return value;
        }
        return 0f;
    }
    
    public string GetStringValue(string variableName)
    {
        var variableStorage = GetVariableStorage();
        if (variableStorage != null && variableStorage.TryGetValue<string>(variableName, out var value))
        {
            return value;
        }
        return string.Empty;
    }
    
    public void SetValue(string variableName, bool value)
    {
        var variableStorage = GetVariableStorage();
        variableStorage?.SetValue(variableName, value);
    }
    
    public void SetValue(string variableName, float value)
    {
        var variableStorage = GetVariableStorage();
        variableStorage?.SetValue(variableName, value);
    }
    
    public void SetValue(string variableName, string value)
    {
        var variableStorage = GetVariableStorage();
        variableStorage?.SetValue(variableName, value);
    }
} 