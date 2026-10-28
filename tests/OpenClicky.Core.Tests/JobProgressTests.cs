using System.Text.Json;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public class JobProgressTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void TodosBecomePlanSteps()
    {
        var plan = JobProgress.FromTodos(Json("""
            [{"content":"Find the page","status":"completed"},{"content":"Skip it","status":"cancelled"},
             {"content":"Write it up","status":"in_progress"},{"content":"Send","status":"pending"},{"status":"pending"},{"content":"  "},"junk"]
            """));
        Assert.Equal([new PlanStep("Find the page", true), new PlanStep("Skip it", true), new PlanStep("Write it up", false), new PlanStep("Send", false)], plan);
        Assert.Empty(JobProgress.FromTodos(Json("{}")));
    }

    [Fact]
    public void TodosAreCappedAt24()
    {
        var many = "[" + string.Join(",", Enumerable.Range(0, 40).Select(i => $$"""{"content":"Step {{i}}","status":"pending"}""")) + "]";
        var plan = JobProgress.FromTodos(Json(many));
        Assert.Equal(24, plan.Count);
        Assert.Equal("Step 23", plan[^1].Text);
    }

    [Theory]
    [InlineData("openclicky_observe_screen", "{}", "Looking at the screen")]
    [InlineData("openclicky_uia", "{}", "Reading the window")]
    [InlineData("openclicky_click", """{"x":"1"}""", "Clicking")]
    [InlineData("openclicky_type_text", """{"text":"secret"}""", "Typing")]
    [InlineData("openclicky_hotkey", """{"keys":"ctrl+n"}""", "Pressing keys")]
    [InlineData("openclicky_scroll", "{}", "Scrolling")]
    [InlineData("openclicky_open_app", """{"app":"Notepad"}""", "Opening Notepad")]
    [InlineData("openclicky_open_app", """{"app_id":"Microsoft.Notepad"}""", "Opening Microsoft.Notepad")]
    [InlineData("openclicky_open_app", "{}", "Opening an app")]
    [InlineData("openclicky_write_output", """{"path":"data/report.md","content":"x"}""", "Saving report.md")]
    [InlineData("openclicky_write_output", "{}", "Saving a file")]
    [InlineData("openclicky_list_outputs", "{}", "Checking saved files")]
    [InlineData("openclicky_connector_call", """{"connector":"github","tool":"x"}""", "Using github")]
    [InlineData("openclicky_connector_call", "{}", "Using a connector")]
    [InlineData("webfetch", "{}", "Reading a web page")]
    [InlineData("websearch", "{}", "Searching the web")]
    [InlineData("bash", "{}", "Running a command")]
    [InlineData("read", "{}", "Reading a file")]
    [InlineData("edit", "{}", "Editing a file")]
    [InlineData("write", "{}", "Editing a file")]
    [InlineData("grep", "{}", "Looking through files")]
    [InlineData("glob", "{}", "Looking through files")]
    [InlineData("list", "{}", "Looking through files")]
    [InlineData("openclicky_openclicky_browser_click", "{}", "Clicking")]
    [InlineData("openclicky_browser_observe", "{}", "Reading the page")]
    [InlineData("OpenClicky_Observe_Screen", "{}", "Looking at the screen")]
    public void PhrasesForKnownTools(string tool, string input, string expected) =>
        Assert.Equal(expected, JobProgress.Phrase(tool, Json(input)));

    [Fact]
    public void PlaceholdersAreCutTo40AndCleaned()
    {
        Assert.Equal("Opening " + new string('a', 40), JobProgress.Phrase("openclicky_open_app", Json($$"""{"app":"{{new string('a', 90)}}"}""")));
        Assert.Equal("Using a b", JobProgress.Phrase("openclicky_connector_call", Json("""{"connector":"a\r\n\tb"}""")));
        Assert.Equal("Opening an app", JobProgress.Phrase("openclicky_open_app", Json("""{"app":42}""")));
        Assert.Equal("Opening an app", JobProgress.Phrase("openclicky_open_app", null));
    }

    [Fact]
    public void UnknownToolGivesNull()
    {
        Assert.Null(JobProgress.Phrase("openclicky_mystery", Json("{}")));
        Assert.Null(JobProgress.Phrase("", null));
        Assert.Null(JobProgress.Phrase("todowrite", Json("{}")));
    }

    [Fact]
    public void KeepsLast12Steps()
    {
        var live = JobLiveState.Empty;
        for (var i = 0; i < 15; i++) live = live.Apply("openclicky_open_app", Json($$"""{"app":"App{{i}}"}"""));
        Assert.Equal(12, live.Steps.Count);
        Assert.Equal("Opening App3", live.Steps[0]);
        Assert.Equal("Opening App14", live.Steps[^1]);
        Assert.Equal("Opening App14", live.LiveLine);
    }

    [Fact]
    public void ApplyKeepsThePlanAndIgnoresUnknownTools()
    {
        var live = JobLiveState.Empty
            .Apply("todowrite", Json("""{"todos":[{"content":"A","status":"completed"},{"content":"B","status":"pending"}]}"""))
            .Apply("openclicky_click", Json("{}"))
            .Apply("openclicky_mystery", Json("{}"))
            .Apply("openclicky_openclicky_todowrite", null);
        Assert.Equal([new PlanStep("A", true), new PlanStep("B", false)], live.Plan);
        Assert.Equal(["Clicking"], live.Steps);
        Assert.Equal("Clicking", live.LiveLine);
        Assert.Empty(live.Apply("todowrite", Json("""{"todos":[]}""")).Plan);
    }

    [Theory]
    [InlineData("Looking at the page.", "Looking at the page.")]
    [InlineData("Done.\n\n```openclicky-result\n{\"files\":[\"a.md\"]}\n```", "Done.")]
    [InlineData("Done.\n```openclicky-res", "Done.")]
    [InlineData("```openclicky-result\n{}", "")]
    [InlineData("Done.\n   ```openclicky-result\n{}\n```", "Done.")]
    [InlineData("Done.\n\t```openclicky-result", "Done.")]
    [InlineData("Done.\r\n  ```openclicky-res", "Done.")]
    [InlineData("a ``` inline fence stays", "a ``` inline fence stays")]
    public void LiveTextNeverShowsTheResultBlock(string streamed, string expected) =>
        Assert.Equal(expected, JobProgress.LiveText(streamed));
}
