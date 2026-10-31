namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.7.3";
    public const string Commit = "d0caa8b2e9d9bcaad9a8045bf1a5899fee9d55a6";
    public static string Display => Version + " · commit " + Commit;
}
