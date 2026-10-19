namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.6.8";
    public const string Commit = "e1263b3c05d47cd9ba0612c07c519842aca3ef98";
    public static string Display => Version + " · commit " + Commit;
}
