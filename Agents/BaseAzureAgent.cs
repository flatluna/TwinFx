using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
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
        var endpoint = GetConfigurationValue(configuration, "PROJECT_CONNECTION_STRING");
        this.Client = AzureAIAgent.CreateAgentsClient(endpoint, new DefaultAzureCredential());
    }

    protected PersistentAgentsClient Client { get; }

    protected AIProjectClient CreateFoundryProjectClient()
    {
        var configuration = LoadConfiguration();
        var endpoint = GetConfigurationValue(configuration, "PROJECT_CONNECTION_STRING");
        return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    protected async Task<string> GetConnectionId(string connectionName)
    {
        AIProjectClient client = CreateFoundryProjectClient();
        Connections connectionClient = client.GetConnectionsClient();
        var configuration = LoadConfiguration();
        var endpoint = GetConfigurationValue(configuration, "PROJECT_CONNECTION_STRING");
        
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
