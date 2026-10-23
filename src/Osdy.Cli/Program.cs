using Osdy.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var app = new CliApp(Console.In, Console.Out, Console.Error, TimeProvider.System);
return await app.RunAsync(args, cancellation.Token);
