# How to Configure Reasoning Effort in Azure OpenAI

This document explains how to properly configure the `reasoning_effort` parameter for Azure OpenAI models, particularly o1 models that support enhanced reasoning capabilities.

## ? What Doesn't Work

**Don't try to set reasoning_effort in AddAzureOpenAIChatCompletion:**

```csharp
// ? This doesn't work - reasoning_effort is not a parameter for AddAzureOpenAIChatCompletion
builder.AddAzureOpenAIChatCompletion(
    deploymentName: deploymentName,
    reasoning_effort: "medium", // ? Invalid parameter
    endpoint: endpoint,
    apiKey: apiKey);
```

## ? Correct Approaches

### 1. Using ChatClient with ChatCompletionOptions (Recommended)

```csharp
// ? Create ChatClient and configure options when making the call
var chatClient = azureClient.GetChatClient("o1-mini");

var options = new ChatCompletionOptions();

// Set reasoning effort using reflection (since it's not yet in public API)
try
{
    var reasoningEffortProperty = options.GetType().GetProperty("ReasoningEffort");
    if (reasoningEffortProperty != null)
    {
        reasoningEffortProperty.SetValue(options, "medium");
    }
    else
    {
        // Alternative: Set it in SerializedAdditionalRawData
        var additionalData = (Dictionary<string, BinaryData>)options.GetType()
            .GetProperty("SerializedAdditionalRawData", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(options)!;
        additionalData["reasoning_effort"] = BinaryData.FromString("\"medium\"");
    }
}
catch (Exception ex)
{
    _logger.LogWarning("Could not set reasoning effort: {Error}", ex.Message);
}

// Create messages
var messages = new List<OpenAI.Chat.ChatMessage>
{
    new UserChatMessage("Your prompt here")
};

// Make the call with reasoning effort
ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);
```

### 2. Using Semantic Kernel with PromptExecutionSettings

```csharp
// ? Configure reasoning effort in execution settings
var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
var history = new ChatHistory();
history.AddUserMessage("Your prompt here");

var executionSettings = new PromptExecutionSettings
{
    ExtensionData = new Dictionary<string, object>
    {
        { "reasoning_effort", "medium" },
        { "max_tokens", 4000 },
        { "temperature", 0.2 }
    }
};

var response = await chatCompletion.GetChatMessageContentAsync(
    history,
    executionSettings,
    kernel);
```

## ?? Reasoning Effort Values

The `reasoning_effort` parameter accepts these values:

- `"low"` - Faster responses, less reasoning
- `"medium"` - Balanced reasoning and speed
- `"high"` - Maximum reasoning, slower responses

## ?? Complete Working Example

Here's the complete pattern used in DocumentsNoStructuredAgent:

```csharp
public class DocumentsNoStructuredAgent
{
    private readonly AzureOpenAIClient _azureClient;
    private readonly ChatClient _chatClient;

    public DocumentsNoStructuredAgent(IConfiguration configuration, string model)
    {
        // Initialize with DefaultAzureCredential
        var endpoint = GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? 
                      configuration["AzureOpenAI:Endpoint"];
        var credential = new DefaultAzureCredential();
        
        _azureClient = new AzureOpenAIClient(new Uri(endpoint), credential);
        _chatClient = _azureClient.GetChatClient(model);
    }

    private async Task<string> ProcessWithAI(string prompt)
    {
        // Create messages
        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new UserChatMessage(prompt)
        };

        // Configure options with reasoning effort
        var options = new ChatCompletionOptions();
        options.MaxOutputTokenCount = 131072;
        
        // Set reasoning effort for o1 models
        try
        {
            var additionalData = new Dictionary<string, BinaryData>();
            options.GetType()
                .GetProperty("SerializedAdditionalRawData", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(options, additionalData);
            additionalData["reasoning_effort"] = BinaryData.FromString("\"medium\"");
        }
        catch (Exception ex)
        {
            // Continue without reasoning effort if not supported
        }

        // Make the call
        ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options);
        return completion.Content[0].Text;
    }
}
```

## ?? Model Compatibility

- **o1-preview, o1-mini**: Support reasoning_effort parameter
- **GPT-4, GPT-3.5**: Ignore reasoning_effort parameter (no error)
- **Other models**: May ignore or error depending on implementation

## ??? Troubleshooting

1. **Parameter not recognized**: Some model versions don't support reasoning_effort yet
2. **Reflection errors**: The property structure may change between SDK versions
3. **Authentication issues**: Ensure DefaultAzureCredential has proper permissions

## ?? References

