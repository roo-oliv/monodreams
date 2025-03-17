using System.Globalization;
using CsvHelper;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Objects;
using MonoDreams.State;
using MonoDreams.YarnSpinner;
using MonoGame.Extended.BitmapFonts;
using Yarn;

namespace MonoDreams.Examples.Dialogue;

// Define a simple class for CSV parsing
public class StringTableEntry
{
    public string id { get; set; } = string.Empty;
    public string text { get; set; } = string.Empty;
    public string file { get; set; } = string.Empty;
    public string node { get; set; } = string.Empty;
    public int lineNumber { get; set; }
}

public enum DialogueState
{
    Inactive,
    Line,
    Options,
    Complete
}

public class DialogueRunner
{
    private const string TextLanguage = "en";
    
    private Yarn.Dialogue _dialogue;
    private IVariableStorage _variableStorage;
    private readonly World _world;
    private readonly BitmapFont _dialogueFont;
    private readonly Texture2D _emoteTexture;
    private readonly Texture2D _dialogueBoxTexture;
    private readonly RenderTarget2D _renderTarget;
    private readonly GraphicsDevice _graphicsDevice;
    private Entity _currentDialogueEntity = default;
    private Entity _currentOptionsEntity = default;
    private Dictionary<string, string> _strings = new();
    private Dictionary<string, Texture2D> _emoteTextures = new();
    private Dictionary<string, Color> _characterColors = new();
    private bool _isWaitingForOptionSelection = false;
    
    // Events for other systems to subscribe to
    public event Action<string>? OnNodeStart;
    public event Action<string>? OnNodeComplete;
    public event Action? OnDialogueComplete;
    public event Action<Line>? OnLineDelivered;
    public event Action<OptionSet>? OnOptionsPresented;
    public event Action<int>? OnOptionSelected;
    public event Action<string[]>? OnCommand;
    public event Action? OnContinueDialogue;

    // Dialogue state
    public bool IsRunning => _dialogue.IsActive;
    public bool IsWaitingForOptionSelection => _isWaitingForOptionSelection;
    public string? CurrentNode { get; private set; }

    public DialogueRunner(
        World world,
        BitmapFont dialogueFont,
        Texture2D emoteTexture,
        Texture2D dialogueBoxTexture,
        RenderTarget2D renderTarget,
        GraphicsDevice graphicsDevice)
    {
        _world = world;
        _dialogueFont = dialogueFont;
        _emoteTexture = emoteTexture;
        _dialogueBoxTexture = dialogueBoxTexture;
        _renderTarget = renderTarget;
        _graphicsDevice = graphicsDevice;
        
        _variableStorage = new InMemoryVariableStorage();
        _dialogue = new Yarn.Dialogue(_variableStorage);
        
        _dialogue.LogDebugMessage = message => Console.WriteLine($"[Yarn Debug] {message}");
        _dialogue.LogErrorMessage = message => Console.WriteLine($"[Yarn Error] {message}");
        
        // Set up handlers
        _dialogue.LineHandler = HandleLine;
        _dialogue.OptionsHandler = HandleOptions;
        _dialogue.CommandHandler = HandleCommand;
        _dialogue.NodeStartHandler = HandleNodeStart;
        _dialogue.NodeCompleteHandler = HandleNodeComplete;
        _dialogue.DialogueCompleteHandler = HandleDialogueComplete;
    }

