namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.6.3";
    public const string Commit = "71df0e6693566ae0eef60044eabc724f04b3d9cc";
    public static string Display => Version + " · commit " + Commit;
}
