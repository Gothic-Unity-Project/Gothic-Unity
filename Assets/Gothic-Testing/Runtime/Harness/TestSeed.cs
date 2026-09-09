using System.Globalization;
using UnityEngine;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// Resolves the RNG seed a session runs with. (ADR-0001 D13, §3.9)
    ///
    /// Gothic branches randomly at runtime - a monster's dodge direction, dialogue variation, loot rolls - and all
    /// of it funnels through Hlp_Random, i.e. through the one global UnityEngine.Random stream. Seeding it does
    /// not make assertions pass (tolerant assertions per D12 already survive either branch of a coin flip); it
    /// makes a *specific failing run reproducible*: take the seed from the failed run's manifest.json, set
    /// GOTHIC_TEST_SEED to it and the same branch decisions happen locally.
    ///
    /// This is also what makes the nightly seed sweep possible: same scenario, same assertions, 20 seeds. (D14)
    /// </summary>
    public static class TestSeed
    {
        public const string OverrideArgument = "-gothicTestSeed";
        public const string OverrideEnvironmentVariable = "GOTHIC_TEST_SEED";


        /// <summary>
        /// Precedence: what the scenario asked for, then the command line / environment, then an arbitrary value.
        /// A scenario-supplied seed wins so a seed-sweep runner can drive the same fixture 20 times in-process.
        /// </summary>
        public static int Resolve(int? requestedSeed)
        {
            if (requestedSeed.HasValue)
                return requestedSeed.Value;

            var raw = ArgumentReader.Read(OverrideArgument, OverrideEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(raw) &&
                int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            // Arbitrary, but recorded in the manifest - which is the whole point. An unseeded run is one nobody
            // can reproduce; a randomly seeded and recorded run is reproducible after the fact.
            return Random.Range(int.MinValue, int.MaxValue);
        }
    }
}
