using System;
using MonoDreams.Demos.Screens;

namespace MonoDreams.Demos;

/// Parsed command-line options for the demo host's headless self-verification path.
///
/// Headless Demos is the level-2 "render output" mode from issue #28: a hidden,
/// off-screen window still hosts a real <c>GraphicsDevice</c>, every frame is rendered
/// (Draw is NOT short-circuited), frames are dumped to <c>MONODREAMS_DEBUG_DIR</c> as
/// PNGs, managed-heap samples go to the log, and the process self-terminates after a
/// fixed number of frames. This lets an agent observe the running result without a human.
///
/// Usage:
///   dotnet run --project MonoDreams.Demos -- --headless --screen camera --frames 600 --exit
internal sealed class HeadlessOptions
{
    public bool Enabled { get; private init; }

    /// Registered screen id to load directly (skipping the launcher).
    public string Screen { get; private init; } = DemoScreens.Camera;

    /// Number of frames to render before auto-exiting.
    public int Frames { get; private init; } = 600;

    /// Capture a PNG every N frames (the final frame is always captured). 0 disables
    /// periodic capture but still captures the final frame.
    public int CaptureEvery { get; private init; } = 60;

    /// Emit a managed-heap sample to the log every N frames.
    public int SampleEvery { get; private init; } = 30;

    public static HeadlessOptions Parse(string[]? args)
    {
        args ??= Array.Empty<string>();

        var enabled = false;
        var screen = "camera";
        var frames = 600;
        var captureEvery = 60;
        var sampleEvery = 30;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless":
                    enabled = true;
                    break;
                // --exit is accepted for parity with the documented invocation; auto-exit
                // is driven by --frames regardless, so it needs no separate state.
                case "--exit":
                    break;
                case "--screen":
                    if (i + 1 < args.Length) screen = args[++i];
                    break;
                case "--frames":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var f)) { frames = f; i++; }
                    break;
                case "--capture-every":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var c)) { captureEvery = c; i++; }
                    break;
                case "--sample-every":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var s)) { sampleEvery = s; i++; }
                    break;
            }
        }

        return new HeadlessOptions
        {
            Enabled = enabled,
            Screen = ResolveScreen(screen),
            Frames = Math.Max(1, frames),
            CaptureEvery = Math.Max(0, captureEvery),
            SampleEvery = Math.Max(0, sampleEvery),
        };
    }

    /// Maps a short CLI screen name (or a full registered id) to the registered id.
    private static string ResolveScreen(string name) => name switch
    {
        "camera" => DemoScreens.Camera,
        "physics" => DemoScreens.Physics,
        "dialogue" => DemoScreens.Dialogue,
        "ui" => DemoScreens.Ui,
        "audio" => DemoScreens.Audio,
        "launcher" => DemoScreens.Launcher,
        _ => name, // allow passing a full id like "demos.camera" verbatim
    };
}
