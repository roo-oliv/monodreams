using Yarn;

namespace MonoDreams.YarnSpinner;

public class YarnProgram
{
    public byte[] CompiledProgram;

    public string BaseLocalisationStringTable;

    public string BaseLocalizationId;
        
    public YarnTranslation[] Localizations = new YarnTranslation[0];

    public Program GetProgram()
    {
        return Program.Parser.ParseFrom(CompiledProgram);
    }
}