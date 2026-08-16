using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: MonoDreams.Tests.ProcessWideStateGuard]

namespace MonoDreams.Tests;

/// <summary>
/// Runs after <b>every</b> test in the assembly and returns the engine's process-wide state to its
/// shipped defaults (see <see cref="ProcessWideState"/>). Declared once at assembly scope — xUnit
/// applies a <see cref="BeforeAfterTestAttribute"/> found on the assembly to every test — so no test
/// class has to remember to opt in, which is exactly the kind of remembering that produced issue #114.
///
/// <para><b>Why after and not before.</b> xUnit's order per test is: class constructor →
/// <c>Before</c> → test method → <c>After</c> → class <c>Dispose</c>. Resetting in <c>Before</c>
/// would wipe a socket a test class deliberately installed in its constructor; resetting in
/// <c>After</c> cannot, and still guarantees the next test starts from the defaults. A class whose
/// <c>Dispose</c> also restores the state it used keeps working — the reset only got there first,
/// and every reset here is idempotent.</para>
///
/// <para><b>It resets, it does not accuse.</b> <c>After</c> runs BEFORE the test class's
/// <c>Dispose</c>, so state still installed at this point is not proof of a leak — the class may be
/// about to clean it up. Set <c>MONODREAMS_TEST_REPORT_LEAKS=1</c> to print what each test still had
/// installed at <c>After</c> time; that list is the starting point when hunting the next
/// order-dependent flake, not a failure. The failure this file does own is
/// <c>ProcessWideStateHygieneTests</c>: it leaks every socket from one test and asserts the next test
/// sees none of it.</para>
/// </summary>
public sealed class ProcessWideStateGuardAttribute : BeforeAfterTestAttribute
{
    private static readonly bool ReportLeaks =
        Environment.GetEnvironmentVariable("MONODREAMS_TEST_REPORT_LEAKS") == "1";

    public override void After(MethodInfo methodUnderTest)
    {
        if (ReportLeaks)
        {
            var dirty = ProcessWideState.Dirty();
            if (dirty.Count > 0)
                Console.WriteLine(
                    $"[static-state] {methodUnderTest.DeclaringType?.FullName}.{methodUnderTest.Name} " +
                    $"left {string.Join(", ", dirty)}");
        }

        if (Environment.GetEnvironmentVariable("MONODREAMS_TEST_NO_RESET") != "1") ProcessWideState.Reset();
    }
}

/// <summary>
/// Runs a class's test methods in alphabetical order. Only for the rare class whose tests are a
/// deliberate SEQUENCE — <see cref="Foundation.ProcessWideStateHygieneTests"/>, where one test leaks
/// on purpose and the next one proves the leak did not survive. Ordinary test classes must not
/// depend on order and so must not use this.
/// </summary>
public sealed class AlphabeticalTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(c => c.TestMethod.Method.Name, StringComparer.Ordinal);
}
