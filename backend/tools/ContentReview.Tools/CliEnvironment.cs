namespace ContentReview.Tools;

/// <summary>
/// Streams and input handling used by the commands, so they can be exercised in tests without a console.
/// Only data meant for capture is written to <see cref="Out"/>; prompts and messages go to <see cref="Error"/>.
/// </summary>
/// <param name="In">Standard input (used by <c>--stdin</c>).</param>
/// <param name="Out">Standard output.</param>
/// <param name="Error">Standard error.</param>
/// <param name="IsInputRedirected">True when input is piped, i.e. no interactive terminal is available.</param>
/// <param name="ReadMasked">Reads a line from the terminal without echoing it; null when the input is cancelled.</param>
public sealed record CliEnvironment(
    TextReader In,
    TextWriter Out,
    TextWriter Error,
    bool IsInputRedirected,
    Func<string?> ReadMasked)
{
    public static CliEnvironment FromConsole() => new(
        Console.In,
        Console.Out,
        Console.Error,
        Console.IsInputRedirected,
        MaskedConsoleReader.ReadLine);
}
