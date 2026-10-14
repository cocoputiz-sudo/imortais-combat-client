namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.6.6";
    public const string Commit = "6abf54315d536940c5b2e0f8ee8b7cedfcfbdd69";
    public static string Display => Version + " · commit " + Commit;
}
