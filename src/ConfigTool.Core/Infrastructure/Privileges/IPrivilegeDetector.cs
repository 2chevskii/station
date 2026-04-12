namespace ConfigTool.Core;

internal interface IPrivilegeDetector
{
    bool IsElevated();
}
