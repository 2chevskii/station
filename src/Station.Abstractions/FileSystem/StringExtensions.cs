using TruePath;

namespace Station.Abstractions.FileSystem;

public static class StringExtensions
{
    public static LocalPath AsLocalPath( this string self ) => LocalPath.Create( self );

    public static AbsolutePath AsAbsolutePath( this string self ) => AbsolutePath.Create( self );
}