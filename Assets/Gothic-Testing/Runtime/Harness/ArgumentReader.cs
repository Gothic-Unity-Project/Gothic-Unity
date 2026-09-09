using System;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// Reads a harness setting from the command line, falling back to an environment variable.
    ///
    /// Same contract as <see cref="Gothic.Core.Domain.Config.DeveloperConfigLoader"/>: the command line wins, so a
    /// CI invocation can't be shadowed by a stale variable on the runner - while the environment variable stays
    /// the only option for a PlayMode test inside an already started Editor, which can no longer influence its
    /// own command line. (ADR-0001 D5)
    /// </summary>
    public static class ArgumentReader
    {
        public static string Read(string argument, string environmentVariable)
        {
            var args = Environment.GetCommandLineArgs();

            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == argument)
                    return args[i + 1];
            }

            return Environment.GetEnvironmentVariable(environmentVariable);
        }
    }
}
