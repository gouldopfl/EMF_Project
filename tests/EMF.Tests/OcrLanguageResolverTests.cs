using EMF.Core.Models;
using EMF.Core.Services;

namespace EMF.Tests;

public sealed class OcrLanguageResolverTests
{
    [Theory]
    [InlineData(null, OcrLanguage.English)]
    [InlineData("", OcrLanguage.English)]
    [InlineData("english", OcrLanguage.English)]
    [InlineData("es", OcrLanguage.Latin)]
    [InlineData("spanish", OcrLanguage.Latin)]
    [InlineData("de", OcrLanguage.Latin)]
    [InlineData("german", OcrLanguage.Latin)]
    [InlineData("fr", OcrLanguage.Latin)]
    [InlineData("french", OcrLanguage.Latin)]
    [InlineData("it", OcrLanguage.Latin)]
    [InlineData("portuguese", OcrLanguage.Latin)]
    [InlineData("zh", OcrLanguage.Chinese)]
    [InlineData("korean", OcrLanguage.Korean)]
    [InlineData("arabic", OcrLanguage.Arabic)]
    [InlineData("greek", OcrLanguage.Greek)]
    [InlineData("thai", OcrLanguage.Thai)]
    [InlineData("cyrillic", OcrLanguage.Cyrillic)]
    public void Resolve_MapsSupportedLanguages(
        string? language,
        OcrLanguage expected)
    {
        Assert.Equal(
            expected,
            OcrLanguageResolver.Resolve(language));
    }
}
