using System;
using System.Collections.Generic;
using System.Text;

namespace MonoDreams.ArchSpike.FacadeEventsProof;

/// <summary>
/// Wave-0 H7/D1 proof runner (issue #119, contract items 3 and 41).
///
/// <para>
/// The migration's single highest-risk decision is D1: the facade — not Arch — raises the engine's
/// reactive events, because Arch cannot deliver a Changed payload carrying the OLD value (M6), has no
/// value-predicate query (M1), and has no publication verb to hang <c>NotifyChanged</c> on (M2). D1
/// is cheap to assert and expensive to be wrong about, so this program asserts nothing on paper and
/// runs the design instead, over the pinned Arch 2.1.0, on the shapes the engine actually has.
/// </para>
///
/// <para>
/// Every scenario builds its own world, exercises one shape and self-verifies. The exit code is the
/// number of failed checks: a green run is <c>exit 0</c>, and any failure is a plan finding rather
/// than a crash (the wave-0 brief explicitly allows the spike to invalidate part of the plan).
/// </para>
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var report = new ProofReport();
        Scenarios.RunAll(report);

        Console.Write(report.Render());
        return report.Failures;
    }
}

/// <summary>Report buffer shared by every scenario: same <c>[ok  ]</c>/<c>[FAIL]</c> shape as the sibling spike heads.</summary>
internal sealed class ProofReport
{
    private readonly StringBuilder _text = new();
    private readonly List<string> _failureLabels = new();

    public int Checks { get; private set; }

    public int Failures => _failureLabels.Count;

    public void Scenario(string title)
    {
        _text.AppendLine();
        _text.AppendLine("== " + title);
    }

    public void Note(string text) => _text.AppendLine("   # " + text);

    /// <summary>An observation with no expected value — measured, not asserted.</summary>
    public void Observe(string label, object value) => _text.AppendLine($"   {label,-58} : {value}");

    public void Check<T>(string label, T actual, T expected)
    {
        Checks++;
        var ok = EqualityComparer<T>.Default.Equals(actual, expected);
        if (!ok) _failureLabels.Add(label);
        _text.AppendLine($"   [{(ok ? "ok  " : "FAIL")}] {label,-58} : {Render(actual)} (expected {Render(expected)})");
    }

    public void CheckTrue(string label, bool actual) => Check(label, actual, true);

    public void Throws<TException>(string label, Action action)
        where TException : Exception
    {
        Checks++;
        try
        {
            action();
            _failureLabels.Add(label);
            _text.AppendLine($"   [FAIL] {label,-58} : returned normally (expected {typeof(TException).Name})");
        }
        catch (TException exception)
        {
            _text.AppendLine($"   [ok  ] {label,-58} : {exception.GetType().Name}: {exception.Message}");
        }
        catch (Exception exception)
        {
            _failureLabels.Add(label);
            _text.AppendLine(
                $"   [FAIL] {label,-58} : {exception.GetType().Name} (expected {typeof(TException).Name})");
        }
    }

    public string Render()
    {
        var summary = new StringBuilder();
        summary.AppendLine("== Facade-fired events over Arch 2.1.0 — wave-0 H7/D1 proof (issue #119, items 3 + 41) ==");
        summary.Append(_text);
        summary.AppendLine();
        summary.AppendLine($"== {Checks} checks, {Failures} failed ==");
        foreach (var label in _failureLabels) summary.AppendLine("   FAILED: " + label);
        return summary.ToString();
    }

    private static string Render<T>(T value) => value is bool flag ? (flag ? "true" : "false") : value?.ToString() ?? "null";
}
