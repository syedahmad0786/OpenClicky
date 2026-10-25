using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Tests;

public class DictationFlowTests
{
    private static readonly DictationOptions On = new(Cleanup: true, Dictionary: [], CleanupTimeoutMs: 100);

    private sealed class FakeField(DictationFieldContext? context, bool verified = true) : IDictationField
    {
        public List<string> Calls { get; } = [];
        public string? Inserted { get; private set; }
        public Func<Exception>? ReadThrows { get; init; }
        public Func<Exception>? InsertThrows { get; init; }

        public Task<DictationFieldContext?> ReadContextAsync(CancellationToken ct)
        {
            Calls.Add("read");
            if (ReadThrows is not null) throw ReadThrows();
            return Task.FromResult(context);
        }

        public Task<FieldInsertOutcome> InsertAsync(string text, CancellationToken ct)
        {
            Calls.Add("insert");
            if (InsertThrows is not null) throw InsertThrows();
            Inserted = text;
            return Task.FromResult(new FieldInsertOutcome(verified, "", verified ? text : "", verified ? "ok" : "not seen"));
        }
    }

    private sealed class FakeCleaner(Func<string, DictationFieldContext?, CancellationToken, Task<string?>> clean, List<string>? calls = null) : IDictationCleaner
    {
        public int Count { get; private set; }
        public DictationFieldContext? SeenContext { get; private set; }

        public Task<string?> CleanAsync(string raw, DictationFieldContext? context, IReadOnlyList<string> dictionary, CancellationToken ct)
        {
            Count++;
            calls?.Add("clean");
            SeenContext = context;
            return clean(raw, context, ct);
        }
    }

    private static FakeCleaner Returns(string? text, List<string>? calls = null) => new((_, _, _) => Task.FromResult(text), calls);

    [Fact]
    public async Task CleanupFailureInsertsRaw()
    {
        var throwing = new FakeCleaner((_, _, _) => throw new InvalidOperationException("provider down"));
        var field = new FakeField(new DictationFieldContext("App", null, "Hello", "", "", false));
        var d = await DictationFlow.DeliverAsync("  this is raw  ", field, throwing, On, _ => { }, default);
        Assert.Equal(DictationDeliveryKind.Inserted, d.Kind);
        Assert.Equal(" this is raw", field.Inserted);

        var slow = new FakeCleaner(async (_, _, ct) => { await Task.Delay(5000, ct); return "too late"; });
        var field2 = new FakeField(null);
        var d2 = await DictationFlow.DeliverAsync("this is raw", field2, slow, On, _ => { }, default);
        Assert.Equal("this is raw", d2.Text);
        Assert.Equal("this is raw", field2.Inserted);

        var ignoresToken = new FakeCleaner(async (_, _, _) => { await Task.Delay(2000); return "too late"; });
        var d3 = await DictationFlow.DeliverAsync("this is raw", new FakeField(null), ignoresToken, On, _ => { }, default);
        Assert.Equal("this is raw", d3.Text);

        var nulls = Returns(null);
        var d4 = await DictationFlow.DeliverAsync("this is raw", new FakeField(null), nulls, On, _ => { }, default);
        Assert.Equal("this is raw", d4.Text);
    }

    [Fact]
    public async Task ShortTranscriptSkipsCleaner()
    {
        var cleaner = Returns("Changed.");
        var d = await DictationFlow.DeliverAsync("two words", new FakeField(null), cleaner, On, _ => { }, default);
        Assert.Equal(0, cleaner.Count);
        Assert.Equal("two words", d.Text);

        await DictationFlow.DeliverAsync("now three words", new FakeField(null), cleaner, On, _ => { }, default);
        Assert.Equal(1, cleaner.Count);
    }

    [Fact]
    public async Task NoFieldGoesToClipboard()
    {
        string? copied = null;
        var d = await DictationFlow.DeliverAsync("um send it now", null, Returns("Send it now."), On, t => copied = t, default);
        Assert.Equal(DictationDeliveryKind.Clipboard, d.Kind);
        Assert.Equal("Send it now.", copied);
        Assert.Equal("Send it now.", d.Text);
        Assert.Null(d.Insert);
    }

    [Fact]
    public async Task UnverifiedInsertGoesToClipboard()
    {
        string? copied = null;
        var field = new FakeField(null, verified: false);
        var d = await DictationFlow.DeliverAsync("send it now", field, null, On, t => copied = t, default);
        Assert.Equal(DictationDeliveryKind.Clipboard, d.Kind);
        Assert.Equal("send it now", copied);
        Assert.NotNull(d.Insert);
        Assert.False(d.Insert!.Verified);
    }

    [Fact]
    public async Task VerifiedInsertDoesNotTouchTheClipboard()
    {
        var copied = false;
        var d = await DictationFlow.DeliverAsync("send it now", new FakeField(null), null, On, _ => copied = true, default);
        Assert.Equal(DictationDeliveryKind.Inserted, d.Kind);
        Assert.False(copied);
    }

