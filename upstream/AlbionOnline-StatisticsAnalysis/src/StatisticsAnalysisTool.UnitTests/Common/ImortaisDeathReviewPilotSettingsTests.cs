using NUnit.Framework;
using StatisticsAnalysisTool.Common.UserSettings;
using System.Text.Json;

namespace StatisticsAnalysisTool.UnitTests.Common;

[TestFixture]
public sealed class ImortaisDeathReviewPilotSettingsTests
{
    [Test]
    public void UpgradeFrom062_EnablesPilotOnce_PreservesExistingRecordingChoice()
    {
        var settings = new SettingsObject
        {
            ImortaisSettingsSchemaVersion = 1,
            IsImortaisDeathReviewEnabled = false,
            HasSeenImortaisDeathReviewCorePilotNotice = true,
            IsImortaisBackgroundRecordingEnabled = true
        };

        Assert.That(SettingsController.ApplyImortaisSettingsMigration(settings), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(settings.ImortaisSettingsSchemaVersion, Is.EqualTo(2));
            Assert.That(settings.IsImortaisDeathReviewEnabled, Is.True);
            Assert.That(settings.HasSeenImortaisDeathReviewCorePilotNotice, Is.False);
            Assert.That(settings.IsImortaisBackgroundRecordingEnabled, Is.True,
                "must not repeat old v0.6.0 recording migration");
        });
    }

    [Test]
    public void PilotOptOut_RemainsDisabledAfterSubsequentLoadsAndMigrations()
    {
        var settings = new SettingsObject { ImortaisSettingsSchemaVersion = 1 };
        Assert.That(SettingsController.ApplyImortaisSettingsMigration(settings), Is.True);
        settings.IsImortaisDeathReviewEnabled = false;
        settings.HasSeenImortaisDeathReviewCorePilotNotice = true;

        var roundTrip = JsonSerializer.Deserialize<SettingsObject>(
            JsonSerializer.Serialize(settings))!;
        Assert.That(SettingsController.ApplyImortaisSettingsMigration(roundTrip), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(roundTrip.IsImortaisDeathReviewEnabled, Is.False,
                "user opt-out cannot be reactivated on next startup");
            Assert.That(roundTrip.HasSeenImortaisDeathReviewCorePilotNotice, Is.True,
                "notice must not be shown on every startup");
        });
    }

    [Test]
    public void FirstInstall_PilotIsPreconfiguredButNoticeIsPending()
    {
        var settings = new SettingsObject();
        Assert.That(settings.IsImortaisDeathReviewEnabled, Is.True);
        Assert.That(settings.HasSeenImortaisDeathReviewCorePilotNotice, Is.False);

        Assert.That(SettingsController.ApplyImortaisSettingsMigration(settings), Is.True);
        Assert.That(settings.ImortaisSettingsSchemaVersion, Is.EqualTo(2));
        Assert.That(settings.IsImortaisDeathReviewEnabled, Is.True);
    }

    [Test]
    public void OriginalPre060User_KeepsOriginalBackgroundRecordingSafetyMigration()
    {
        var settings = new SettingsObject
        {
            ImortaisSettingsSchemaVersion = 0,
            IsImortaisBackgroundRecordingEnabled = true
        };
        Assert.That(SettingsController.ApplyImortaisSettingsMigration(settings), Is.True);
        Assert.That(settings.IsImortaisBackgroundRecordingEnabled, Is.False);
        Assert.That(settings.IsImortaisDeathReviewEnabled, Is.True);
    }
}
