namespace StatisticsAnalysisTool.Imortais.DeathReview;

/// <summary>
/// Visibility and startup gate for the optional, local-only Death Review notice.
/// No data capture starts before the notice is acknowledged.
/// </summary>
internal static class DeathReviewStartupNoticePolicy
{
    public static bool CanShow(bool splashClosed, bool windowVisible,
        bool minimized, bool noticeAcknowledged) =>
        splashClosed && windowVisible && !minimized && !noticeAcknowledged;
}
