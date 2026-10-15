namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.6.7";
    public const string Commit = "a9639e17c5eea698317153f2aad19d323cb02a03";
    public static string Display => Version + " · commit " + Commit;
}