- [OpenAI o1 Models Documentation](https://platform.openai.com/docs/guides/reasoning)
- [Azure OpenAI Service Documentation](https://docs.microsoft.com/azure/ai-services/openai/)
- [Microsoft.Extensions.AI Documentation](https://learn.microsoft.com/dotnet/ai/)

# How to Configure Azure OpenAI Authentication in TwinFx

This document explains the proper way to configure Azure OpenAI authentication, particularly when `DefaultAzureCredential` fails.

## ?? Common Authentication Issues

You might encounter errors like:
```
DefaultAzureCredential failed to retrieve a token from the included credentials.
- EnvironmentCredential authentication unavailable
- ManagedIdentityCredential authentication unavailable  
- VisualStudioCredential authentication failed
- AzureCliCredential authentication failed
```

## ? Solution: Use API Key Authentication

Instead of relying on `DefaultAzureCredential`, use **API Key authentication** which is more reliable for development environments.

### Before (? Problematic):
```csharp
// This approach fails when credentials are not properly configured
var credential = new DefaultAzureCredential();
var azureClient = new AzureOpenAIClient(new Uri(endpoint), credential);
```

### After (? Working):
```csharp
// Use API Key authentication - more reliable
var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ?? 
            configuration["AzureOpenAI:ApiKey"] ?? 
            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");

var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
```

## ?? Configuration Setup

### 1. local.settings.json (Azure Functions)
```json
{
  "Values": {
    "AzureOpenAI:Endpoint": "https://your-openai-resource.openai.azure.com/",
    "AzureOpenAI:ApiKey": "your-api-key-here",
    "AzureOpenAI:DeploymentName": "gpt-5-mini"
  }
}
```

### 2. appsettings.json (Regular Applications)
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-openai-resource.openai.azure.com/",
    "ApiKey": "your-api-key-here", 
    "DeploymentName": "gpt-5-mini"
  }
}
```

## ?? Implementation Pattern

### Complete Constructor Implementation:
```csharp
public DocumentsNoStructuredAgent(ILogger<DocumentsNoStructuredAgent> logger, 
    IConfiguration configuration, string Model)
{
    _logger = logger;
    _configuration = configuration;

    try
    {
        // Get Azure OpenAI configuration with fallback
        var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ?? 
                      configuration["AzureOpenAI:Endpoint"] ?? 
                      throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                      
        var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ?? 
                    configuration["AzureOpenAI:ApiKey"] ?? 
                    throw new InvalidOperationException("AzureOpenAI:ApiKey is required");

        var deploymentName = Model ?? configuration["Values:AzureOpenAI:DeploymentName"] ?? 
                            configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4";

        // Initialize Azure OpenAI clients using API Key authentication
        _azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _chatClient = _azureClient.GetChatClient(deploymentName);

        // Initialize Semantic Kernel (for backward compatibility)
        var builder = Kernel.CreateBuilder();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };

        builder.AddAzureOpenAIChatCompletion(
            deploymentName: deploymentName,
            endpoint: endpoint,
            apiKey: apiKey,
            httpClient: httpClient);

        _kernel = builder.Build();
        
        _logger.LogInformation("? Azure OpenAI clients initialized with API Key authentication");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "? Failed to initialize DocumentsNoStructuredAgent");
        throw;
    }
}
```

### Required Using Statements:
```csharp
using Azure.AI.OpenAI;
using Azure; // For AzureKeyCredential
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
```

## ?? Reasoning Effort Configuration

With the new authentication method, you can still configure reasoning effort:

```csharp
// Create messages
var messages = new List<OpenAI.Chat.ChatMessage>
{
    new UserChatMessage(prompt)
};

// Configure options with reasoning effort
var options = new ChatCompletionOptions();
options.MaxOutputTokenCount = 131072;

// Set reasoning effort for o1 models
try
{
    var additionalData = new Dictionary<string, BinaryData>();
    options.GetType()
        .GetProperty("SerializedAdditionalRawData", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(options, additionalData);
    additionalData["reasoning_effort"] = BinaryData.FromString("\"medium\"");
}
catch (Exception ex)
{
    _logger.LogWarning("Could not set reasoning effort: {Error}", ex.Message);
}

// Make the call
ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options);
```

## ??? Troubleshooting

### Issue: "AzureOpenAI:ApiKey is required"
**Solution:** Ensure your configuration file contains the API key:
- Check `local.settings.json` for Azure Functions
- Check `appsettings.json` for other applications
- Verify the key path: `Values:AzureOpenAI:ApiKey` or `AzureOpenAI:ApiKey`

### Issue: "Invalid API key format"
**Solution:** 
- Get the key from Azure Portal ? Your OpenAI Resource ? Keys and Endpoint
- Copy Key 1 or Key 2 completely
- Ensure no extra spaces or characters

### Issue: Model deployment not found
**Solution:**
- Verify the deployment name in Azure OpenAI Studio
- Ensure the model is deployed and accessible
- Check deployment name spelling

## ?? Migration Checklist

When migrating from `DefaultAzureCredential` to API Key:

- [ ] Add `using Azure;` directive
- [ ] Replace `DefaultAzureCredential` with `AzureKeyCredential`
- [ ] Add API key to configuration files
- [ ] Update constructor to read API key from configuration
- [ ] Test authentication with new approach
- [ ] Update any dependent services using the same pattern

## ?? Benefits of API Key Authentication

1. **Reliability**: Works in all environments without complex credential setup
2. **Simplicity**: No need to configure multiple authentication providers
3. **Debugging**: Clear error messages when keys are missing/invalid
4. **Consistency**: Same pattern across development, staging, and production
5. **Security**: Keys can be managed through Azure Key Vault if needed

This approach ensures your Azure OpenAI integration works reliably across different environments and development setups.