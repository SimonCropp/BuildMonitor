/// <summary>
/// Runs a program to completion with its output captured. Arguments go as a list, never a
/// shell line, so a value with a space or a quote in it arrives intact.
/// </summary>
static class ProcessRunner
{
    public static (int Code, string Output) Run(string file, IReadOnlyList<string> arguments, string? input = null, TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info) ??
                                throw new InvalidOperationException($"Could not start {file}");
            if (input is not null)
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int) (timeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds))
            {
                process.Kill(true);
                return (-1, $"{file} timed out");
            }

            var text = output.Result;
            if (text.Length == 0)
            {
                text = error.Result;
            }

            return (process.ExitCode, text);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, exception.Message);
        }
    }

    public static string? OnPath(string file)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
