using Station.Core.Infrastructure.Privileges;

namespace Station.Tests.Helpers;

internal sealed class StubPrivilegeDetector(bool isElevated) : IPrivilegeDetector
{
    public bool IsElevated() => isElevated;
}
