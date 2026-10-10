namespace OpenClicky.Core;

public sealed record PointingParseResult(
    string SpokenText,
    double? X,
    double? Y,
    string? ElementLabel,
    int? ScreenNumber,
    bool IsNone);
