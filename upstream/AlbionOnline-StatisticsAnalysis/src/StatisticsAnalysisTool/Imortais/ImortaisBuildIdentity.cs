namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.6.1";
    public const string Commit = "fa6e50558d4441159caccd8cfb72db3ff109fd31";
    public static string Display => Version + " · commit " + Commit;
}
