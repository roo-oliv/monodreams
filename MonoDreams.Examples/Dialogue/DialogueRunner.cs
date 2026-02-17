using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using MonoDreams.YarnSpinner;
using Yarn;
using Yarn.Markup;

namespace MonoDreams.Examples.Dialogue;

public class DialogueRunner
{
    private const string TextLanguage = "en";

    private readonly Dictionary<string, string> _strings = new();

    public void AddStringTable(YarnProgram yarnScript)
    {
        string? textToLoad = null;

        if (yarnScript.Localizations != null && yarnScript.Localizations.Length > 0)
        {
            textToLoad = Array.Find(yarnScript.Localizations, element => element.LanguageName == TextLanguage)?.Text;
        }

        if (string.IsNullOrEmpty(textToLoad))
        {
            textToLoad = yarnScript.BaseLocalisationStringTable;
        }

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture);

        using var reader = new StringReader(textToLoad);
        using var csv = new CsvReader(reader, configuration);
        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            _strings[csv.GetField("id")!] = csv.GetField("text")!;
        }
    }

    public string GetLocalizedTextForLine(Line line)
    {
        if (!_strings.TryGetValue(line.ID, out var result)) return line.ID;

        var lineParser = new LineParser();
        var builtInReplacer = new BuiltInMarkupReplacer();
        lineParser.RegisterMarkerProcessor("select", builtInReplacer);
        lineParser.RegisterMarkerProcessor("ordinal", builtInReplacer);
        lineParser.RegisterMarkerProcessor("plural", builtInReplacer);

        result = LineParser.ExpandSubstitutions(result, line.Substitutions);
        result = lineParser.ParseString(result, "en").Text;

        return result;
    }
}
