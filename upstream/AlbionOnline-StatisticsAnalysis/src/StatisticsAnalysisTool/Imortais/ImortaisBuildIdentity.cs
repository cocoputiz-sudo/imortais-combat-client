namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.6.4";
    public const string Commit = "cc02b1f5794aec1c5dcbbbc92dc5f1f7e315ec29";
    public static string Display => Version + " · commit " + Commit;
}