    [Fact]
    public async Task EmptyTranscriptIsNothing()
    {
        var cleaner = Returns("x y z");
        var field = new FakeField(null);
        var copied = false;
        var d = await DictationFlow.DeliverAsync(" \n\t ", field, cleaner, On, _ => copied = true, default);
        Assert.Equal(DictationDeliveryKind.Nothing, d.Kind);
        Assert.Equal(0, cleaner.Count);
        Assert.DoesNotContain("insert", field.Calls);
        Assert.False(copied);
    }

    [Fact]
    public async Task CleanupOffSkipsCleaner()
    {
        var cleaner = Returns("Changed.");
        var d = await DictationFlow.DeliverAsync("now three words", new FakeField(null), cleaner, On with { Cleanup = false }, _ => { }, default);
        Assert.Equal(0, cleaner.Count);
        Assert.Equal("now three words", d.Text);
    }

    [Fact]
    public async Task ContextIsReadBeforeCleanupAndUsedForFit()
    {
        var ctx = new DictationFieldContext("App", null, "Hello", "world", "", SingleLine: true);
        var field = new FakeField(ctx);
        var cleaner = new FakeCleaner((_, _, _) => Task.FromResult<string?>("there\nmy dear"), field.Calls);
        var d = await DictationFlow.DeliverAsync("there my dear", field, cleaner, On, _ => { }, default);
        Assert.Equal(["read", "clean", "insert"], field.Calls);
        Assert.Same(ctx, cleaner.SeenContext);
        Assert.Equal(" there my dear ", field.Inserted);
        Assert.Equal(" there my dear ", d.Text);
    }

    [Fact]
    public async Task ContextReadFailureStillDelivers()
    {
        var field = new FakeField(null) { ReadThrows = () => new InvalidOperationException("uia gone") };
        var d = await DictationFlow.DeliverAsync("send it now", field, null, On, _ => { }, default);
        Assert.Equal(DictationDeliveryKind.Inserted, d.Kind);
        Assert.Equal("send it now", field.Inserted);
    }

    [Fact]
    public async Task InsertFailureGoesToClipboard()
    {
        string? copied = null;
        var ctx = new DictationFieldContext("App", null, "Hello", "", "", false);
        var field = new FakeField(ctx) { InsertThrows = () => new InvalidOperationException("typing failed") };
        var d = await DictationFlow.DeliverAsync("send it now", field, null, On, t => copied = t, default);
        Assert.Equal(DictationDeliveryKind.Clipboard, d.Kind);
        Assert.Equal(" send it now", d.Text);
        Assert.Equal(" send it now", copied);
        Assert.False(d.Insert!.Verified);
        Assert.Equal("typing failed", d.Insert.Message);
    }

    [Theory]
    [InlineData("今日は会議です", 1)]   // 7 CJK/kana characters
    [InlineData("明日会議です", 1)]     // 6
    [InlineData("สวัสดีครับ", 1)]       // Thai, 10
    [InlineData("明日会議で", 0)]       // 5
    public async Task NoSpaceScriptTranscriptsReachTheCleanerFromSixCharacters(string raw, int expectedCalls)
    {
        var cleaner = Returns("Tidied");
        await DictationFlow.DeliverAsync(raw, new FakeField(null), cleaner, On, _ => { }, default);
        Assert.Equal(expectedCalls, cleaner.Count);
    }

    [Fact]
    public async Task CallerCancellationThrows()
    {
        using var cts = new CancellationTokenSource();
        var cleaner = new FakeCleaner(async (_, _, ct) => { cts.Cancel(); await Task.Delay(5000, ct); return "x"; });
        var field = new FakeField(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DictationFlow.DeliverAsync("now three words", field, cleaner, On with { CleanupTimeoutMs = 5000 }, _ => { }, cts.Token));
        Assert.DoesNotContain("insert", field.Calls);
    }
    [Fact]
    public async Task TwoClipboardDeliveriesInOneSessionAccumulate()
    {
        var delivered = ""; // the session's clipboard text, as the tray keeps it
        string? clipboard = null;
        void Copy(string fitted) { delivered = DictationTextFit.Append(delivered, fitted); clipboard = delivered; }

        await DictationFlow.DeliverAsync("First phrase.", null, null, On, Copy, CancellationToken.None);
        // An unverified field fitted the second phrase against its own text: the session's join still applies.
        await DictationFlow.DeliverAsync("Second phrase.", new FakeField(new("App", null, "x", "y", "", false), verified: false),
            null, On, Copy, CancellationToken.None);

        Assert.Equal("First phrase. Second phrase.", clipboard);
    }

    [Fact]
    public void AppendJoinsWithOneSpace()
    {
        Assert.Equal("Hello", DictationTextFit.Append("", " Hello "));
        Assert.Equal("Hello world", DictationTextFit.Append("Hello", "world"));
        Assert.Equal("Hello, world", DictationTextFit.Append("Hello", ", world"));
        Assert.Equal("你好世界", DictationTextFit.Append("你好", "世界"));
    }
}
