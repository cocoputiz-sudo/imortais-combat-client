namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.6.2";
    public const string Commit = "1062c6110d180e12549f248381618850b20663eb";
    public static string Display => Version + " · commit " + Commit;
}
