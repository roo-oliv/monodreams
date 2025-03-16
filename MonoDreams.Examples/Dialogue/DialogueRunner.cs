using System.Globalization;
using CsvHelper;
using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.YarnSpinner;
using MonoGame.Extended.BitmapFonts;
using Yarn;

namespace MonoDreams.Examples.Dialogue;

public class DialogueRunner()
{
    private const string TextLanguage = "en";
    
    // private Yarn.Dialogue _dialogue;
    // private IVariableStorage _variableStorage;
    // private readonly World _world;
    // private readonly BitmapFont _dialogueFont;
    // private readonly Texture2D _emoteTexture;
    // private readonly Texture2D _dialogueBoxTexture;
    // private readonly RenderTarget2D _renderTarget;
    // private readonly GraphicsDevice _graphicsDevice;
    private Entity? _currentDialogueEntity;
    private Dictionary<string, string> _strings = new();

    // public DialogueRunner(World world, BitmapFont dialogueFont, Texture2D emoteTexture, Texture2D dialogueBoxTexture,
    //     RenderTarget2D renderTarget,
    //     GraphicsDevice graphicsDevice,
    //     IVariableStorage? variableStorage = null)
    // {
    //     _variableStorage = variableStorage ?? new InMemoryVariableStorage();
    //     _dialogue = new Yarn.Dialogue(_variableStorage);
    //     _world = world;
    //     _dialogueFont = dialogueFont;
    //     _emoteTexture = emoteTexture;
    //     _dialogueBoxTexture = dialogueBoxTexture;
    //     _renderTarget = renderTarget;
    //     _graphicsDevice = graphicsDevice;
    // }
    
    public void AddStringTable(YarnProgram yarnScript)
    {
        string textToLoad = null;

        if (yarnScript.Localizations != null || yarnScript.Localizations.Length > 0)
        {
            textToLoad = Array.Find(yarnScript.Localizations, element => element.LanguageName == TextLanguage)?.Text;
        }

        if (textToLoad == null || string.IsNullOrEmpty(textToLoad))
        {
            textToLoad = yarnScript.BaseLocalisationStringTable;
        }

        var configuration =
            new CsvHelper.Configuration.Configuration(CultureInfo.InvariantCulture);
            
        using (var reader = new StringReader(textToLoad))
        using (var csv = new CsvReader(reader, configuration))
        {
            csv.Read();
            csv.ReadHeader();

            while (csv.Read())
            {
                _strings.Add(csv.GetField("id"), csv.GetField("text"));
            }
        }
    }
    
    public string GetLocalizedTextForLine(Line line)
    {
        if (!_strings.TryGetValue(line.ID, out var result)) return null;

        var lineParser = new Yarn.Markup.LineParser();
        var builtInReplacer = new Yarn.Markup.BuiltInMarkupReplacer();
        lineParser.RegisterMarkerProcessor("select", builtInReplacer);
        lineParser.RegisterMarkerProcessor("ordinal", builtInReplacer);
        lineParser.RegisterMarkerProcessor("plural", builtInReplacer);
        
        result = Yarn.Markup.LineParser.ExpandSubstitutions(result, line.Substitutions);
        result = lineParser.ParseString(result, "en").Text;

        return result;
    }
}
