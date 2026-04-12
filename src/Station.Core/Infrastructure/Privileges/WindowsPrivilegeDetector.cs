using System.Security.Principal;

namespace Station.Core;

internal sealed class WindowsPrivilegeDetector : IPrivilegeDetector
{
    public bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
