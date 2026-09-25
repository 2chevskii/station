namespace Station.Core.Execution.Privileges;

internal interface IPrivilegeDetector
{
    bool IsElevated();
}
