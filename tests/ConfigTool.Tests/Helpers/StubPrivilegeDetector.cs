using ConfigTool.Core;

namespace ConfigTool.Tests;

internal sealed class StubPrivilegeDetector(bool isElevated) : IPrivilegeDetector
{
    public bool IsElevated() => isElevated;
}
