namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.7.2";
    public const string Commit = "c859466c53744a73ba434701f5fb0feadeb81235";
    public static string Display => Version + " · commit " + Commit;
}
