using Station.Abstractions.Modules;

namespace Station.Output;

internal static class TaskReportStatus
{
    public static string Get( ModuleResult result )
    {
        if ( result.Failed )
            return "failed";

        if ( result.Changed )
            return "changed";

        return result.Skipped ? "skipped" : "ok";
    }
}
