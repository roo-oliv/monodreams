namespace MonoDreams.Cli.Tests;

/// <summary>
/// Groups the command tests that capture <c>Console.Out</c> (they swap the process-global
/// <c>Console.Out</c> to assert on printed output) into ONE xunit collection so they never run in parallel
/// with each other — concurrent writers would each observe the other's (empty) captured buffer. The
/// migrate / migrate-colliders command tests share it. <c>DisableParallelization</c> also keeps the
/// collection from racing any other collection that might touch the console.
/// </summary>
[CollectionDefinition("Console (non-parallel: swaps Console.Out)", DisableParallelization = true)]
public sealed class ConsoleCollection { }
