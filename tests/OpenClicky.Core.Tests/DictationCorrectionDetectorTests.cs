using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Tests;

public class DictationCorrectionDetectorTests
{
    private const string Before = "Note: ", Inserted = "deploy to kubernates today", After = "Note: deploy to kubernates today";

    private static DictationCorrectionDetector Detector(params string[] dictionary) => new(Before, After, Inserted, dictionary);

    [Fact]
    public void SingleWordFixLearned()
    {
        var d = Detector();
        const string fixedValue = "Note: deploy to Kubernetes today";
        Assert.Null(d.Observe(fixedValue));
        Assert.Equal("Kubernetes", d.Observe(fixedValue));
        Assert.Null(d.Observe(fixedValue));
        Assert.Null(d.Observe(fixedValue));
    }

    [Fact]
    public void UnstableValueIgnored()
    {
        var d = Detector();
        Assert.Null(d.Observe("Note: deploy to Kubernetes today"));
        Assert.Null(d.Observe("Note: deploy to kubernates today"));
        Assert.Null(d.Observe("Note: deploy to Kubernetes today"));
    }

    [Fact] public void AppendIsNotACorrection() => AssertIgnored("Note: deploy to kubernates today and more");
    [Fact] public void TwoChangedWordsIgnored() => AssertIgnored("Note: deploy two Kubernetes today");
    [Fact] public void ChangeOutsideInsertedSpanIgnored() => AssertIgnored("Notes: deploy to kubernates today");
    [Fact] public void CaseOrPunctuationOnlyIgnored() { AssertIgnored("Note: deploy to Kubernates today"); AssertIgnored("Note: deploy to kubernates, today"); }
    [Fact] public void DissimilarWordIgnored() => AssertIgnored("Note: deploy to banana today");
    [Fact] public void ShortWordIgnored() => AssertIgnored("Note: deploy to kubernates tx");

    [Fact]
    public void AlreadyInDictionaryIgnored()
    {
        var d = Detector("kubernetes");
        Assert.Null(d.Observe("Note: deploy to Kubernetes today"));
        Assert.Null(d.Observe("Note: deploy to Kubernetes today"));
    }

    [Fact]
    public void SuffixAfterTheSpanIsKept()
    {
        var d = new DictationCorrectionDetector("Hi  there", "Hi deploy to kubernates today there", "deploy to kubernates today", []);
        Assert.Null(d.Observe("Hi deploy to Kubernetes today there"));
        Assert.Equal("Kubernetes", d.Observe("Hi deploy to Kubernetes today there"));
    }

    [Fact]
    public void IsSpent_WhenSpanLostOrWordLearned()
    {
        var d = Detector();
        Assert.False(d.IsSpent);
        Assert.Null(d.Observe("Note: deploy to kubernates today and more")); // still inside the span: keep watching
        Assert.False(d.IsSpent);
        Assert.Null(d.Observe("Notes: deploy to kubernates today"));
        Assert.True(d.IsSpent);

        var learned = Detector();
        learned.Observe("Note: deploy to Kubernetes today");
        Assert.False(learned.IsSpent);
        Assert.Equal("Kubernetes", learned.Observe("Note: deploy to Kubernetes today"));
        Assert.True(learned.IsSpent);
    }

    [Fact]
    public void NoSpaceScriptFixLearnsNothing()
    {
        foreach (var (before, after) in new[] { ("今天我们去公园玩", "今天我们去公园完"), ("これはペンです", "これはペソです") })
        {
            var d = new DictationCorrectionDetector("", before, before, []);
            Assert.Null(d.Observe(after));
            Assert.Null(d.Observe(after));
        }
    }

    private static void AssertIgnored(string value)
    {
        var d = Detector();
        Assert.Null(d.Observe(value));
        Assert.Null(d.Observe(value));
    }
}
