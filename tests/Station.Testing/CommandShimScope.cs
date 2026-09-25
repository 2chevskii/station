using System.Text;

using Station.Abstractions.FileSystem;
using TruePath;
using TruePath.SystemIo;

namespace Station.Testing;

public sealed class CommandShimScope : IDisposable
{
    private readonly string originalPath;
    private readonly string originalPathExt;
    private readonly TemporaryDirectory directory = new();

    public CommandShimScope()
    {
        originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        originalPathExt =
            Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM;.PS1";
        Environment.SetEnvironmentVariable(
            "PATH",
            directory.Path + System.IO.Path.PathSeparator + originalPath
        );
        Environment.SetEnvironmentVariable("PATHEXT", ".CMD;.BAT;.EXE;.COM;.PS1");
    }

    public string Path => directory.Path;

    public string AddCommand(string name, string body)
    {
        var path = directory.GetPath($"{name}.cmd");
        path.AsAbsolutePath().WriteAllText("@echo off\r\n" + body.Trim() + "\r\n", Encoding.ASCII);
        return path;
    }

    public string AddPowerShellCommand(string name, string body)
    {
        var scriptPath = directory.GetPath($"{name}.ps1");
        scriptPath.AsAbsolutePath().WriteAllText(body.Replace("\r\n", "\n"), Encoding.UTF8);
        return AddCommand(
            name,
            $"powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0{name}.ps1\" %*"
        );
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", originalPath);
        Environment.SetEnvironmentVariable("PATHEXT", originalPathExt);
        directory.Dispose();
    }
}
