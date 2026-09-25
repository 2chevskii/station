using TruePath;

namespace Station.Abstractions.FileSystem;

public static class StringExtensions
{
    public static LocalPath AsLocalPath(this string self)
    {
        return LocalPath.Create(self);
    }

    public static AbsolutePath AsAbsolutePath(this string self)
    {
        return AbsolutePath.Create(self);
    }
}
