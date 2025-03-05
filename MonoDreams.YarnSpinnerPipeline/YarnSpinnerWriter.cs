using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler;

namespace MonoDreams.YarnSpinnerPipeline;

[ContentTypeWriter]
public class YarnSpinnerWriter : ContentTypeWriter<YarnProgram>
{
    protected override void Write(ContentWriter output, YarnProgram value)
    {
        output.Write(value.CompiledProgram.Length);
        output.Write(value.CompiledProgram);
        output.Write(value.BaseLocalizationId);
        output.Write(value.BaseLocalisationStringTable);
        
        output.Write(value.Localizations.Length);
        foreach (var translation in value.Localizations)
        {
            output.Write(translation.LanguageName);
            output.Write(translation.Text);
        }
    }

    public override string GetRuntimeType(TargetPlatform targetPlatform)
    {
        return typeof(YarnProgram).AssemblyQualifiedName;
    }

    public override string GetRuntimeReader(TargetPlatform targetPlatform)
    {
        return typeof(YarnSpinnerReader).AssemblyQualifiedName;
    }
}