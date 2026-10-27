using System.Text;

namespace ContentReview.Tools;

/// <summary>Reads a line from the terminal without echoing the typed characters.</summary>
public static class MaskedConsoleReader
{
    public static string? ReadLine()
    {
        var buffer = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.Error.WriteLine();
                    return buffer.ToString();
                case ConsoleKey.Escape:
                    Console.Error.WriteLine();
                    return null;
                case ConsoleKey.Backspace:
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                    }

                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Append(key.KeyChar);
                    }

                    break;
            }
        }
    }
}
