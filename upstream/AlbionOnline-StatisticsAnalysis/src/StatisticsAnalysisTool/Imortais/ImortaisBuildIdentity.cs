namespace StatisticsAnalysisTool.Imortais;

// The private GitHub workflow stamps this file with the exact commit built.
// Local developer builds retain an explicit unknown marker.
public static class ImortaisBuildIdentity
{
    public const string Version = "v0.7.0";
    public const string Commit = "b556139d8d094a088c786f90c319df44e0fa3e51";
    public static string Display => Version + " · commit " + Commit;
}
