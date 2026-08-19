using System;

namespace MonoDreams.ArchSpike.AotConsole;

/// <summary>
/// NativeAOT head of the wave-0 Arch target proof (issue #119, contract item 2, AOT leg).
///
/// All the checks live in <see cref="ArchExercise"/>, shared verbatim with the KNI/BlazorGL WASM
/// head. This head's only job is to run them in a published NATIVE binary and turn the result into
/// an exit code, so "the publish succeeded" and "Arch works in the published image" stay separate
/// claims — the negative control (<c>-p:UseArchAotGenerator=false</c>) publishes just as happily
/// and then dies on the first <c>world.Create</c>.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var (failures, report) = ArchExercise.Run();
        Console.WriteLine(report);
        return failures == 0 ? 0 : 1;
    }
}
