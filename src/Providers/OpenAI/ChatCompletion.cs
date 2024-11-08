using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChatAIze.Abstractions.Chat;
using ChatAIze.GenerativeCS.Options.OpenAI;
using ChatAIze.GenerativeCS.Utilities;
using ChatAIze.Utilities;

namespace ChatAIze.GenerativeCS.Providers.OpenAI;

internal static class ChatCompletion
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal static async Task<string> CompleteAsync<TChat, TMessage, TFunctionCall, TFunctionResult>(TChat chat, string? apiKey, ChatCompletionOptions<TMessage, TFunctionCall, TFunctionResult>? options = null, TokenUsageTracker? usageTracker = null, HttpClient? httpClient = null, int recursion = 0, CancellationToken cancellationToken = default)
        where TChat : IChat<TMessage, TFunctionCall, TFunctionResult>
        where TMessage : IChatMessage<TFunctionCall, TFunctionResult>, new()
        where TFunctionCall : IFunctionCall, new()
        where TFunctionResult : IFunctionResult, new()
    {
        if (recursion >= 5)
        {
            throw new InvalidOperationException("Recursion limit reached (infinite loop detected).");
        }

        options ??= new();
        httpClient ??= new();

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            apiKey = options.ApiKey;
        }

        var request = CreateChatCompletionRequest(chat, options);
        if (options.IsDebugMode)
        {
            Debug.WriteLine(request.ToString());
        }

        using var response = await httpClient.RepeatPostAsJsonAsync("https://api.openai.com/v1/chat/completions", request, apiKey, options.MaxAttempts, cancellationToken);
        using var responseContent = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseDocument = await JsonDocument.ParseAsync(responseContent, cancellationToken: cancellationToken);

        if (options.IsDebugMode)
        {
            Debug.WriteLine(responseDocument.RootElement.ToString());
        }

        if (usageTracker != null)
        {
            var usage = responseDocument.RootElement.GetProperty("usage");
            var promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
            var cachedTokens = usage.GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32();
            var completionTokens = usage.GetProperty("completion_tokens").GetInt32();

            usageTracker.AddPromptTokens(promptTokens);
            usageTracker.AddCachedTokens(cachedTokens);
            usageTracker.AddCompletionTokens(completionTokens);
        }

        var generatedMessage = responseDocument.RootElement.GetProperty("choices")[0].GetProperty("message");
        if (generatedMessage.TryGetProperty("tool_calls", out var toolCallsElement))
        {
            foreach (var toolCallElement in toolCallsElement.EnumerateArray())
            {
                if (toolCallElement.GetProperty("type").GetString() == "function")
                {
                    var toolCallId = toolCallElement.GetProperty("id").GetString()!;
                    var functionElement = toolCallElement.GetProperty("function");
                    var functionName = functionElement.GetProperty("name").GetString()!;
                    var functionArguments = functionElement.GetProperty("arguments").GetString()!;

                    var message1 = await chat.FromChatbotAsync(new TFunctionCall { ToolCallId = toolCallId, Name = functionName, Arguments = functionArguments });
                    await options.AddMessageCallback(message1);

                    var function = options.Functions.FirstOrDefault(f => f.Name.NormalizedEquals(functionName));
                    if (function != null)
                    {
                        if (function.RequiresDoubleCheck && chat.Messages.Count(m => m.FunctionCalls.Any(c => c.Name == functionName)) % 2 != 0)
                        {
                            var message2 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = toolCallId, Name = functionName, Value = "Before executing, are you sure the user wants to run this function? If yes, call it again to confirm." });
                            await options.AddMessageCallback(message2);
                        }
                        else
                        {
                            if (function.Callback != null)
                            {
                                var functionValue = await FunctionInvoker.InvokeAsync(function.Callback, functionArguments, options.ExecutionContext, cancellationToken);
                                var message3 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = toolCallId, Name = functionName, Value = functionValue });

                                await options.AddMessageCallback(message3);
                            }
                            else
                            {
                                var functionValue = await options.DefaultFunctionCallback(functionName, functionArguments, cancellationToken);
                                if (functionValue is string stringValue)
                                {
                                    var message4 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = toolCallId, Name = functionName, Value = stringValue });
                                    await options.AddMessageCallback(message4);
                                }
                                else
                                {
                                    var message4 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = toolCallId, Name = functionName, Value = JsonSerializer.Serialize(functionValue, JsonOptions) });
                                    await options.AddMessageCallback(message4);
                                }
                            }
                        }
                    }
                    else
                    {
                        var message5 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = toolCallId, Name = functionName, Value = $"Function '{functionName}' was not found." });
                        await options.AddMessageCallback(message5);
                    }
                }
            }

            return await CompleteAsync(chat, apiKey, options, usageTracker, httpClient, recursion + 1, cancellationToken);
        }

        var messageContent = generatedMessage.GetProperty("content").GetString()!;
        var message6 = await chat.FromChatbotAsync(messageContent);

        await options.AddMessageCallback(message6);
        return messageContent;
    }

    internal static async IAsyncEnumerable<string> StreamCompletionAsync<TChat, TMessage, TFunctionCall, TFunctionResult>(TChat chat, string? apiKey, ChatCompletionOptions<TMessage, TFunctionCall, TFunctionResult>? options = null, TokenUsageTracker? usageTracker = null, HttpClient? httpClient = null, int recursion = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TChat : IChat<TMessage, TFunctionCall, TFunctionResult>
        where TMessage : IChatMessage<TFunctionCall, TFunctionResult>, new()
        where TFunctionCall : IFunctionCall, new()
        where TFunctionResult : IFunctionResult, new()
    {
        if (recursion >= 5)
        {
            throw new InvalidOperationException("Recursion limit reached (infinite loop detected).");
        }

        options ??= new();
        httpClient ??= new();

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            apiKey = options.ApiKey;
        }

        var request = CreateChatCompletionRequest(chat, options);
        request.Add("stream", true);

        if (usageTracker != null)
        {
            request.Add("stream_options", new JsonObject
            {
                { "include_usage", true }
            });
        }

        if (options.IsDebugMode)
        {
            Debug.WriteLine(request.ToString());
        }

        using var response = await httpClient.RepeatPostAsJsonForStreamAsync("https://api.openai.com/v1/chat/completions", request, apiKey, options.MaxAttempts, cancellationToken);
        using var responseContent = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseReader = new StreamReader(responseContent);

        var functionCalls = new List<TFunctionCall>();
        var currentToolCallId = string.Empty;
        var currentFunctionName = string.Empty;
        var currentFunctionArguments = string.Empty;
        var entireContent = string.Empty;

        while (!responseReader.EndOfStream)
        {
            var chunk = await responseReader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            var chunkParts = chunk.Split("data: ");
            if (chunkParts.Length == 1)
            {
                continue;
            }

            var chunkData = chunkParts[1];
            if (chunkData == "[DONE]")
            {
                break;
            }

            using var chunkDocument = JsonDocument.Parse(chunkData);
            if (options.IsDebugMode)
            {
                Debug.WriteLine(chunkDocument.RootElement.ToString());
            }

            if (usageTracker != null)
            {
                var usage = chunkDocument.RootElement.GetProperty("usage");
                if (usage.ValueKind != JsonValueKind.Null)
                {
                    var promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
                    var cachedTokens = usage.GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32();
                    var completionTokens = usage.GetProperty("completion_tokens").GetInt32();

                    usageTracker.AddPromptTokens(promptTokens);
                    usageTracker.AddCachedTokens(cachedTokens);
                    usageTracker.AddCompletionTokens(completionTokens);
                }
            }

            var choices = chunkDocument.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            var delta = choice.GetProperty("delta");

            if (delta.TryGetProperty("content", out var contentProperty) && contentProperty.ValueKind != JsonValueKind.Null)
            {
                var content = contentProperty.GetString()!;
                entireContent += content;

                yield return content;
            }

            if (delta.TryGetProperty("tool_calls", out var toolCallsProperty))
            {
                var toolCallProperty = toolCallsProperty[0];
                if (toolCallProperty.TryGetProperty("function", out var functionProperty))
                {
                    if (functionProperty.TryGetProperty("name", out var functionNameProperty))
                    {
                        if (!string.IsNullOrWhiteSpace(currentFunctionName))
                        {
                            functionCalls.Add(new TFunctionCall { ToolCallId = currentToolCallId, Name = currentFunctionName, Arguments = currentFunctionArguments });
                        }

                        currentToolCallId = toolCallProperty.GetProperty("id").GetString()!;
                        currentFunctionName = functionNameProperty.GetString()!;
                        currentFunctionArguments = string.Empty;
                    }

                    if (functionProperty.TryGetProperty("arguments", out var functionArgumentsProperty))
                    {
                        currentFunctionArguments += functionArgumentsProperty.GetString()!;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(currentFunctionName))
        {
            functionCalls.Add(new TFunctionCall { ToolCallId = currentToolCallId, Name = currentFunctionName, Arguments = currentFunctionArguments });
        }

        if (functionCalls.Count > 0)
        {
            var message1 = await chat.FromChatbotAsync(functionCalls);
            await options.AddMessageCallback(message1);
        }

        foreach (var functionCall in functionCalls)
        {
            var function = options.Functions.FirstOrDefault(f => f.Name.NormalizedEquals(functionCall.Name));
            if (function != null)
            {
                if (function.RequiresDoubleCheck && chat.Messages.Count(m => m.FunctionCalls.Any(c => c.Name == functionCall.Name)) % 2 != 0)
                {
                    var message2 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = functionCall.ToolCallId, Name = functionCall.Name, Value = "Before executing, are you sure the user wants to run this function? If yes, call it again to confirm." });
                    await options.AddMessageCallback(message2);
                }
                else
                {
                    if (function.Callback != null)
                    {
                        var functionValue = await FunctionInvoker.InvokeAsync(function.Callback, functionCall.Arguments, options.ExecutionContext, cancellationToken);
                        var message3 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = functionCall.ToolCallId, Name = functionCall.Name, Value = functionValue });

                        await options.AddMessageCallback(message3);
                    }
                    else
                    {
                        var functionValue = await options.DefaultFunctionCallback(functionCall.Name, functionCall.Arguments, cancellationToken);
                        if (functionValue is string stringValue)
                        {
                            var message4 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = functionCall.ToolCallId!, Name = functionCall.Name, Value = stringValue });
                            await options.AddMessageCallback(message4);
                        }
                        else
                        {
                            var message4 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = functionCall.ToolCallId!, Name = functionCall.Name, Value = JsonSerializer.Serialize(functionValue, JsonOptions) });
                            await options.AddMessageCallback(message4);
                        }
                    }
                }
            }
            else
            {
                var message5 = await chat.FromFunctionAsync(new TFunctionResult { ToolCallId = functionCall.ToolCallId, Name = functionCall.Name, Value = $"Function '{functionCall.Name}' was not found." });
                await options.AddMessageCallback(message5);
            }
        }

        if (!string.IsNullOrWhiteSpace(entireContent))
        {
            var message6 = await chat.FromChatbotAsync(entireContent);
            await options.AddMessageCallback(message6);
        }

        if (functionCalls.Count > 0)
        {
            await foreach (var chunk in StreamCompletionAsync(chat, apiKey, options, usageTracker, httpClient, recursion + 1, cancellationToken))
            {
                yield return chunk;
            }
        }
    }

    private static JsonObject CreateChatCompletionRequest<TChat, TMessage, TFunctionCall, TFunctionResult>(TChat chat, ChatCompletionOptions<TMessage, TFunctionCall, TFunctionResult> options)
        where TChat : IChat<TMessage, TFunctionCall, TFunctionResult>
        where TMessage : IChatMessage<TFunctionCall, TFunctionResult>, new()
        where TFunctionCall : IFunctionCall
        where TFunctionResult : IFunctionResult
    {
        var messages = chat.Messages.ToList();

        if (options.SystemMessageCallback != null)
        {
            MessageTools.AddDynamicSystemMessage<TMessage, TFunctionCall, TFunctionResult>(messages, options.SystemMessageCallback());
        }

        if (options.IsTimeAware)
        {
            MessageTools.AddTimeInformation<TMessage, TFunctionCall, TFunctionResult>(messages, options.TimeCallback());
        }

        MessageTools.RemoveDeletedMessages<TMessage, TFunctionCall, TFunctionResult>(messages);

        if (options.IsIgnoringPreviousFunctionCalls)
        {
            MessageTools.RemovePreviousFunctionCalls<TMessage, TFunctionCall, TFunctionResult>(messages);
        }

        MessageTools.LimitTokens<TMessage, TFunctionCall, TFunctionResult>(messages, options.MessageLimit, options.CharacterLimit);

        var messagesArray = new JsonArray();
        foreach (var message in messages)
        {
            var messageObject = new JsonObject
            {
                { "role", GetRoleName(message.Role) }
            };

            if (message.UserName != null)
            {
                messageObject.Add("name", message.UserName);
            }

            if (message.Content != null)
            {
                messageObject.Add("content", message.Content);
            }

            var toolCallsArray = new JsonArray();
            foreach (var functionCall in message.FunctionCalls)
            {
                var functionObject = new JsonObject
                {
                    { "name", functionCall.Name },
                    { "arguments", functionCall.Arguments }
                };

                var toolCallObject = new JsonObject
                {
                    { "id", functionCall.ToolCallId },
                    { "type", "function" },
                    { "function", functionObject }
                };

                toolCallsArray.Add(toolCallObject);
            }

            if (toolCallsArray.Count > 0)
            {
                messageObject.Add("tool_calls", toolCallsArray);
            }

            if (message.FunctionResult != null && !string.IsNullOrWhiteSpace(message.FunctionResult.Name))
            {
                messageObject.Add("tool_call_id", message.FunctionResult.ToolCallId);
                messageObject.Add("content", message.FunctionResult.Value);
            }

            messagesArray.Add(messageObject);
        }

        var requestObject = new JsonObject
        {
            { "model", options.Model },
            { "messages", messagesArray }
        };

        if (chat.UserTrackingId != null)
        {
            requestObject.Add("user", chat.UserTrackingId);
        }
        else if (options.UserTrackingId != null)
        {
            requestObject.Add("user", options.UserTrackingId);
        }

        if (options.MaxOutputTokens.HasValue)
        {
            requestObject.Add("max_completion_tokens", options.MaxOutputTokens.Value);
        }

        if (options.Seed.HasValue)
        {
            requestObject.Add("seed", options.Seed.Value);
        }

        if (options.Temperature.HasValue)
        {
            requestObject.Add("temperature", options.Temperature.Value);
        }

        if (options.TopP.HasValue)
        {
            requestObject.Add("top_p", options.TopP.Value);
        }

        if (options.FrequencyPenalty.HasValue)
        {
            requestObject.Add("frequency_penalty", options.FrequencyPenalty.Value);
        }

        if (options.PresencePenalty.HasValue)
        {
            requestObject.Add("presence_penalty", options.PresencePenalty.Value);
        }

        if (options.ResponseType != null)
        {
            requestObject.Add("response_format", SchemaSerializer.SerializeResponseFormat(options.ResponseType));
        }
        else if (options.IsJsonMode)
        {
            var responseFormatObject = new JsonObject
            {
               { "type", "json_object" }
            };

            requestObject.Add("response_format", responseFormatObject);
        }

        if (options.Functions.Count > 0 && (!options.IsParallelFunctionCallingOn || options.IsStrictFunctionCallingOn))
        {
            requestObject.Add("parallel_tool_calls", false);
        }

        if (options.IsStoringOutputs)
        {
            requestObject.Add("store", true);
        }

        if (options.StopWords != null && options.StopWords.Count > 0)
        {
            var stopArray = new JsonArray();
            foreach (var stop in options.StopWords)
            {
                stopArray.Add(stop);
            }

            requestObject.Add("stop", stopArray);
        }

        if (options.Functions.Count > 0)
        {
            var toolsArray = new JsonArray();
            foreach (var function in options.Functions)
            {
                var normalizedName = function.Name.ToSnakeLower();
                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    continue;
                }

                var functionObject = SchemaSerializer.SerializeFunction(function, options.IsStrictFunctionCallingOn);
                var toolObject = new JsonObject
                {
                    { "type", "function" },
                    { "function", functionObject }
                };

                toolsArray.Add(toolObject);
            }

            if (toolsArray.Count > 0)
            {
                requestObject.Add("tools", toolsArray);
            }
        }

        return requestObject;
    }

    private static string GetRoleName(ChatRole role)
    {
        return role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Chatbot => "assistant",
            ChatRole.Function => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Invalid role")
        };
    }
}
