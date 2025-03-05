using Microsoft.Xna.Framework.Content;

namespace MonoDreams.YarnSpinner;

public class YarnSpinnerReader : ContentTypeReader<YarnProgram>
{
    protected override YarnProgram Read(ContentReader input, YarnProgram existingInstance)
    {
        YarnProgram program = new YarnProgram();
            
        var compiledProgramLength = input.ReadInt32();
        program.CompiledProgram = input.ReadBytes(compiledProgramLength);
        program.BaseLocalizationId = input.ReadString();
        program.BaseLocalisationStringTable = input.ReadString();

        var translationLength = input.ReadInt32();
        program.Localizations = new YarnTranslation[translationLength];
        for (var i = 0; i < translationLength; i++)
        {
            program.Localizations[i].LanguageName = input.ReadString();
            program.Localizations[i].Text = input.ReadString();
        }

        return program;
    }
}
