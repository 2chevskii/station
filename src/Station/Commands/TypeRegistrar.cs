using Microsoft.Extensions.DependencyInjection;

using Spectre.Console.Cli;

namespace Station.Commands;

internal sealed class TypeRegistrar( IServiceCollection registrations ) : ITypeRegistrar, IAsyncDisposable
{
    private ServiceProvider? services;

    public void Register( Type service, Type implementation )
    {
        registrations.AddSingleton( service, implementation );
    }

    public void RegisterInstance( Type service, object implementation )
    {
        registrations.AddSingleton( service, implementation );
    }

    public void RegisterLazy( Type service, Func<object> factory )
    {
        registrations.AddSingleton( service, _ => factory() );
    }

    public ITypeResolver Build()
    {
        services ??= registrations.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        return new TypeResolver( services );
    }

    public async ValueTask DisposeAsync()
    {
        if ( services is not null )
            await services.DisposeAsync();
    }

    // The registrar owns disposal so asynchronous services can be released after CommandApp returns.
    private sealed class TypeResolver( ServiceProvider services ) : ITypeResolver
    {
        public object? Resolve( Type? type ) => type is null ? null : services.GetService( type );
    }
}