    public void AddStringTable(YarnProgram program)
    {
        try
        {
            Console.WriteLine("[Debug] Adding string table from Yarn program");
            
            if (program.CompiledProgram == null || program.CompiledProgram.Length == 0)
            {
                Console.WriteLine("[Error] Yarn program is null or empty");
                return;
            }
            
            // Load the compiled program
            using (var memoryStream = new MemoryStream(program.CompiledProgram))
            {
                var compiledProgram = Yarn.Program.Parser.ParseFrom(memoryStream);
                Console.WriteLine($"[Debug] Loaded program with {compiledProgram.Nodes.Count} nodes");
                
                if (compiledProgram.Nodes.Count > 0)
                {
                    Console.WriteLine("[Debug] Available nodes:");
                    foreach (var node in compiledProgram.Nodes)
                    {
                        Console.WriteLine($"[Debug] - {node.Key}");
                    }
                }
                else
                {
                    Console.WriteLine("[Warning] No nodes found in the compiled program");
                }
                
                _dialogue.SetProgram(compiledProgram);
            }
            
            // Load the string table
            if (!string.IsNullOrEmpty(program.BaseLocalisationStringTable))
            {
                try
                {
                    using (var stringReader = new StringReader(program.BaseLocalisationStringTable))
                    using (var csv = new CsvReader(stringReader, CultureInfo.InvariantCulture))
                    {
                        var records = csv.GetRecords<StringTableEntry>().ToList();
                        foreach (var record in records)
                        {
                            _strings[record.id] = record.text;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Failed to parse string table: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to add string table: {ex.Message}");
        }
    }
    
    public bool HasNode(string nodeName)
    {
        try
        {
            if (_dialogue == null)
            {
                Console.WriteLine($"[Debug] Cannot check for node '{nodeName}': Dialogue is null");
                return false;
            }
            
            // We can't directly access _dialogue.Program, so we'll use a different approach
            // Try to set the node and see if it succeeds
            try
            {
                _dialogue.SetNode(nodeName);
                // If we get here, the node exists
                Console.WriteLine($"[Debug] Node '{nodeName}' exists: true");
                return true;
            }
            catch (Exception)
            {
                // If setting the node fails, the node doesn't exist
                Console.WriteLine($"[Debug] Node '{nodeName}' exists: false");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Error checking for node '{nodeName}': {ex.Message}");
            return false;
        }
    }

    public void StartDialogue(string nodeName)
    {
        try
        {
            Console.WriteLine($"[Debug] Attempting to start dialogue at node: {nodeName}");
            
            // Try to set the node and continue
            try
            {
                CurrentNode = nodeName;
                _dialogue.SetNode(nodeName);
                _dialogue.Continue();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to start dialogue at node '{nodeName}': {ex.Message}");
                
                // List available nodes if possible
                Console.WriteLine("[Debug] Available nodes (if any):");
                // We can't directly access the nodes, so we'll just notify that we can't list them
                Console.WriteLine("[Debug] Unable to list available nodes due to API limitations");
                
                OnDialogueComplete?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to start dialogue: {ex.Message}");
            OnDialogueComplete?.Invoke();
        }
    }
    
    public void ContinueDialogue()
    {
        OnContinueDialogue?.Invoke();
    }

    public void Continue()
    {
        if (!IsRunning || IsWaitingForOptionSelection) return;
        
        // Cleanup existing dialogue UI before continuing
        if (_currentDialogueEntity != default)
        {
            _currentDialogueEntity.Dispose();
            _currentDialogueEntity = default;
        }
        
        _dialogue.Continue();
    }

    public void SelectOption(int optionIndex)
    {
        if (!IsWaitingForOptionSelection) return;
        
        // Cleanup options UI
        if (_currentOptionsEntity != default)
        {
            _currentOptionsEntity.Dispose();
            _currentOptionsEntity = default;
        }
        
        // Update state
        _isWaitingForOptionSelection = false;
        
        // Select the option in the dialogue
        _dialogue.SetSelectedOption(optionIndex);
        
        // Continue the dialogue
        _dialogue.Continue();
    }
    
    // Expose the variable storage for the DialogueManager
    public IVariableStorage GetVariableStorage()
    {
        return _variableStorage;
    }
    
    private void HandleLine(Line line)
    {
        Console.WriteLine($"[Debug] Line: {line}");
        
        // Create a dialogue entity to display the line
        var characterName = "Unknown";
        var text = line.ToString();
        
        // Try to get the text from the string table if available
        if (_strings.TryGetValue(text, out var localizedText))
        {
            text = localizedText;
        }
        
        // Try to extract character name if the text contains a colon
        if (text.Contains(":"))
        {
            var parts = text.Split(':', 2);
            characterName = parts[0].Trim();
            text = parts[1].Trim();
        }
        
        // Get character color
        var textColor = Color.White;
        if (!string.IsNullOrEmpty(characterName) && _characterColors.ContainsKey(characterName))
        {
            textColor = _characterColors[characterName];
        }
        
        // Get character emote texture
        var emoteTexture = _emoteTexture;
        if (!string.IsNullOrEmpty(characterName) && _emoteTextures.ContainsKey(characterName))
        {
            emoteTexture = _emoteTextures[characterName];
        }
        
        // Create the dialogue entity
        _currentDialogueEntity = Objects.Dialogue.Create(
            _world,
            text,
            emoteTexture,
            _dialogueFont,
            _dialogueBoxTexture,
            _renderTarget,
            _graphicsDevice,
            textColor
        );
        
        // Notify subscribers
        OnLineDelivered?.Invoke(line);
    }
    
    private void HandleOptions(OptionSet optionSet)
    {
        Console.WriteLine($"[Debug] Options: {optionSet.Options.Length} options");
        
        // Update state
        _isWaitingForOptionSelection = true;
        
        // Create an options entity to display the options
        var options = new string[optionSet.Options.Length];
        
        for (int i = 0; i < optionSet.Options.Length; i++)
        {
            var optionText = optionSet.Options[i].Line.ToString();
            
            // Try to get the text from the string table if available
            if (_strings.TryGetValue(optionText, out var localizedText))
            {
                optionText = localizedText;
            }
            
            // Try to extract just the text part if it contains a colon
            if (optionText.Contains(":"))
            {
                var parts = optionText.Split(':', 2);
                optionText = parts[1].Trim();
            }
            
            options[i] = optionText;
        }
        
        _currentOptionsEntity = Objects.Dialogue.CreateOptions(
            _world,
            options,
            _dialogueFont,
            _dialogueBoxTexture,
            _renderTarget,
            _graphicsDevice
        );
        
        // Notify subscribers
        OnOptionsPresented?.Invoke(optionSet);
    }
    
    private void HandleCommand(Command command)
    {
        Console.WriteLine($"[Debug] Command: {command.Text}");
        
        // Parse the command
        var commandText = command.Text.Trim();
        var commandParts = commandText.Split(' ');
        var commandName = commandParts[0];
        
        // Handle specific commands
        switch (commandName)
        {
            case "set_var":
                if (commandParts.Length >= 3)
                {
                    var varName = commandParts[1];
                    var varValue = commandParts[2];
                    
                    if (bool.TryParse(varValue, out bool boolValue))
                    {
                        _variableStorage.SetValue(varName, boolValue);
                    }
                    else if (float.TryParse(varValue, out float floatValue))
                    {
                        _variableStorage.SetValue(varName, floatValue);
                    }
                    else
                    {
                        _variableStorage.SetValue(varName, varValue);
                    }
                }
                break;
                
            case "emote":
                if (commandParts.Length >= 3)
                {
                    var characterName = commandParts[1];
                    var emoteName = commandParts[2];
                    
                    Console.WriteLine($"[Debug] Emote: {characterName} -> {emoteName}");
                    // TODO: Handle emote changes
                }
                break;
                
            default:
                // Notify listeners of the command
                OnCommand?.Invoke(commandParts);
                break;
        }
        
        // Continue the dialogue
        _dialogue.Continue();
    }
    
    private void HandleNodeStart(string nodeName)
    {
        Console.WriteLine($"[Debug] Node start: {nodeName}");
        OnNodeStart?.Invoke(nodeName);
    }
    
    private void HandleNodeComplete(string nodeName)
    {
        Console.WriteLine($"[Debug] Node complete: {nodeName}");
        OnNodeComplete?.Invoke(nodeName);
    }
    
    private void HandleDialogueComplete()
    {
        Console.WriteLine("[Debug] Dialogue complete");
        
        // Clean up any remaining UI
        if (_currentDialogueEntity != default)
        {
            _currentDialogueEntity.Dispose();
            _currentDialogueEntity = default;
        }
        
        if (_currentOptionsEntity != default)
        {
            _currentOptionsEntity.Dispose();
            _currentOptionsEntity = default;
        }
        
        // Reset state
        _isWaitingForOptionSelection = false;
        CurrentNode = null;
        
        // Notify listeners
        OnDialogueComplete?.Invoke();
    }
    
    public void SetCharacterColor(string characterName, Color color)
    {
        _characterColors[characterName] = color;
    }
    
    public void LoadEmoteTexture(string characterName, Texture2D texture)
    {
        _emoteTextures[characterName] = texture;
    }
}
