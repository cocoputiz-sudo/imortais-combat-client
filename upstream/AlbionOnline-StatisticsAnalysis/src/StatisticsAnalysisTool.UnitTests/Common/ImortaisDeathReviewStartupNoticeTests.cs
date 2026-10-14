using NUnit.Framework;
using StatisticsAnalysisTool.Imortais.DeathReview;

namespace StatisticsAnalysisTool.UnitTests.Common;

[TestFixture]
public sealed class ImortaisDeathReviewStartupNoticeTests
{
    [TestCase(false, true, false, false, false, TestName = "Never display before splash closes")]
    [TestCase(true, false, false, false, false, TestName = "Hidden tray window never displays modal")]
    [TestCase(true, true, true, false, false, TestName = "Minimized window never displays modal")]
    [TestCase(true, true, false, true, false, TestName = "Accepted notice never repeats")]
    [TestCase(true, true, false, false, true, TestName = "Visible main window after splash can present notice")]
    public void NoticeOnlyAppearsAfterStartupAndWhenVisible(
        bool splashClosed, bool visible, bool minimized, bool acknowledged, bool expected)
    {
        Assert.That(DeathReviewStartupNoticePolicy.CanShow(
            splashClosed, visible, minimized, acknowledged), Is.EqualTo(expected));
    }

    [Test]
    public void HiddenTrayStartup_WaitsUntilWindowRestored_NoSilentAcknowledgement()
    {
        var splashClosed = true;
        var hasSeen = false; // must not be modified while hidden
        Assert.That(DeathReviewStartupNoticePolicy.CanShow(
            splashClosed, false, false, hasSeen), Is.False);
        Assert.That(hasSeen, Is.False);
        Assert.That(DeathReviewStartupNoticePolicy.CanShow(
            splashClosed, true, false, hasSeen), Is.True);
    }
}
