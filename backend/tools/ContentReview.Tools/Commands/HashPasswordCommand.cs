using System.CommandLine;
using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Security;

namespace ContentReview.Tools.Commands;

/// <summary>
/// <c>hash-password</c>: prints an Identity V3 hash produced by the same <see cref="IPasswordHashingService"/>
/// the API uses. Standard output carries only the hash; the password is never logged or persisted.
/// </summary>
public static class HashPasswordCommand
{
    public const int ExitOk = 0;
    public const int ExitPolicyViolation = 1;
    public const int ExitInputError = 2;

    public static Command Create(CliEnvironment environment) =>
        Create(environment, new PasswordHashingService());

    public static Command Create(CliEnvironment environment, IPasswordHashingService hasher)
    {
        var stdinOption = new Option<bool>("--stdin")
        {
            Description = "Read the password from the first line of standard input (for automation).",
        };

        var command = new Command(
            "hash-password",
            "Prints a password hash compatible with the API. The hash is the only output on stdout.")
        {
            stdinOption,
        };

        command.SetAction(parseResult => Execute(environment, hasher, parseResult.GetValue(stdinOption)));
        return command;
    }

    private static int Execute(CliEnvironment environment, IPasswordHashingService hasher, bool useStdin)
    {
        var password = useStdin ? ReadFromStdin(environment) : ReadInteractively(environment);
        if (password is null)
        {
            return ExitInputError;
        }

        var violations = PasswordPolicy.Validate(password);
        if (violations.Count > 0)
        {
            environment.Error.WriteLine("The password does not meet the password policy:");
            foreach (var rule in PasswordPolicy.Rules)
            {
                var marker = violations.Contains(rule) ? "x" : "ok";
                environment.Error.WriteLine($"  [{marker}] {rule}");
            }

            return ExitPolicyViolation;
        }

        environment.Out.WriteLine(hasher.Hash(password));
        return ExitOk;
    }

    private static string? ReadFromStdin(CliEnvironment environment)
    {
        var line = environment.In.ReadLine();
        if (line is null)
        {
            environment.Error.WriteLine("No password was provided on standard input.");
        }

        return line;
    }

    private static string? ReadInteractively(CliEnvironment environment)
    {
        if (environment.IsInputRedirected)
        {
            environment.Error.WriteLine(
                "Standard input is redirected, so the password cannot be typed. Use --stdin to pipe it in.");
            return null;
        }

        environment.Error.Write("Password: ");
        var password = environment.ReadMasked();
        if (password is null)
        {
            environment.Error.WriteLine("Cancelled.");
            return null;
        }

        environment.Error.Write("Confirm password: ");
        var confirmation = environment.ReadMasked();
        if (confirmation is null)
        {
            environment.Error.WriteLine("Cancelled.");
            return null;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            environment.Error.WriteLine("The passwords do not match.");
            return null;
        }

        return password;
    }
}
