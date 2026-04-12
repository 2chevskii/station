namespace Station.Core.Infrastructure.Privileges;

internal interface IPrivilegeDetector
{
    bool IsElevated();
}
