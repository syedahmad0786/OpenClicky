using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class IntelligentUiDocumentTests
{
    [Fact]
    public void Parse_ReadsBoundedAgUiCustomDocumentAndKeepsPlainText()
    {
        const string reply = """
            Here is the rollout path.

            ```ag-ui
            {
              "type": "CUSTOM",
              "name": "openclicky.ui.document",
              "value": {
                "version": 1,
                "title": "Release plan",
                "summary": "Two steps remain.",
                "cards": [{"title":"Coverage","value":"82%","detail":"Focused tests pass","tone":"good"}],
                "nodes": [
                  {"id":"build","title":"Build","detail":"Compile Release","status":"done"},
                  {"id":"verify","title":"Verify","detail":"Run focused checks","status":"current"}
                ],
                "edges": [{"from":"build","to":"verify","label":"then"}],
                "actions": [{"label":"Show risks","prompt":"Explain the remaining release risks"}]
              }
            }
            ```
            """;

        var parsed = IntelligentUiParser.Parse(reply);

        Assert.Equal("Here is the rollout path.", parsed.PlainText);
        Assert.NotNull(parsed.Document);
        Assert.Equal("Release plan", parsed.Document.Title);
        Assert.Equal("82%", Assert.Single(parsed.Document.Cards).Value);
        Assert.Equal("verify", Assert.Single(parsed.Document.Edges).To);
        Assert.Equal("Explain the remaining release risks", Assert.Single(parsed.Document.Actions).Prompt);
    }

    [Fact]
    public void Parse_InvalidOrUnrelatedEventIsNotRenderedAndJsonIsNotSpoken()
    {
        const string reply = """
            A safe text answer.
            ```ag-ui
            {"type":"CUSTOM","name":"somewhere.else","value":{"version":1,"title":"Ignore me"}}
            ```
            """;

        var parsed = IntelligentUiParser.Parse(reply);

        Assert.Equal("A safe text answer.", parsed.PlainText);
        Assert.Null(parsed.Document);
    }

    [Fact]
    public void Parse_RejectsBrokenReferencesAndUnsafeSizes()
    {
        var broken = Wrap("""
            {"version":1,"title":"Flow","nodes":[{"id":"one","title":"One"}],
             "edges":[{"from":"one","to":"missing"}]}
            """);
        var tooManyCards = string.Join(',', Enumerable.Range(0, IntelligentUiParser.MaxCards + 1)
            .Select(index => $"{{\"title\":\"{index}\",\"value\":\"x\"}}"));

        Assert.Null(IntelligentUiParser.Parse(broken).Document);
        Assert.Null(IntelligentUiParser.Parse(Wrap($"{{\"version\":1,\"title\":\"Cards\",\"cards\":[{tooManyCards}]}}" )).Document);
    }

    [Fact]
    public void Parse_UsesSummaryWhenValidDocumentHasNoSurroundingText()
    {
        var parsed = IntelligentUiParser.Parse(Wrap("{\"version\":1,\"title\":\"Flow\",\"summary\":\"A spoken summary.\"}"));

        Assert.Equal("A spoken summary.", parsed.PlainText);
        Assert.NotNull(parsed.Document);
    }

    [Fact]
    public void Parse_StripsIncompletePayloadFromSpeechAndHistory()
    {
        var parsed = IntelligentUiParser.Parse("Readable answer.\n```ag-ui\n{\"type\":\"CUSTOM\",\"value\":");

        Assert.Equal("Readable answer.", parsed.PlainText);
        Assert.Null(parsed.Document);
        Assert.DoesNotContain("CUSTOM", parsed.PlainText);
    }

    [Fact]
    public void Parse_StripsEveryPayloadAndRejectsAmbiguousRepeatedEvents()
    {
        var repeated = "Before.\n" + Wrap("{\"version\":1,\"title\":\"One\"}")
            + "\nBetween.\n" + Wrap("{\"version\":1,\"title\":\"Two\"}") + "\nAfter.";

        var parsed = IntelligentUiParser.Parse(repeated);

        Assert.Equal("Before.\nBetween.\nAfter.", parsed.PlainText);
        Assert.Null(parsed.Document);
        Assert.DoesNotContain("openclicky.ui.document", parsed.PlainText);
    }

    [Fact]
    public void Parse_OversizedReplyStillStripsPayload()
    {
        var prefix = new string('a', IntelligentUiParser.MaxReplyLength + 1);
        var parsed = IntelligentUiParser.Parse(prefix + "\n" + Wrap("{\"version\":1,\"title\":\"Hidden\"}"));

        Assert.Null(parsed.Document);
        Assert.DoesNotContain("Hidden", parsed.PlainText);
        Assert.DoesNotContain("CUSTOM", parsed.PlainText);
        Assert.EndsWith("…", parsed.PlainText);
    }

    [Fact]
    public void Parse_TruncatedOpeningFenceUsesSafeFallback()
    {
        var parsed = IntelligentUiParser.Parse("```ag-ui");

        Assert.Equal(IntelligentUiParser.SafeFallbackText, parsed.PlainText);
        Assert.Null(parsed.Document);
        Assert.DoesNotContain("ag-ui", parsed.PlainText);
    }

    [Theory]
    [InlineData("{\"version\":2,\"title\":\"Future\"}")]
    [InlineData("{\"version\":1,\"title\":\"Wrong envelope\"}", "other.event")]
    public void Parse_UnknownVersionOrSchemaUsesSafePlainFallback(string value, string? eventName = null)
    {
        var raw = Wrap(value, eventName ?? IntelligentUiParser.EventName);
        var parsed = IntelligentUiParser.Parse(raw);

        Assert.Equal(IntelligentUiParser.SafeFallbackText, parsed.PlainText);
        Assert.Null(parsed.Document);
        Assert.DoesNotContain("version", parsed.PlainText);
    }

    private static string Wrap(string value, string eventName = IntelligentUiParser.EventName) => $$"""
        ```ag-ui
        {"type":"CUSTOM","name":"{{eventName}}","value":{{value}}}
        ```
        """;
}
