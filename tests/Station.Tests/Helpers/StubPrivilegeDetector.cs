using Station.Core;

namespace Station.Tests;

internal sealed class StubPrivilegeDetector(bool isElevated) : IPrivilegeDetector
{
    public bool IsElevated() => isElevated;
}
