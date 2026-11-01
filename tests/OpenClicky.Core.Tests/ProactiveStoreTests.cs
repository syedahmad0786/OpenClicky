using OpenClicky.Core.Proactive;

namespace OpenClicky.Core.Tests;

public class ProactiveStoreTests
{
    [Fact]
    public void RoundTripsAndToleratesCorruptFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-proactive-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "proactive.json");
        try
        {
            AssertEmpty(ProactiveStore.Load(path)); // no file (and no folder) → new state

            var at = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(2));
            var s = new ProactiveState { BriefDay = "2026-10-26" };
            s.Nudges.Shown.Add(at);
            s.Nudges.SnoozedUntil["notion"] = at.AddHours(48);
            s.Nudges.Never.Add("slack");
            s.Suggestions.Day = "2026-10-26";
            s.Suggestions.Failures = 2;
            s.Suggestions.NextAttempt = at.AddMinutes(15);
            s.Suggestions.Pending.Add(new("a1", "Tidy downloads", "Frees space.", "Tidy my downloads", at, at.AddHours(24)));
            s.Suggestions.Declined.Add("Write a poem");
            s.DismissedMeetings["evt-1"] = at;
            ProactiveStore.Save(s, path);

            var back = ProactiveStore.Load(path);
            Assert.Equal("2026-10-26", back.BriefDay);
            Assert.Equal([at], back.Nudges.Shown);
            Assert.Equal(at.AddHours(48), back.Nudges.SnoozedUntil["notion"]);
            Assert.Equal(["slack"], back.Nudges.Never);
            Assert.Equal(2, back.Suggestions.Failures);
            Assert.Equal(at.AddMinutes(15), back.Suggestions.NextAttempt);
            Assert.Equal(s.Suggestions.Pending, back.Suggestions.Pending);
            Assert.Equal(["Write a poem"], back.Suggestions.Declined);
            Assert.Equal(at, back.DismissedMeetings["evt-1"]);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));

            foreach (var bad in new[] { "{not json", "", "null", "[1,2]", "{\"BriefDay\":5}" })
            {
                File.WriteAllText(path, bad);
                AssertEmpty(ProactiveStore.Load(path));
            }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void NullCollectionsInTheFileBecomeEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-proactive-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "proactive.json");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var json in new[]
            {
                "{\"Nudges\":null,\"Suggestions\":null,\"DismissedMeetings\":null}",
                "{\"Nudges\":{\"Shown\":null,\"SnoozedUntil\":null,\"Never\":null},\"Suggestions\":{\"Pending\":null,\"Declined\":null}}",
                "{\"Nudges\":{\"Never\":[null]},\"Suggestions\":{\"Pending\":[null,{\"Id\":\"x\"}],\"Declined\":[null]}}",
            })
            {
                File.WriteAllText(path, json);
                AssertEmpty(ProactiveStore.Load(path));
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void AssertEmpty(ProactiveState s)
    {
        Assert.Null(s.BriefDay);
        Assert.Empty(s.Nudges.Shown);
        Assert.Empty(s.Nudges.SnoozedUntil);
        Assert.Empty(s.Nudges.Never);
        Assert.Empty(s.Suggestions.Pending);
        Assert.Empty(s.Suggestions.Declined);
        Assert.Empty(s.DismissedMeetings);
    }
}
