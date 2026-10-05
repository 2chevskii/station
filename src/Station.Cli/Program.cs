using Microsoft.Extensions.DependencyInjection;

using Station.Cli;
using Station.Cli.Commands;

await using var services = new ServiceCollection().AddStation().BuildServiceProvider(
    new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
);

return await services.GetRequiredService<StationCommandLine>().RunAsync( args );
