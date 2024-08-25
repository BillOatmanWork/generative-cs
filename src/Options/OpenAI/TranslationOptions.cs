using ChatAIze.GenerativeCS.Constants;
using ChatAIze.GenerativeCS.Enums;

namespace ChatAIze.GenerativeCS.Options.OpenAI;

public record TranslationOptions
{
    public TranslationOptions(string? prompt = null, string? apiKey = null)
    {
        Prompt = prompt;
        ApiKey = apiKey;
    }

    public string Model { get; set; } = DefaultModels.OpenAI.SpeechToText;

    public string? ApiKey { get; set; }

    public string? Prompt { get; set; }

    public double Temperature { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public TranscriptionResponseFormat ResponseFormat { get; set; } = TranscriptionResponseFormat.Text;
}
