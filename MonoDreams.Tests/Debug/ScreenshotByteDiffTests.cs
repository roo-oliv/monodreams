using Xunit.Sdk;

namespace MonoDreams.Tests.Debug;

/// <summary>
/// The byte-level screenshot comparison <see cref="GameTestRunner.AssertScreenshotsByteIdentical(string, string, string?)"/>
/// provides — the equivalence proof the ECS migration's identity gate rests on (issue #119, contract
/// C7/item 24). <c>AssertScreenshotNonBlank</c> only proves a frame was drawn; it cannot see a changed
/// pixel, so it can never say "this run rendered what the baseline rendered".
///
/// <para>These cases drive the helper over hand-built directories rather than spawning a game, because
/// what needs pinning is the comparison's own contract: it pairs frames by <b>capture ordinal</b> (the
/// only cross-run-stable part of a capture's name — <c>ScreenshotCaptureSystem.MakeFilename</c> ends
/// every name with a <c>DateTime.Now</c> stamp), it fails on a single differing byte, and it fails
/// rather than passes when there is nothing to compare.</para>
/// </summary>
public sealed class ScreenshotByteDiffTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "monodreams_bytediff_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Dir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// Writes one capture with the real name shape: <c>screenshot_%06d_gt&lt;t&gt;_&lt;wallclock&gt;.png</c>.
    private static void WriteCapture(string dir, int ordinal, string gt, string timestamp, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(dir, $"screenshot_{ordinal:D6}_gt{gt}_{timestamp}.png"), bytes);

    private static byte[] Png(params byte[] tail) => new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }.Concat(tail).ToArray();

    /// <summary>
    /// The pairing contract: two runs of the same demo NEVER agree on a filename — the capture name
    /// ends in <c>DateTime.Now</c> — so pairing is on the ordinal alone. Same pixels under different
    /// names must pass, or the helper could never be used across runs at all.
    /// </summary>
    [Fact]
    public void IdenticalBytesUnderDifferentFilenames_Pass()
    {
        var a = Dir("a");
        var b = Dir("b");
        WriteCapture(a, 0, "0.03", "20260819_103534_829", Png(1, 2, 3));
        WriteCapture(b, 0, "0.03", "20260819_110201_004", Png(1, 2, 3));
        WriteCapture(a, 1, "1.03", "20260819_103536_407", Png(9, 9));
        WriteCapture(b, 1, "1.03", "20260819_110202_991", Png(9, 9));

        GameTestRunner.AssertScreenshotsByteIdentical(a, b, "camera");
    }

    /// <summary>One differing byte is a failure — and the report says which capture, how big each
    /// file was, and at what offset they parted, so a red gate is diagnosable without re-running.</summary>
    [Fact]
    public void ASingleDifferingByte_Fails_AndReportsTheOrdinalAndOffset()
    {
        var a = Dir("a");
        var b = Dir("b");
        WriteCapture(a, 0, "0.03", "20260819_103534_829", Png(1, 2, 3));
        WriteCapture(b, 0, "0.03", "20260819_110201_004", Png(1, 2, 3));
        WriteCapture(a, 1, "1.03", "20260819_103536_407", Png(7, 7, 7));
        WriteCapture(b, 1, "1.03", "20260819_110202_991", Png(7, 8, 7));

        var failure = Assert.Throws<FailException>(
            () => GameTestRunner.AssertScreenshotsByteIdentical(a, b, "camera"));

        Assert.Contains("camera: Capture #1 differs", failure.Message);
        Assert.Contains("first differing byte at offset 5", failure.Message);
    }

    /// <summary>A truncated capture (a run that died mid-write) is a difference too — the shorter file
    /// being a prefix of the longer one must never read as equal.</summary>
    [Fact]
    public void APrefixOfTheOtherCapture_Fails()
    {
        var a = Dir("a");
        var b = Dir("b");
        WriteCapture(a, 0, "0.03", "20260819_103534_829", Png(1, 2, 3));
        WriteCapture(b, 0, "0.03", "20260819_110201_004", Png(1, 2));

        var failure = Assert.Throws<FailException>(() => GameTestRunner.AssertScreenshotsByteIdentical(a, b));
        Assert.Contains("one file is a prefix of the other", failure.Message);
    }

    /// <summary>Frame sets must match. A run that captured fewer frames than its baseline has already
    /// diverged (it rendered a different number of frames), so this is a failure, not a subset compare.</summary>
    [Fact]
    public void DifferentFrameSets_Fail()
    {
        var a = Dir("a");
        var b = Dir("b");
        WriteCapture(a, 0, "0.03", "20260819_103534_829", Png(1));
        WriteCapture(a, 1, "1.03", "20260819_103536_407", Png(2));
        WriteCapture(b, 0, "0.03", "20260819_110201_004", Png(1));

        var failure = Assert.Throws<TrueException>(() => GameTestRunner.AssertScreenshotsByteIdentical(a, b));
        Assert.Contains("captured different frame sets", failure.Message);
    }

    /// <summary>An empty directory means the run captured nothing — the one outcome a comparison
    /// helper must never report as "identical", or a gate over a broken capture path passes silently.</summary>
    [Fact]
    public void NoCapturesAtAll_Fails()
    {
        var failure = Assert.Throws<TrueException>(
            () => GameTestRunner.AssertScreenshotsByteIdentical(Dir("a"), Dir("b")));
        Assert.Contains("nothing to compare", failure.Message);
    }

    /// <summary>The ordinal restarts per capture-system instance, so a debug dir fed by two of them
    /// (the periodic headless capture plus an <c>MONODREAMS_SCREENSHOT</c> env-driven one) would pair
    /// unrelated frames. That is a broken comparison, and it throws rather than comparing.</summary>
    [Fact]
    public void TwoCapturesSharingAnOrdinal_Throw()
    {
        var a = Dir("a");
        File.WriteAllBytes(Path.Combine(a, "screenshot_000000_gt0.03_20260819_103534_829.png"), Png(1));
        File.WriteAllBytes(Path.Combine(a, "screenshot_000000_gt0.03_20260819_103599_001.png"), Png(2));

        var error = Assert.Throws<InvalidOperationException>(() => GameTestRunner.ReadScreenshotsByOrdinal(a));
        Assert.Contains("share ordinal 0", error.Message);
    }

    /// <summary>Non-capture files in the debug dir (the log, a raw frame dump, an editor artefact) are
    /// not captures and must not enter the comparison.</summary>
    [Fact]
    public void NonCaptureFiles_AreIgnored()
    {
        var a = Dir("a");
        var b = Dir("b");
        WriteCapture(a, 0, "0.03", "20260819_103534_829", Png(1, 2, 3));
        WriteCapture(b, 0, "0.03", "20260819_110201_004", Png(1, 2, 3));
        File.WriteAllText(Path.Combine(a, "monodreams_20260819.log"), "a log line");
        File.WriteAllBytes(Path.Combine(b, "frame_000000_1280x720.raw"), new byte[] { 1, 2, 3, 4 });

        Assert.Single(GameTestRunner.ReadScreenshotsByOrdinal(a));
        GameTestRunner.AssertScreenshotsByteIdentical(a, b);
    }
}
