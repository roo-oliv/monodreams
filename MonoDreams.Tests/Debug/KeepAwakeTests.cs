using MonoDreams.Debug;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.Tests.Debug;

/// <summary>
/// Protects the <c>MONODREAMS_KEEP_AWAKE</c> contract — the opt-in macOS power-management assertion
/// that keeps an unattended run from being suspended by App Nap or display sleep (the failure that
/// hung a three-hour run inside <c>Cocoa_GL_SwapWindow</c>).
///
/// <para>Two properties matter and both are asserted here: the flag is <b>off unless asked</b> (a
/// game must never assert anything about a user's power settings on its own), and asking is
/// <b>never fatal</b> — on macOS it holds a real NSProcessInfo activity, and everywhere else it is a
/// logged no-op that still returns cleanly.</para>
///
/// <para>Like the capture tests, these mutate the global <see cref="PlatformServices.Current"/>
/// holder and therefore share the non-parallel collection.</para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class KeepAwakeTests : IDisposable
{
    public KeepAwakeTests() => ResetLoggerStatics();

    public void Dispose() => ResetLoggerStatics();

    private static void ResetLoggerStatics()
    {
        WithEnvironment(new FakeEnvironment(), _ =>
        {
            Logger.Shutdown();
            Logger.Initialize("scratch");
            Logger.Shutdown();
        });
    }

    /// In-memory <see cref="IPlatformServices"/>: env vars from a dictionary, log lines recorded.
    private sealed class FakeEnvironment : IPlatformServices
    {
        public string BaseDirectory => "/fake/base/";
        public Dictionary<string, string> EnvVars { get; } = new();
        public List<string> ConsoleLines { get; } = new();

        public string GetEnvironmentVariable(string name) =>
            EnvVars.TryGetValue(name, out var v) ? v : null!;

        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => false;
        public string ReadAllText(string path) => throw new NotSupportedException();
        public void WriteAllText(string path, string contents) => throw new NotSupportedException();
        public void WriteAllBytes(string path, byte[] bytes) => throw new NotSupportedException();
        public string ExportScene(string suggestedFileName, string contents) => throw new NotSupportedException();
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => TextWriter.Null;
        public void WriteLineToConsole(string line) => ConsoleLines.Add(line);
        public void RunBackground(Action work) => work();
    }

    private static void WithEnvironment(FakeEnvironment fake, Action<FakeEnvironment> body)
    {
        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = fake;
            body(fake);
        }
        finally
        {
            PlatformServices.Current = previous;
        }
    }

    private static FakeEnvironment Env(params (string name, string value)[] vars)
    {
        var fake = new FakeEnvironment();
        foreach (var (name, value) in vars) fake.EnvVars[name] = value;
        return fake;
    }

    [Fact]
    public void FromEnvironment_ReturnsNull_AndSaysNothing_WhenUnset()
    {
        WithEnvironment(Env(), fake =>
        {
            // The default must be invisible: a shipped game holds no assertion and logs no line
            // about the machine's power management.
            Assert.Null(KeepAwake.FromEnvironment());
            Assert.Empty(fake.ConsoleLines);
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    [InlineData("OFF")]
    [InlineData(" off ")]
    [InlineData("")]
    [InlineData("   ")]
    public void FromEnvironment_ReturnsNull_WhenExplicitlyOff(string requested)
    {
        WithEnvironment(Env((KeepAwake.EnvironmentVariable, requested)), fake =>
        {
            Assert.Null(KeepAwake.FromEnvironment());
            Assert.Empty(fake.ConsoleLines);
        });
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("2")]
    [InlineData("caffeinate")]
    public void FromEnvironment_ReturnsNull_AndLogsAnError_ForAnUnknownValue(string requested)
    {
        WithEnvironment(Env((KeepAwake.EnvironmentVariable, requested)), fake =>
        {
            // An unreadable value must not be read as "on": the run would then be silently
            // unprotected while the variable suggests otherwise, so it says so.
            Assert.Null(KeepAwake.FromEnvironment());
            Assert.Contains(fake.ConsoleLines,
                l => l.Contains("[ERROR]") && l.Contains("is not a keep-awake setting"));
        });
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("on")]
    [InlineData(" TRUE ")]
    public void FromEnvironment_WhenAsked_HoldsOnMacOs_AndIsAGracefulNoOpElsewhere(string requested)
    {
        WithEnvironment(Env((KeepAwake.EnvironmentVariable, requested)), fake =>
        {
            var token = KeepAwake.FromEnvironment();
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    // The real thing: an NSProcessInfo activity, resolved through the Objective-C
                    // runtime, held by the returned token.
                    Assert.NotNull(token);
                    Assert.Contains(fake.ConsoleLines, l => l.Contains("NSProcessInfo activity held"));
                }
                else
                {
                    // Everywhere else the request is honoured by saying, in the run's own log, that
                    // it is not — never by throwing and never in silence.
                    Assert.Null(token);
                    Assert.Contains(fake.ConsoleLines, l => l.Contains("macOS-only"));
                }
            }
            finally
            {
                token?.Dispose();
            }
        });
    }

    [Fact]
    public void Dispose_ReleasesTheActivity_AndIsIdempotent()
    {
        WithEnvironment(Env((KeepAwake.EnvironmentVariable, "1")), fake =>
        {
            var token = KeepAwake.FromEnvironment();
            if (token == null)
            {
                Assert.False(OperatingSystem.IsMacOS(), "macOS must produce a live assertion token.");
                return;
            }

            token.Dispose();
            Assert.Contains(fake.ConsoleLines, l => l.Contains("NSProcessInfo activity released"));

            // A host that disposes twice (explicit Dispose plus a using) must not double-release the
            // Objective-C object — that is a crash, not a warning.
            var releasedLines = fake.ConsoleLines.Count(l => l.Contains("activity released"));
            token.Dispose();
            Assert.Equal(releasedLines, fake.ConsoleLines.Count(l => l.Contains("activity released")));
        });
    }
}
