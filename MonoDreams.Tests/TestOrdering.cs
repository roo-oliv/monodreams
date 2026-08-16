using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCollectionOrderer("MonoDreams.Tests.DeterministicCollectionOrderer", "MonoDreams.Tests")]

namespace MonoDreams.Tests;

/// <summary>
/// Makes the assembly's test-class execution order <b>deterministic and steerable</b>.
///
/// <para>xUnit puts each test class in its own collection and, with no orderer installed, runs those
/// collections in the order a hash-keyed grouping hands them over. .NET randomises string hashing per
/// process, so that order is different on every run — which is why an order-dependent failure
/// (issue #114) appears in one run and hides for the next three, and why "it passes in isolation"
/// tells you nothing. Sorting by name makes the order identical on every run and every machine: a
/// failure that depends on order is now either always there or never there.</para>
///
/// <para><b>Deliberate shuffling is still available</b>, because a frozen order can also hide a leak
/// that a different order would expose. Set <c>MONODREAMS_TEST_SEED</c> to any integer to shuffle the
/// classes with that seed (the seed is echoed to the console, so a red run is replayable by re-running
/// with the same value), or <c>MONODREAMS_TEST_LAST=&lt;substring&gt;</c> to force every matching class
/// to run after all the others — the two knobs this issue's investigation was driven with.</para>
///
/// <para>Ordering is a <b>diagnosis</b> tool, not the fix: it does not stop one test from leaving
/// process-wide state behind, it only stops the consequence from being random.
/// <see cref="ProcessWideState"/> is the fix — see the foundation premise
/// "A process-wide socket is restored by whoever installs it — tests included".</para>
/// </summary>
public sealed class DeterministicCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        var ordered = testCollections.OrderBy(c => c.DisplayName, StringComparer.Ordinal).ToList();

        var seedText = Environment.GetEnvironmentVariable("MONODREAMS_TEST_SEED");
        if (int.TryParse(seedText, out var seed))
        {
            Console.WriteLine($"[test-order] shuffling {ordered.Count} test classes with MONODREAMS_TEST_SEED={seed}");
            var random = new Random(seed);
            for (var i = ordered.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
            }
        }

        var last = Environment.GetEnvironmentVariable("MONODREAMS_TEST_LAST");
        if (string.IsNullOrWhiteSpace(last)) return ordered;

        Console.WriteLine($"[test-order] running classes matching MONODREAMS_TEST_LAST={last} last");
        return ordered.Where(c => !c.DisplayName.Contains(last, StringComparison.Ordinal))
            .Concat(ordered.Where(c => c.DisplayName.Contains(last, StringComparison.Ordinal)))
            .ToList();
    }
}
