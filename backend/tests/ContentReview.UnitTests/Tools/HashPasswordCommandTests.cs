using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Security;
using ContentReview.Tools;
using ContentReview.Tools.Commands;

namespace ContentReview.UnitTests.Tools;

public class HashPasswordCommandTests
{
    private const string Password = "Str0ng!Passw0rd";

    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    private int Run(string[] args, string stdin = "", bool inputRedirected = true, params string?[] maskedInput)
    {
        var masked = new Queue<string?>(maskedInput);
        var environment = new CliEnvironment(
            new StringReader(stdin),
            _stdout,
            _stderr,
            inputRedirected,
            () => masked.Dequeue());

        return CliApplication.Run(args, environment);
    }

    [Fact]
    public void Stdin_with_a_valid_password_prints_only_a_hash_that_the_api_service_verifies()
    {
        var exitCode = Run(["hash-password", "--stdin"], $"{Password}\n");

        Assert.Equal(HashPasswordCommand.ExitOk, exitCode);
        var hash = Assert.Single(_stdout.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(new PasswordHashingService().Verify(hash, Password));
        Assert.Empty(_stderr.ToString());
    }

    [Fact]
    public void Windows_line_endings_are_not_part_of_the_password()
    {
        var exitCode = Run(["hash-password", "--stdin"], $"{Password}\r\n");

        Assert.Equal(HashPasswordCommand.ExitOk, exitCode);
        Assert.True(new PasswordHashingService().Verify(_stdout.ToString().Trim(), Password));
    }

    [Fact]
    public void A_weak_password_exits_non_zero_lists_the_rules_and_prints_nothing_on_stdout()
    {
        var exitCode = Run(["hash-password", "--stdin"], "short\n");

        Assert.NotEqual(0, exitCode);
        Assert.Equal(HashPasswordCommand.ExitPolicyViolation, exitCode);
        Assert.Empty(_stdout.ToString());
        foreach (var rule in PasswordPolicy.Rules)
        {
            Assert.Contains(rule, _stderr.ToString());
        }
    }

    [Fact]
    public void The_password_is_never_written_to_either_output()
    {
        Run(["hash-password", "--stdin"], $"{Password}\n");
        Run(["hash-password", "--stdin"], "weakpassword\n");

        Assert.DoesNotContain(Password, _stdout.ToString() + _stderr.ToString());
        Assert.DoesNotContain("weakpassword", _stdout.ToString() + _stderr.ToString());
    }

    [Fact]
    public void Empty_stdin_exits_with_an_input_error()
    {
        var exitCode = Run(["hash-password", "--stdin"]);

        Assert.Equal(HashPasswordCommand.ExitInputError, exitCode);
        Assert.Empty(_stdout.ToString());
    }

    [Fact]
    public void Redirected_input_without_the_stdin_flag_is_refused()
    {
        var exitCode = Run(["hash-password"], $"{Password}\n");

        Assert.Equal(HashPasswordCommand.ExitInputError, exitCode);
        Assert.Empty(_stdout.ToString());
        Assert.Contains("--stdin", _stderr.ToString());
    }

    [Fact]
    public void Interactive_mode_reads_masked_input_and_prompts_on_stderr_only()
    {
        var exitCode = Run(["hash-password"], inputRedirected: false, maskedInput: [Password, Password]);

        Assert.Equal(HashPasswordCommand.ExitOk, exitCode);
        Assert.True(new PasswordHashingService().Verify(_stdout.ToString().Trim(), Password));
        Assert.Contains("Password:", _stderr.ToString());
        Assert.DoesNotContain("Password:", _stdout.ToString());
    }

    [Fact]
    public void Interactive_mode_rejects_a_mismatched_confirmation()
    {
        var exitCode = Run(["hash-password"], inputRedirected: false, maskedInput: [Password, Password + "x"]);

        Assert.Equal(HashPasswordCommand.ExitInputError, exitCode);
        Assert.Empty(_stdout.ToString());
    }

    [Fact]
    public void Interactive_mode_can_be_cancelled()
    {
        var exitCode = Run(["hash-password"], inputRedirected: false, maskedInput: [null]);

        Assert.Equal(HashPasswordCommand.ExitInputError, exitCode);
        Assert.Empty(_stdout.ToString());
    }

    [Fact]
    public void Unknown_commands_fail()
    {
        Assert.NotEqual(0, Run(["does-not-exist"]));
    }
}
