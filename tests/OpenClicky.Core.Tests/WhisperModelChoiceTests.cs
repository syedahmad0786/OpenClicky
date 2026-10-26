using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Tests;

public class WhisperModelChoiceTests
{
    [Fact]
    public void AutoUsesMultilingualWithAutoDetect() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.Multilingual, "auto"), WhisperModelChoice.Resolve(true, "fr", englishReady: true, multilingualReady: true));

    [Fact]
    public void AutoWithoutMultilingualFallsBackToEnglish() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.English, "en"), WhisperModelChoice.Resolve(true, "fr", englishReady: true, multilingualReady: false));

    [Fact]
    public void EnglishPrefersTheEnglishModel() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.English, "en"), WhisperModelChoice.Resolve(false, "en", englishReady: true, multilingualReady: true));

    [Fact]
    public void EnglishWithoutTheEnglishModelUsesMultilingualInEnglish() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.Multilingual, "en"), WhisperModelChoice.Resolve(false, "en", englishReady: false, multilingualReady: true));

    [Fact]
    public void OtherLanguageUsesMultilingualWithThatCode() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.Multilingual, "ur"), WhisperModelChoice.Resolve(false, "ur", englishReady: true, multilingualReady: true));

    [Fact]
    public void OtherLanguageWithoutMultilingualFallsBackToEnglish() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.English, "en"), WhisperModelChoice.Resolve(false, "ur", englishReady: true, multilingualReady: false));

    [Theory]
    [InlineData(true, "en")]
    [InlineData(false, "en")]
    [InlineData(false, "ur")]
    public void NeitherModelReturnsNull(bool autoDetect, string language) =>
        Assert.Null(WhisperModelChoice.Resolve(autoDetect, language, englishReady: false, multilingualReady: false));

    [Fact]
    public void AccurateChoiceUsesSmallEnglishForEnglish() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.SmallEnglish, "en"),
            WhisperModelChoice.Resolve(false, "en", englishReady: true, multilingualReady: true, accurateReady: true));

    [Fact]
    public void AccurateChoiceAloneIsEnoughForEnglish() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.SmallEnglish, "en"),
            WhisperModelChoice.Resolve(true, "fr", englishReady: false, multilingualReady: false, accurateReady: true));

    [Fact]
    public void AccurateChoiceLeavesOtherLanguagesOnMultilingual() =>
        Assert.Equal(new WhisperDecodePlan(WhisperModels.Multilingual, "auto"),
            WhisperModelChoice.Resolve(true, "en", englishReady: true, multilingualReady: true, accurateReady: true));

    [Fact]
    public void ModelsCarryTheVerifiedFiles()
    {
        Assert.Equal(("english-small", "ggml-small.en.bin", 487614201L, "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d", false,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin"),
            (WhisperModels.SmallEnglish.Id, WhisperModels.SmallEnglish.FileName, WhisperModels.SmallEnglish.Size, WhisperModels.SmallEnglish.Sha256,
                WhisperModels.SmallEnglish.Multilingual, WhisperModels.SmallEnglish.Url));
        Assert.Equal(("ggml-base.en.bin", 147964211L, "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002", false),
            (WhisperModels.English.FileName, WhisperModels.English.Size, WhisperModels.English.Sha256, WhisperModels.English.Multilingual));
        Assert.Equal(("ggml-base.bin", 147951465L, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", true,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"),
            (WhisperModels.Multilingual.FileName, WhisperModels.Multilingual.Size, WhisperModels.Multilingual.Sha256, WhisperModels.Multilingual.Multilingual,
                WhisperModels.Multilingual.Url));
    }
}
