using Microsoft.Xna.Framework.Content.Pipeline;

namespace MonoDreams.YarnSpinner;

[ContentImporter(
    ".yarn",
    DefaultProcessor = "YarnSpinnerProcessor",
    DisplayName = "YarnSpinner Importer - MonoDreams")]
public class YarnSpinnerImporter : ContentImporter<YarnSpinnerFile>
{
    public override YarnSpinnerFile Import(string filename, ContentImporterContext context)
    {
        context.Logger.LogMessage("Importing Yarn file: {0}", filename);
            
        return new YarnSpinnerFile()
        {
            Text = File.ReadAllText(filename),
            FileName = filename
        };
    }
}