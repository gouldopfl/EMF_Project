using EMF.Core.Models;

namespace EMF.Core.Services;

public static class OcrLanguageResolver
{
    public static OcrLanguage Resolve(string? language) =>
        language?.Trim().ToLowerInvariant() switch
        {
            "zh" or "chinese" => OcrLanguage.Chinese,
            "ko" or "korean" => OcrLanguage.Korean,
            "ar" or "arabic" => OcrLanguage.Arabic,
            "el" or "greek" => OcrLanguage.Greek,
            "th" or "thai" => OcrLanguage.Thai,
            "cyrillic" => OcrLanguage.Cyrillic,
            "es" or "spanish" or
            "de" or "german" or
            "fr" or "french" or
            "it" or "italian" or
            "pt" or "portuguese" or
            "latin" => OcrLanguage.Latin,
            _ => OcrLanguage.English
        };
}
