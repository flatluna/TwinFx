// Copyright (c) Microsoft. All rights reserved.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace TwinFx.Clients;

/// <summary>
/// Base class for TwinFx agent tests that demonstrate ChatCompletionAgent usage
/// </summary>
public abstract class BaseTwinAgentTest<TClient>
{
    protected BaseTwinAgentTest(ILogger logger)
    {
        Output = logger;
    }

    protected ILogger Output { get; }

    protected void WriteAgentChatMessage(ChatMessageContent message)
    {
        var role = message.Role == AuthorRole.Assistant ? "?? Agent" : 
                   message.Role == AuthorRole.Tool ? "?? Tool" : "?? User";
        var content = message.Content ?? string.Empty;
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        var author = message.AuthorName ?? "*";
#pragma warning restore SKEXP0001
        
        Output.LogInformation($"{role} ({author}): {content}");
        Console.WriteLine($"{role} ({author}): {content}");
        
        // Log any additional metadata
        if (message.Metadata?.Count > 0)
        {
            Output.LogDebug($"?? Message metadata: {string.Join(", ", message.Metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
        }
    }

    protected void AddChatClientToKernel(IKernelBuilder builder)
    {
        // Add configuration-based chat client
        var configuration = LoadConfiguration();
        
        var endpoint = GetConfigurationValue(configuration, "AzureOpenAI:Endpoint");
        var apiKey = GetConfigurationValue(configuration, "AzureOpenAI:ApiKey");
        var deploymentName = GetConfigurationValue(configuration, "AzureOpenAI:DeploymentName", "gpt4mini");

        builder.AddAzureOpenAIChatCompletion(
            deploymentName: deploymentName,
            endpoint: endpoint,
            apiKey: apiKey);
            
        Output.LogInformation($"?? Added Azure OpenAI Chat Client: {deploymentName}");
        Output.LogInformation($"?? Endpoint: {endpoint}");
    }

    protected void AddChatCompletionToKernel(IKernelBuilder builder)
    {
        // Add configuration-based chat completion
        var configuration = LoadConfiguration();
        
        var endpoint = GetConfigurationValue(configuration, "AzureOpenAI:Endpoint");
        var apiKey = GetConfigurationValue(configuration, "AzureOpenAI:ApiKey");
        var deploymentName = GetConfigurationValue(configuration, "AzureOpenAI:DeploymentName", "gpt4mini");

        builder.AddAzureOpenAIChatCompletion(
            deploymentName: deploymentName,
            endpoint: endpoint,
            apiKey: apiKey);
            
        Output.LogInformation($"?? Added Azure OpenAI Chat Completion: {deploymentName}");
        Output.LogInformation($"?? Endpoint: {endpoint}");
    }

    protected IConfiguration LoadConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        var config = builder.Build();
        
        Output.LogInformation("?? Loading configuration from local.settings.json");
        
        var endpoint = GetConfigurationValue(config, "AzureOpenAI:Endpoint", null, false);
        if (!string.IsNullOrEmpty(endpoint))
        {
            Output.LogInformation($"? Found Azure OpenAI configuration in local.settings.json");
            Output.LogInformation($"?? Endpoint: {endpoint}");
            
            var deploymentName = GetConfigurationValue(config, "AzureOpenAI:DeploymentName", null, false);
            if (!string.IsNullOrEmpty(deploymentName))
            {
                Output.LogInformation($"?? Deployment: {deploymentName}");
            }
        }
        else
        {
            Output.LogWarning("?? Azure OpenAI configuration not found in local.settings.json");
        }

        return config;
    }

    protected string GetConfigurationValue(IConfiguration configuration, string key, string? defaultValue = null, bool throwIfMissing = true)
    {
        // Try to get the value directly from the configuration
        string? value = configuration.GetValue<string>(key);
        
        // If not found, try to get it from the Values section (Azure Functions local.settings.json structure)
        if (string.IsNullOrEmpty(value))
        {
            value = configuration.GetValue<string>($"Values:{key}");
        }
        
        // If still not found, try environment variables with different formats
        if (string.IsNullOrEmpty(value))
        {
            var envKey = key.Replace(":", "_").ToUpperInvariant();
            value = Environment.GetEnvironmentVariable(envKey);
        }
        
        // If still not found and we have a default, use it
        if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(defaultValue))
        {
            Output.LogWarning($"?? Configuration key '{key}' not found, using default: {defaultValue}");
            return defaultValue;
        }
        
        // If still not found and no default, throw or return null based on throwIfMissing
        if (string.IsNullOrEmpty(value))
        {
            if (throwIfMissing)
            {
                var errorMessage = $"Configuration key '{key}' not found in local.settings.json or environment variables";
                Output.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
            return null!;
        }
        
        // Log successful retrieval (but mask sensitive values)
        var displayValue = key.Contains("ApiKey") || key.Contains("Key") || key.Contains("CONNECTION_STRING") ? "***MASKED***" : value;
        Output.LogInformation($"?? Configuration '{key}': {displayValue}");
        
        return value;
    }
}