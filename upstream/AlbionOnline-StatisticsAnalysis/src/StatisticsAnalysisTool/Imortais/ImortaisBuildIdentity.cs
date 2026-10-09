namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.6.0";
    public const string Commit = "LOCAL-UNSTAMPED";
    public static string Display => Version + " · commit " + Commit;
}
