namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.7.1";
    public const string Commit = "943617a2ef5e4b78140bffa829f681dbcf5b6218";
    public static string Display => Version + " · commit " + Commit;
}
