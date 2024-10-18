using ChatAIze.GenerativeCS.Constants;
using ChatAIze.GenerativeCS.Interfaces;
using ChatAIze.GenerativeCS.Models;

namespace ChatAIze.GenerativeCS.Options.OpenAI;

public record ChatCompletionOptions<TMessage, TFunctionCall, TFunctionResult>
    where TMessage : IChatMessage<TFunctionCall, TFunctionResult>
    where TFunctionCall : IFunctionCall
    where TFunctionResult : IFunctionResult
{
    public ChatCompletionOptions(string model = DefaultModels.OpenAI.ChatCompletion, string? apiKey = null)
    {
        Model = model;
        ApiKey = apiKey;
    }

    public string Model { get; set; } = DefaultModels.OpenAI.ChatCompletion;

    public string? ApiKey { get; set; }

    public string? UserTrackingId { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public int? MaxOutputTokens { get; set; }

    public int? MessageLimit { get; set; }

    public int? CharacterLimit { get; set; }

    public int? Seed { get; set; }

    public double? Temperature { get; set; }

    public double? TopP { get; set; }

    public double? FrequencyPenalty { get; set; }

    public double? PresencePenalty { get; set; }

    public Type? ResponseType { get; set; }

    public bool IsJsonMode { get; set; }

    public bool IsParallelFunctionCallingOn { get; set; } = true;

    public bool IsStrictFunctionCallingOn { get; set; }

    public bool IsStoringOutputs { get; set; }

    public bool IsTimeAware { get; set; }

    public bool IsIgnoringPreviousFunctionCalls { get; set; }

    public bool IsDebugMode { get; set; }

    public List<string> StopWords { get; set; } = [];

    public List<ChatFunction> Functions { get; set; } = [];

    public Func<string?>? SystemMessageCallback { get; set; } = null;

    public Func<TMessage, Task> AddMessageCallback { get; set; } = (_) => Task.CompletedTask;

    public Func<string, string?, CancellationToken, Task<object?>> DefaultFunctionCallback { get; set; } = (_, _, _) => throw new NotImplementedException("Function callback has not been implemented.");

    public Func<DateTime> TimeCallback { get; set; } = () => DateTime.Now;

    public void AddFunction(ChatFunction function)
    {
        Functions.Add(function);
    }

    public void AddFunction(string name, bool requiresConfirmation = false)
    {
        Functions.Add(new ChatFunction(name, requiresConfirmation));
    }

    public void AddFunction(string name, string? description, bool requiresConfirmation = false)
    {
        Functions.Add(new ChatFunction(name, description, requiresConfirmation));
    }

    public void AddFunction(Delegate callback)
    {
        Functions.Add(new ChatFunction(callback));
    }

    public void AddFunction(string name, Delegate callback)
    {
        Functions.Add(new ChatFunction(name, callback));
    }

    public void AddFunction(string name, IEnumerable<FunctionParameter> parameters)
    {
        Functions.Add(new ChatFunction(name, parameters));
    }

    public void AddFunction(string name, params FunctionParameter[] parameters)
    {
        Functions.Add(new ChatFunction(name, parameters));
    }

    public void AddFunction(string name, string? description, Delegate callback)
    {
        Functions.Add(new ChatFunction(name, description, callback));
    }

    public void AddFunction(string name, string? description, IEnumerable<FunctionParameter> parameters)
    {
        Functions.Add(new ChatFunction(name, description, parameters));
    }

    public void AddFunction(string name, string? description, params FunctionParameter[] parameters)
    {
        Functions.Add(new ChatFunction(name, description, parameters));
    }

    public void AddFunction(string name, bool requiresConfirmation, Delegate callback)
    {
        Functions.Add(new ChatFunction(name, requiresConfirmation, callback));
    }

    public void AddFunction(string name, bool requiresConfirmation, IEnumerable<FunctionParameter> parameters)
    {
        Functions.Add(new ChatFunction(name, requiresConfirmation, parameters));
    }

    public void AddFunction(string name, bool requiresConfirmation, params FunctionParameter[] parameters)
    {
        Functions.Add(new ChatFunction(name, requiresConfirmation, parameters));
    }

    public void AddFunction(string name, string? description, bool requiresConfirmation, Delegate callback)
    {
        Functions.Add(new ChatFunction(name, description, requiresConfirmation, callback));
    }

    public void AddFunction(string name, string? description, bool requiresConfirmation, IEnumerable<FunctionParameter> parameters)
    {
        Functions.Add(new ChatFunction(name, description, requiresConfirmation, parameters));
    }

    public void AddFunction(string name, string? description, bool requiresConfirmation, params FunctionParameter[] parameters)
    {
        Functions.Add(new ChatFunction(name, description, requiresConfirmation, parameters));
    }

    public bool RemoveFunction(ChatFunction function)
    {
        return Functions.Remove(function);
    }

    public bool RemoveFunction(string name)
    {
        var function = Functions.LastOrDefault(f => f.Name == name);
        if (function is null)
        {
            return false;
        }

        return Functions.Remove(function);
    }

    public bool RemoveFunction(Delegate callback)
    {
        var function = Functions.LastOrDefault(f => f.Callback == callback);
        if (function is null)
        {
            return false;
        }

        return Functions.Remove(function);
    }

    public void ClearFunctions()
    {
        Functions.Clear();
    }
}

public record ChatCompletionOptions : ChatCompletionOptions<ChatMessage, FunctionCall, FunctionResult>
{
    public ChatCompletionOptions(string model = DefaultModels.OpenAI.ChatCompletion) : base(model) { }
}
