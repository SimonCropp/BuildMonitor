static class Program
{
    static async Task<int> Main(string[] args)
    {
        var command = CommandLine.Parse(args);
        var port = Port.Resolve(ReadSettings());
        using var cancel = new CancelSource();
        Console.CancelKeyPress += (_, arguments) =>
        {
            arguments.Cancel = true;
            cancel.Cancel();
        };

        switch (command.Kind)
        {
            case CommandKindLauncher.Start:
                return await HeadLauncher.StartOrShow(port, show: true, cancel.Token);
            case CommandKindLauncher.Mcp:
                return await McpHost.Run(port, cancel.Token);
            case CommandKindLauncher.Version:
                ConsoleAttach.TryAttachParent();
                Console.WriteLine(VersionReader.VersionString);
                return 0;
            case CommandKindLauncher.Help:
                ConsoleAttach.TryAttachParent();
                Console.WriteLine(CommandLine.Usage);
                return 0;
            case CommandKindLauncher.Unknown:
                ConsoleAttach.TryAttachParent();
                await Console.Error.WriteLineAsync($"Unknown command: {command.Argument}");
                await Console.Error.WriteLineAsync(CommandLine.Usage);
                return 1;
            default:
                ConsoleAttach.TryAttachParent();
                return await Send(command, port, cancel.Token);
        }
    }

    static async Task<int> Send(Command command, int port, Cancel cancel)
    {
        var client = new ProtocolClient(port);
        try
        {
            switch (command.Kind)
            {
                case CommandKindLauncher.Status:
                {
                    var tools = new MonitorTools(client);
                    var summary = await tools.Summary(cancel);
                    var builds = await tools.ListBuilds(null, cancel);
                    Console.Write(StatusPrinter.Render(summary, builds));
                    return 0;
                }
                case CommandKindLauncher.Show:
                    return Print(await client.Send(new(Verb.Show), cancel));
                case CommandKindLauncher.Hide:
                    return Print(await client.Send(new(Verb.Hide), cancel));
                case CommandKindLauncher.Quit:
                    return Print(await client.Send(new(Verb.Quit), cancel));
                case CommandKindLauncher.Refresh:
                    return Print(await client.Send(new(Verb.Refresh, command.Argument), cancel));
                default:
                    return 1;
            }
        }
        catch (TrayUnreachableException)
        {
            await Console.Error.WriteLineAsync($"BuildMonitor is not running (nothing answered on port {port}). Run `buildmonitor` to start it.");
            return 3;
        }
    }

    static int Print(Response response)
    {
        if (response.Body.Length > 0)
        {
            Console.WriteLine(response.Body);
        }

        if (response.Ok)
        {
            return 0;
        }

        return 1;
    }

    static Settings? ReadSettings()
    {
        try
        {
            return SettingsHelper.Read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
