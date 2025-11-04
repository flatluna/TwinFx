using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.AzureAI;
using System.Diagnostics;
using TwinFx.Clients;

/// <summary>
/// Base class for samples that demonstrate the usage of <see cref="AzureAIAgent"/>.
/// </summary>
public abstract class BaseAzureAgent : BaseTwinAgentTest<PersistentAgentsClient>
{
    protected BaseAzureAgent(ILogger logger) : base(logger)
    {
        var configuration = LoadConfiguration();
        
        // Try to get PROJECT_CONNECTION_STRING first, then fallback to Azure OpenAI settings
        string? endpoint = null;
        try
        {
            endpoint = GetConfigurationValue(configuration, "PROJECT_CONNECTION_STRING", null, false);
        }
        catch
        {
            // PROJECT_CONNECTION_STRING not found, try Azure OpenAI endpoint
            endpoint = configuration["Values:AzureOpenAI:Endpoint"] ?? 
                      configuration["AzureOpenAI:Endpoint"] ?? 
                      configuration["AZURE_OPENAI_ENDPOINT"];
        }

        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException("Either PROJECT_CONNECTION_STRING or AzureOpenAI:Endpoint must be configured");
        }

        // Try to create client with DefaultAzureCredential first, then fallback to simulated connection
        try
        {
            Output.LogInformation("🔐 Attempting DefaultAzureCredential authentication...");
            this.Client = AzureAIAgent.CreateAgentsClient(endpoint, new DefaultAzureCredential());
            Output.LogInformation("✅ DefaultAzureCredential authentication successful");
        }
        catch (Exception ex)
        {
            Output.LogWarning(ex, "⚠️ DefaultAzureCredential failed: {Message}", ex.Message);
            Output.LogWarning("📝 Note: Azure AI Agents requires proper Azure AI Project setup with PROJECT_CONNECTION_STRING");
            Output.LogWarning("🔧 For development, ensure you're logged in with 'az login' or use proper Azure credentials");
            
            // For now, we'll rethrow the exception with more helpful information
            throw new InvalidOperationException(
                "Azure AI Agents authentication failed. This service requires:\n" +
                "1. A valid PROJECT_CONNECTION_STRING for Azure AI Project, OR\n" +
                "2. Proper Azure authentication (run 'az login' or configure DefaultAzureCredential)\n" +
                $"Original error: {ex.Message}");
        }
    }

    protected PersistentAgentsClient Client { get; }

    protected AIProjectClient CreateFoundryProjectClient()
    {
        var configuration = LoadConfiguration();
        
        // Try PROJECT_CONNECTION_STRING first, then fallback
        string? endpoint = null;
        try
        {
            endpoint = GetConfigurationValue(configuration, "PROJECT_CONNECTION_STRING", null, false);
        }
        catch
        {
            endpoint = configuration["Values:AzureOpenAI:Endpoint"] ?? 
                      configuration["AzureOpenAI:Endpoint"] ?? 
                      configuration["AZURE_OPENAI_ENDPOINT"];
        }

        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException("Either PROJECT_CONNECTION_STRING or AzureOpenAI:Endpoint must be configured");
        }

        // Try DefaultAzureCredential
        try
        {
            return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
        }
        catch (Exception ex)
        {
            Output.LogWarning(ex, "⚠️ DefaultAzureCredential failed for AIProjectClient");
            throw new InvalidOperationException(
                "Azure AI Project client authentication failed. This requires:\n" +
                "1. Valid PROJECT_CONNECTION_STRING, OR\n" +
                "2. Proper Azure authentication (run 'az login')\n" +
                $"Original error: {ex.Message}");
        }
    }

    protected async Task<string> GetConnectionId(string connectionName)
    {
        AIProjectClient client = CreateFoundryProjectClient();
        Connections connectionClient = client.GetConnectionsClient();
        var configuration = LoadConfiguration();
        var endpoint = GetConfigurationValue(configuration, "PROJECT_CONNECTION_STRING", null, false);
        
        Connection? connection = null;
        await foreach (var conn in connectionClient.GetConnectionsAsync())
        {
            if (conn.Name == connectionName)
            {
                connection = conn;
                break;
            }
        }
        
        return connection?.Id ?? throw new InvalidOperationException($"Connection '{connectionName}' not found in project '{endpoint}'.");
    }

    protected async Task DownloadContentAsync(ChatMessageContent message)
    {
        foreach (KernelContent item in message.Items)
        {
            if (item is AnnotationContent annotation)
            {
                await this.DownloadFileAsync(annotation.ReferenceId!);
            }
        }
    }

    protected async Task DownloadFileAsync(string fileId, bool launchViewer = false)
    {
        PersistentAgentFileInfo fileInfo = this.Client.Files.GetFile(fileId);
        if (fileInfo.Purpose == PersistentAgentFilePurpose.AgentsOutput)
        {
            string filePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(fileInfo.Filename));
            if (launchViewer)
            {
                filePath = Path.ChangeExtension(filePath, ".png");
            }

            BinaryData content = await this.Client.Files.GetFileContentAsync(fileId);
            File.WriteAllBytes(filePath, content.ToArray());
            Console.WriteLine($"  File #{fileId} saved to: {filePath}");

            if (launchViewer)
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C start {filePath}"
                    });
            }
        }
    }
}
