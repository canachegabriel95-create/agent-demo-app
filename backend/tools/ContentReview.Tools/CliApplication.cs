using System.CommandLine;
using ContentReview.Tools.Commands;

namespace ContentReview.Tools;

/// <summary>Builds the root command. Further commands (seed, reset) register themselves here.</summary>
public static class CliApplication
{
    public static RootCommand Build(CliEnvironment environment) =>
        new("Content Review Platform command line tools.")
        {
            HashPasswordCommand.Create(environment),
        };

    public static int Run(string[] args, CliEnvironment environment)
    {
        var configuration = new InvocationConfiguration
        {
            Output = environment.Out,
            Error = environment.Error,
        };

        return Build(environment).Parse(args).Invoke(configuration);
    }
}
