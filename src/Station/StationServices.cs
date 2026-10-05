using Microsoft.Extensions.DependencyInjection;

using Station.Abstractions.Artifacts;
using Station.Abstractions.Processes;
using Station.Configuration.Abstractions.Documents;
using Station.Configuration.JavaScript;
using Station.Configuration.Yaml;
using Station.Core;
using Station.Plugins;
using Station.Utilities.Privileges;
using Station.Utilities.Processes;

namespace Station;

public static class StationServices
{
    public static IServiceCollection AddStation( this IServiceCollection services )
    {
        services.AddSingleton<ITemplateEngine, ScribanTemplateRenderer>();
        services.AddSingleton<YamlConfigurationReader>();
        services.AddSingleton<JavaScriptConfigRuntime>();
        services.AddSingleton<IRoleDocumentLoader, YamlRoleDocumentLoader>();
        services.AddSingleton<IRoleDocumentLoader, JavaScriptRoleDocumentLoader>();
        services.AddSingleton<IVariableDocumentLoader, YamlVariableDocumentLoader>();
        services.AddSingleton<IVariableDocumentLoader, JavaScriptVariableDocumentLoader>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPrivilegeDetector, WindowsPrivilegeDetector>();
        services.AddSingleton( TimeProvider.System );
        services.AddSingleton( _ => new HttpClient() );
        services.AddStationPlugins();
        services.AddStationCore();

        return services;
    }
}
