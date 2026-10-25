using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public class HowTosTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    private static string TempFile() => Path.Combine(Directory.CreateTempSubdirectory("howtos").FullName, "howtos.json");

    [Fact]
    public void AddDedupesCaseInsensitive()
    {
        var store = new HowToStore(TempFile());
        store.Add("Use CSV for exports", "1", T0);
        store.Add("  use csv FOR exports ", "2", T0.AddMinutes(1));
        var all = store.All;
        Assert.Single(all);
        Assert.Equal("2", all[0].JobId);
    }

    [Fact]
    public void CapsAt240Chars()
    {
        var store = new HowToStore(TempFile());
        store.Add(new string('x', 500), "1", T0);
        Assert.Equal(240, store.All[0].Text.Length);
    }

    [Fact]
    public void StripsNewlinesControlCharsAndQuotes()
    {
        var store = new HowToStore(TempFile());
        store.Add("line one\r\nline\ttwo\u0007 \"quoted\"", "1", T0);
        Assert.Equal("line one line two 'quoted'", store.All[0].Text);
        store.Add("\n \u0000 ", "2", T0);
        Assert.Single(store.All);
    }

    [Fact]
    public void KeepsSixtyNewest()
    {
        var path = TempFile();
        var store = new HowToStore(path);
        for (var i = 0; i < 75; i++) store.Add("lesson " + i, "j" + i, T0.AddMinutes(i));
        Assert.Equal(60, store.All.Count);
        Assert.DoesNotContain(store.All, h => h.Text == "lesson 14");
        Assert.Contains(store.All, h => h.Text == "lesson 15");
        store.Save();
        Assert.Equal(60, new HowToStore(path).All.Count);
    }

    [Fact]
    public void SaveAndReloadRoundTrips_AndLeavesNoTempFile()
    {
        var path = TempFile();
        var store = new HowToStore(path);
        store.Add("Prefer the sidebar", "7", T0);
        store.Save();
        store.Add("Second", "8", T0.AddMinutes(1));
        store.Save();
        Assert.Equal(["Prefer the sidebar", "Second"], new HowToStore(path).All.Select(h => h.Text).Order());
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void CorruptFileStartsEmpty()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ not json [");
        var store = new HowToStore(path);
        Assert.Empty(store.All);
        store.Add("fresh", "1", T0);
        store.Save();
        Assert.Single(new HowToStore(path).All);
    }

    [Fact]
    public void ConcurrentAddsAndSavesAreSafe()
    {
        var path = TempFile();
        var store = new HowToStore(path);
        Parallel.For(0, 40, i => { store.Add("lesson " + i, "j" + i, T0.AddSeconds(i)); store.Save(); });
        Assert.Equal(40, new HowToStore(path).All.Count);
    }

    private static HowTo H(string text, int minute) => new(text, T0.AddMinutes(minute), "j");

    [Fact]
    public void ChooseNewestTenPlusFiveRelated()
    {
        var all = new List<HowTo>();
        for (var i = 0; i < 20; i++) all.Add(H("filler note " + i, i));
        all.Add(H("invoices go in the finance folder", -5)); // old, shares 3 words
        all.Add(H("invoices paid by finance", -4));          // old, shares 2 words, older than the next
        all.Add(H("finance folder invoices archive", -3));   // old, shares 4 words
        all.Add(H("finance folder is shared", -2));          // old, shares 2 words
        all.Add(H("unrelated trivia", -1));
        var chosen = HowToChooser.Choose(all, "Sort the invoices into the finance folder archive");
        Assert.Equal(14, chosen.Count);
        Assert.Equal(Enumerable.Range(10, 10).Reverse().Select(i => "filler note " + i), chosen.Take(10).Select(h => h.Text));
        Assert.Equal(["finance folder invoices archive", "invoices go in the finance folder", "finance folder is shared", "invoices paid by finance"],
            chosen.Skip(10).Select(h => h.Text));
        Assert.Equal(chosen.Count, chosen.Distinct().Count());
    }

    [Fact]
    public void RelatedAreCappedAtFive()
    {
        var all = Enumerable.Range(0, 30).Select(i => H("budget item " + i, i)).ToList();
        Assert.Equal(15, HowToChooser.Choose(all, "budget").Count);
    }

    [Fact]
    public void StopWordsIgnored()
    {
        var all = new List<HowTo> { H("this is the one", 0) };
        for (var i = 1; i <= 10; i++) all.Add(H("pad " + i, i));
        // "one" is a real word and relates; the stop words around it do not
        Assert.Contains(HowToChooser.Choose(all, "one"), h => h.Text == "this is the one");
        Assert.DoesNotContain(HowToChooser.Choose(all, "this is the"), h => h.Text == "this is the one");
    }

    [Fact]
    public void EmptyQueryGivesNewestOnly()
    {
        var all = Enumerable.Range(0, 20).Select(i => H("note " + i, i)).ToList();
        Assert.Equal(10, HowToChooser.Choose(all, "").Count);
        Assert.Equal(10, HowToChooser.Choose(all, "the and of").Count);
        Assert.Empty(HowToChooser.Choose([], "anything"));
    }
}
