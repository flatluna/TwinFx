using Agents;
using Azure.AI.Agents.Persistent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Agents;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinFx.Agents;
using Azure.AI.OpenAI;
using Azure;
using Azure.Identity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwinFx.Services
{
    public class StructuredDocumentAICoder
    {
        private readonly ILogger<StructuredDocumentAICoder> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private Kernel _kernel;
        
        // Azure AI Agents Client (for code interpreter functionality)
        private PersistentAgentsClient _aiAgentsClient;
        
        public StructuredDocumentAICoder(ILogger<StructuredDocumentAICoder> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _logger.LogInformation("📊 StructuredDocumentAICoder initialized with hybrid authentication approach");

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("✅ DocumentIntelligenceService initialized successfully");

                // Initialize Semantic Kernel for AI processing using API Key authentication
                var builder = Kernel.CreateBuilder();

                // Get Azure OpenAI configuration with fallback paths
                var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ?? 
                              configuration["AzureOpenAI:Endpoint"] ?? 
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                              
                var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ?? 
                            configuration["AzureOpenAI:ApiKey"] ?? 
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                                                      
                var deploymentName = configuration["Values:AzureOpenAI:DeploymentName"] ?? 
                                   configuration["AzureOpenAI:DeploymentName"] ?? 
                                   "gpt-4";

                deploymentName = "gpt4mini";

                // Use API Key authentication for Semantic Kernel (more reliable)
                builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
                _kernel = builder.Build();
                _logger.LogInformation("✅ Semantic Kernel initialized successfully with API Key authentication");

                // Try to initialize Azure AI Agents client with fallback handling
                try
                {
                    // First try to get PROJECT_CONNECTION_STRING
                    var projectConnectionString = configuration["Values:PROJECT_CONNECTION_STRING"] ?? 
                                                configuration["PROJECT_CONNECTION_STRING"];
                                                
                    if (!string.IsNullOrEmpty(projectConnectionString))
                    {
                        _logger.LogInformation("🔐 Attempting Azure AI Agents with PROJECT_CONNECTION_STRING...");
                        _aiAgentsClient = AzureAIAgent.CreateAgentsClient(projectConnectionString, new DefaultAzureCredential());
                        _logger.LogInformation("✅ Azure AI Agents client initialized with PROJECT_CONNECTION_STRING");
                    }
                    else
                    {
                        // Fallback: try with Azure OpenAI endpoint and DefaultAzureCredential
                        _logger.LogInformation("🔐 Attempting Azure AI Agents with DefaultAzureCredential...");
                        _aiAgentsClient = AzureAIAgent.CreateAgentsClient(endpoint, new DefaultAzureCredential());
                        _logger.LogInformation("✅ Azure AI Agents client initialized with DefaultAzureCredential");
                    }
                }
                catch (Exception aiAgentsEx)
                {
                    _logger.LogWarning(aiAgentsEx, "⚠️ Azure AI Agents initialization failed: {Message}", aiAgentsEx.Message);
                    _logger.LogWarning("🔄 StructuredDocumentAICoder will work without Azure AI Agents (fallback mode)");
                    _aiAgentsClient = null!; // Will use fallback analysis
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize StructuredDocumentAICoder");
                throw;
            }
        }

        private StructuredDocumentsCosmosDB CreateStructuredDocumentsCosmosService()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<StructuredDocumentsCosmosDB>();
            return new StructuredDocumentsCosmosDB(cosmosLogger, _configuration);
        }
        
        public async Task<CsvAnalysis> AnalyzeCSVFileUsingAzureAIAgentAsync(
            string FileId,
            string FileName, string PathFile, string ContainerName,
            string question)
        {
            string Path = PathFile + "/" + FileName;
            
            // Download file using DataLakeClient
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(ContainerName);
            var fileBytes = await dataLakeClient.DownloadFileAsync(Path);
            CsvAnalysis csvAnalysis = new CsvAnalysis();
            if (fileBytes == null)
            {
                csvAnalysis.AIResponse = "❌ File not found or could not be downloaded from DataLake";
                return csvAnalysis;

            }
            
            // Convert bytes to stream
            using var csvStream = new MemoryStream(fileBytes);
            
            var cosmosService = CreateStructuredDocumentsCosmosService();
         //   var csvFileData = await cosmosService.GetCSVFileDataAsync(Path, ContainerName);

            // Check if Azure AI Agents client is available
            if (_aiAgentsClient == null)
            {
                csvAnalysis.AIResponse = "❌ File not found or could not be downloaded from DataLake";
                return csvAnalysis;
            }

            // StringBuilder to collect all results
            var allResults = new StringBuilder();
            allResults.AppendLine("🔍 CSV Analysis Results:");
            allResults.AppendLine(new string('=', 50));

            try
            {
                csvStream.Position = 0;
                _logger.LogInformation("📤 Uploading CSV data to Azure AI Agents...");

                // Validate CSV size (Azure AI Agents has file size limits)
                var csvSize = csvStream.Length;
                _logger.LogInformation("📊 CSV file size: {SizeKB} KB", csvSize / 1024);

                if (csvSize > 100 * 1024 * 1024) // 100MB limit
                {
                    _logger.LogWarning("⚠️ CSV file is very large ({SizeKB} KB), this may cause issues", csvSize / 1024);
                }

                // Get CSV metadata for context
                string csvMetadataContext = "";
                string genericFileName = "data.csv";
                
                if (FileId == "" )
                {
                    PersistentAgentFileInfo fileInfo = await _aiAgentsClient.Files.UploadFileAsync(csvStream, PersistentAgentFilePurpose.Agents, genericFileName);
                    _logger.LogInformation("✅ CSV file uploaded successfully. File ID: {FileId}", fileInfo.Id);
                    FileId = fileInfo.Id;
                }
                
                // Get model deployment name from configuration
                var modelDeploymentName = _configuration["Values:AzureOpenAI:DeploymentName"] ?? 
                                        _configuration["AzureOpenAI:DeploymentName"] ?? 
                                        "gpt4mini";

                // For visualization tasks, try to use a more powerful model if available
                if (question.ToLower().Contains("histograma") || question.ToLower().Contains("histogram") ||
                    question.ToLower().Contains("gráfico") || question.ToLower().Contains("chart") ||
                    question.ToLower().Contains("diagram"))
                {
                    // Try to use gpt-4 for complex visualizations
                    var alternativeModel = _configuration["Values:AzureOpenAI:AlternativeDeploymentName"] ?? 
                                         _configuration["AzureOpenAI:AlternativeDeploymentName"];
                    if (!string.IsNullOrEmpty(alternativeModel) && alternativeModel != modelDeploymentName)
                    {
                        modelDeploymentName = alternativeModel;
                        _logger.LogInformation("🎨 Using alternative model for visualization: {ModelName}", modelDeploymentName);
                    }
                }

                _logger.LogInformation("🤖 Creating Azure AI Agent with model: {ModelName}", modelDeploymentName);
                
                // Create dynamic instructions with CSV metadata
                var dynamicInstructions = $$"""
                You are a professional data analyst with code interpreter capabilities specialized in CSV data analysis.
                
                CORE RULES:
                - NEVER ask questions - provide immediate analysis
                - ALWAYS create visualizations when requested (histograms, charts, plots)
                - Use pandas for data loading and matplotlib for visualizations
                - Handle encoding errors gracefully (try utf-8, latin-1, cp1252)
                - Complete analysis without stopping for user input
                - Focus on the specific data and columns available in this dataset
                
                VISUALIZATION REQUIREMENTS:
                - Create clear histograms and charts as requested
                - Use proper figure size: plt.figure(figsize=(12, 8))
                - Add titles, labels, and save as PNG
                - Handle Spanish text properly in plots
                - Use appropriate chart types based on data types
                
                DATA CONTEXT:{{csvMetadataContext}}
                
                SAMPLE CODE PATTERN:
                ```python
                import pandas as pd
                import matplotlib.pyplot as plt
                import numpy as np
                
                # Load CSV with error handling
                try:
                    df = pd.read_csv('/mnt/data/{{genericFileName}}')
                except:
                    df = pd.read_csv('/mnt/data/{{genericFileName}}', encoding='latin-1')
                
                # Display basic info about the dataset
                print(f"Dataset shape: {df.shape}")
                print(f"Columns: {list(df.columns)}")
                print(df.head())
                print(df.info())
                
                # Create visualization based on data types
                plt.figure(figsize=(12, 8))
                # For numeric data: histograms, scatter plots, box plots
                # For categorical data: bar charts, pie charts
                # For time series: line plots, trend analysis
                plt.title('Data Analysis Results')
                plt.xlabel('X Label')
                plt.ylabel('Y Label')
                plt.savefig('analysis_result.png', dpi=300, bbox_inches='tight')
                plt.show()
                ```
                
                ANALYSIS APPROACH:
                - Start by examining the data structure and types
                - Identify numeric, categorical, and date columns
                - Provide statistical summaries for numeric data
                - Show value counts for categorical data
                - Create appropriate visualizations based on the question
                - Handle missing values and data quality issues
                - Provide insights and conclusions based on findings
                
                FORBIDDEN: 
                - Do not ask "¿Quieres que...?" or similar questions
                - Do not make assumptions about data without verification
                - Do not stop analysis waiting for user input
                
                IMPORTANT: After completing your analysis with code interpreter, provide your final response in the following JSON format WITHOUT using ```json at the beginning:
                
                {
                  "Codigo": "All the Python code you executed during the analysis",
                  "RespuestaFinal": "Complete analysis response with insights, findings, and conclusions"
                }
                
                Execute comprehensive data analysis first, then format your final response as pure JSON without any markdown formatting.
                """;
                _logger.LogInformation("📏 Dynamic instructions length: {Length} characters", dynamicInstructions.Length);
                
                // Define the agent using configuration with improved instructions
                PersistentAgent definition = null;
                try
                {
                    _logger.LogInformation("🤖 Creating Azure AI Agent...");
                    _logger.LogInformation("   • Model: {ModelName}", modelDeploymentName);
                    _logger.LogInformation("   • Tools: Code Interpreter");
                    _logger.LogInformation("   • File ID: {FileId}", FileId);

                    definition = await _aiAgentsClient.Administration.CreateAgentAsync(
                        modelDeploymentName,
                        name: "CSV Data Analyzer",
                        instructions: dynamicInstructions,
                        tools: [new CodeInterpreterToolDefinition()],
                        toolResources:
                            new()
                            {
                                CodeInterpreter = new()
                                {
                                    FileIds = { FileId },
                                }
                            });

                    _logger.LogInformation("✅ Azure AI Agent definition created successfully");
                }
                catch (Exception agentCreationEx)
                {
                    _logger.LogError(agentCreationEx, "❌ Failed to create Azure AI Agent: {Message}", agentCreationEx.Message);
                    throw new InvalidOperationException($"Agent creation failed: {agentCreationEx.Message}", agentCreationEx);
                }
                AzureAIAgent agent = new(definition, _aiAgentsClient);
                AzureAIAgentThread thread = new(_aiAgentsClient);

                _logger.LogInformation("✅ Azure AI Agent created successfully. Agent ID: {AgentId}", agent.Id);

                // Respond to user input and collect results with timeout and retry logic
                try
                {
                    _logger.LogInformation("🔍 Starting CSV analysis with Code Interpreter...");

                    // Add timeout for the operation
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // 5 minute timeout

                    var result = await InvokeAgentWithTimeoutAsync(question, cts.Token, agent, thread, csvStream);
                    
                    // Extract final response from JSON if present
                    var finalResponse = ExtractFinalResponseFromJson(result);
                    
                    // Create elegant HTML response using AI
                    var elegantResponse = await CreateFinalResponse(question, finalResponse);
                    allResults.AppendLine(elegantResponse);
                    allResults.AppendLine();

                    _logger.LogInformation("✅ CSV analysis completed successfully!");

                    // Log and return the complete results
                    var finalResults = allResults.ToString();
                    _logger.LogInformation("📊 Complete Analysis Results:\n{Results}", finalResults);
                    csvAnalysis.AIResponse = finalResults;
                    csvAnalysis.Question = question;
                    csvAnalysis.FileID = FileId;
                    return csvAnalysis;
                }
                catch (OperationCanceledException)
                {
                    var timeoutMsg = "⏰ CSV analysis timed out after 5 minutes. The dataset might be too complex or large.";
                    csvAnalysis.AIResponse = "⏰ CSV analysis timed out after 5 minutes. The dataset might be too complex or large.";
                    return csvAnalysis;
                }
                finally
                {
                    _logger.LogInformation("🧹 Cleaning up Azure AI resources...");
                    try
                    {
                      //  await thread.DeleteAsync();
                     //   await _aiAgentsClient.Administration.DeleteAgentAsync(agent.Id);
                     //   await _aiAgentsClient.Files.DeleteFileAsync(fileInfo.Id);
                        _logger.LogInformation("✅ Cleanup completed");
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx, "⚠️ Error during cleanup: {Message}", cleanupEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                var errorResult = $"❌ Error during CSV analysis: {ex.Message}";
                allResults.AppendLine(errorResult);
                _logger.LogError(ex, "❌ Error during CSV analysis: {Message}", ex.Message);

                // Try fallback analysis
                try
                {
                    allResults.AppendLine("");
                    allResults.AppendLine("🔄 Attempting fallback analysis...");

                    // Reset stream position for fallback
                    csvStream.Position = 0;
                    var fallbackResult = await ProvideFallbackAnalysis(csvStream, question);
                    allResults.AppendLine(fallbackResult);
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "❌ Fallback analysis failed: {Message}", fallbackEx.Message);
                    allResults.AppendLine($"❌ Fallback analysis failed: {fallbackEx.Message}");
                }

                // Still return the results collected so far plus the error
                var finalResults = allResults.ToString();
                csvAnalysis.AIResponse = finalResults;
                csvAnalysis.Question = question;
                csvAnalysis.FileID = FileId;
                return csvAnalysis;
            }
        }


        private async Task<string> CreateFinalResponse(string Question, string AIResponse)
        {
            try
            {
                _logger.LogInformation("🎨 Creating elegant HTML response for CSV analysis");

                // Initialize Semantic Kernel if not already done
                await InitializeKernelAsync();

                // Create prompt for elegant HTML formatting
                var htmlFormattingPrompt = $@"Eres un especialista en presentación de datos que crea respuestas elegantes y profesionales.

PREGUNTA DEL USUARIO:
{Question}

RESPUESTA TÉCNICA DEL ANÁLISIS CSV:
{AIResponse}

INSTRUCCIONES:
1. Convierte la respuesta técnica en una presentación HTML elegante y profesional
2. Organiza la información de manera clara y visualmente atractiva
3. Usa colores, emojis y formato HTML apropiado
4. Destaca los hallazgos más importantes 
6. Incluye secciones organizadas con títulos claros
7. Usa tablas cuando sea apropiado para datos estructurados

FORMATO DE RESPUESTA:
- Responde ÚNICAMENTE con HTML elegante
- NO uses ```html al inicio o final
- Usa estilos CSS inline para colores y formato
- Incluye emojis relevantes (📊, 📈, 💰, 📋, etc.)
- Estructura con secciones claras: pregunta, resultados, conclusiones
- Destaca números importantes con colores y negritas
- Queremos una respuesta sencilla y directa de la pregunta no expliques de mas ni pongas datos del codigo enfocate en la respuesta final del aAgente AI

EJEMPLO DE ESTRUCTURA:
<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto; background: #f8f9fa; padding: 20px; border-radius: 12px;'>
  <h2 style='color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px;'>📊 Análisis de Datos CSV</h2>
  <div style='background: white; padding: 15px; border-radius: 8px; margin: 15px 0; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>
    <!-- Contenido organizado aquí -->
  </div>
</div>

Genera una respuesta HTML elegante y profesional basada en la información proporcionada.";

                var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(htmlFormattingPrompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_tokens"] = 4000,
                        ["temperature"] = 0.3
                    }
                };

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    _kernel);

                var htmlResponse = response.Content?.Trim() ?? "";
                
                // Clean up any potential markdown formatting
                if (htmlResponse.StartsWith("```html"))
                {
                    htmlResponse = htmlResponse.Substring(7).Trim();
                }
                if (htmlResponse.EndsWith("```"))
                {
                    htmlResponse = htmlResponse.Substring(0, htmlResponse.Length - 3).Trim();
                }

                _logger.LogInformation("✅ Elegant HTML response created successfully");
                return htmlResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating elegant HTML response");
                
                // Fallback to basic HTML formatting
                return CreateBasicHtmlResponse(Question, AIResponse);
            }
        }

        /// <summary>
        /// Initialize Semantic Kernel if not already done
        /// </summary>
        private async Task InitializeKernelAsync()
        {
            if (_kernel != null)
                return; // Already initialized

            try
            {
                _logger.LogInformation("🔧 Initializing Semantic Kernel for elegant response generation");

                // Kernel is already initialized in constructor
                // Just log that we're using it
                _logger.LogInformation("✅ Using existing Semantic Kernel instance");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error initializing Semantic Kernel for response generation");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Create basic HTML response as fallback
        /// </summary>
        private string CreateBasicHtmlResponse(string question, string aiResponse)
        {
            return $@"
<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto; background: #f8f9fa; padding: 20px; border-radius: 12px;'>
    <h2 style='color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px;'>📊 Análisis de Datos CSV</h2>
    
    <div style='background: white; padding: 15px; border-radius: 8px; margin: 15px 0; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>
        <h3 style='color: #34495e; margin: 0 0 10px 0;'>❓ Pregunta</h3>
        <p style='margin: 0; padding: 10px; background: #ecf0f1; border-radius: 5px; font-style: italic;'>{question}</p>
    </div>
    
    <div style='background: white; padding: 15px; border-radius: 8px; margin: 15px 0; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>
        <h3 style='color: #27ae60; margin: 0 0 15px 0;'>📋 Resultados del Análisis</h3>
        <div style='line-height: 1.6; color: #2c3e50;'>
            {aiResponse.Replace("\n", "<br>")}
        </div>
    </div>
    
    <div style='background: #d5edda; padding: 15px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #27ae60;'>
        <p style='margin: 0; color: #155724; font-size: 14px;'>
            ✅ <strong>Análisis completado</strong> - Los datos han sido procesados exitosamente
        </p>
    </div>
</div>";
        }

        // Improved InvokeAgentAsync with timeout support
        private async Task<string> InvokeAgentWithTimeoutAsync(string input, CancellationToken cancellationToken, 
            AzureAIAgent agent, AzureAIAgentThread thread, MemoryStream csvStream)
        {
            var responseBuilder = new StringBuilder();

            try
            {
                ChatMessageContent message = new(AuthorRole.User, input);
                WriteAgentChatMessage(message);

                // Add the user question to results
                responseBuilder.AppendLine($"❓ Question: {input}");
                responseBuilder.AppendLine("📋 Response:");

                var responseCount = 0;
                var maxResponses = 20; // Prevent infinite loops

                await foreach (ChatMessageContent response in agent.InvokeAsync(message, thread))
                {
                    // Check for cancellation
                    cancellationToken.ThrowIfCancellationRequested();

                    responseCount++;
                    if (responseCount > maxResponses)
                    {
                        _logger.LogWarning("🔄 Maximum response count reached, stopping conversation");
                        responseBuilder.AppendLine("⚠️ Analysis stopped: Maximum response count reached");
                        break;
                    }

                    WriteAgentChatMessage(response);
                    await DownloadContentAsync(response);

                    // Collect agent response content
                    if (!string.IsNullOrEmpty(response.Content))
                    {
                        var role = response.Role == AuthorRole.Assistant ? "🤖 Agent" :
                                   response.Role == AuthorRole.Tool ? "🔧 Tool" :
                                   response.Role == AuthorRole.User ? "👤 User" : "📝 System";
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                        var author = response.AuthorName ?? "*";
#pragma warning restore SKEXP0001

                        responseBuilder.AppendLine($"{role} ({author}): {response.Content}");
                    }

                    // Check for completion indicators
                    if (response.Content?.Contains("analysis complete", StringComparison.OrdinalIgnoreCase) == true ||
                        response.Content?.Contains("final result", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _logger.LogInformation("🎯 Analysis completion detected");
                        break;
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                var errorMsg = $"❌ Error processing question '{input}': {ex.Message}";
                _logger.LogError(ex, "❌ Error in InvokeAgentWithTimeoutAsync: {Message}", ex.Message);
                responseBuilder.AppendLine(errorMsg);

                // Try to provide a basic fallback analysis
                responseBuilder.AppendLine("");
                responseBuilder.AppendLine("🔄 Attempting fallback analysis...");

                try
                {
                    var fallbackResult = await ProvideFallbackAnalysis(csvStream, input);
                    responseBuilder.AppendLine(fallbackResult);
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "❌ Fallback analysis also failed: {Message}", fallbackEx.Message);
                    responseBuilder.AppendLine($"❌ Fallback analysis failed: {fallbackEx.Message}");

                    // Provide basic CSV info even if everything fails
                    try
                    {
                        csvStream.Position = 0;
                        using var reader = new StreamReader(csvStream);
                        var csvPreview = await reader.ReadToEndAsync();
                        var lines = csvPreview.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                        responseBuilder.AppendLine("");
                        responseBuilder.AppendLine("📊 **Basic CSV Information:**");
                        responseBuilder.AppendLine($"   • Total lines: {lines.Length}");
                        responseBuilder.AppendLine($"   • Estimated columns: {(lines.FirstOrDefault()?.Split(',').Length ?? 0)}");
                        responseBuilder.AppendLine($"   • Question: {input}");
                        responseBuilder.AppendLine($"   • ⚠️ **Azure AI Agent with Code Interpreter required for full analysis**");
                    }
                    catch (Exception basicEx)
                    {
                        _logger.LogError(basicEx, "❌ Even basic CSV info failed: {Message}", basicEx.Message);
                        responseBuilder.AppendLine($"❌ Complete analysis failure. Question: {input}");
                    }
                }
            }

            return responseBuilder.ToString();
        }

        // Helper methods for message writing and downloading content
        private void WriteAgentChatMessage(ChatMessageContent message)
        {
            var role = message.Role == AuthorRole.Assistant ? "🤖 Assistant" :
                       message.Role == AuthorRole.Tool ? "🔧 Tool" :
                       message.Role == AuthorRole.User ? "👤 User" : "📝 System";
                       
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var author = message.AuthorName ?? "*";
#pragma warning restore SKEXP0001
            
            _logger.LogInformation("{Role} ({Author}): {Content}", role, author, message.Content ?? "[No content]");
        }

        private async Task DownloadContentAsync(ChatMessageContent message)
        {
            foreach (KernelContent item in message.Items)
            {
                // Skip AnnotationContent processing as it's not available in current SDK version
                // This would be for downloading files generated by code interpreter
                if (_aiAgentsClient != null && item.GetType().Name.Contains("Annotation"))
                {
                    try
                    {
                        // Use reflection to get ReferenceId if available
                        var referenceIdProperty = item.GetType().GetProperty("ReferenceId");
                        if (referenceIdProperty != null)
                        {
                            var referenceId = referenceIdProperty.GetValue(item) as string;
                            if (!string.IsNullOrEmpty(referenceId))
                            {
                                await DownloadFileAsync(referenceId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to download file from annotation: {Message}", ex.Message);
                    }
                }
            }
        }

        private async Task DownloadFileAsync(string fileId, bool launchViewer = false)
        {
            if (_aiAgentsClient == null) return;
            
            try
            {
                PersistentAgentFileInfo fileInfo = _aiAgentsClient.Files.GetFile(fileId);
                if (fileInfo.Purpose == PersistentAgentFilePurpose.AgentsOutput)
                {
                    string filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetFileName(fileInfo.Filename));
                    if (launchViewer)
                    {
                        filePath = System.IO.Path.ChangeExtension(filePath, ".png");
                    }

                    BinaryData content = await _aiAgentsClient.Files.GetFileContentAsync(fileId);
                    File.WriteAllBytes(filePath, content.ToArray());
                    _logger.LogInformation("📁 File #{FileId} saved to: {FilePath}", fileId, filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error downloading file {FileId}: {Message}", fileId, ex.Message);
            }
        }

        /// <summary>
        /// Analyze a specific CSV file with custom questions
        /// </summary>
        /// <param name="csvFilePath">Path to the CSV file to analyze</param>
        /// <param name="questions">List of questions to ask about the CSV</param>
        /// <returns>Task representing the async operation</returns>
        public async Task AnalyzeCustomCSVAsync(string csvFilePath, params string[] questions)
        {
            if (!File.Exists(csvFilePath))
            {
                _logger.LogError("❌ CSV file not found: {FilePath}", csvFilePath);
                return;
            }

            if (_aiAgentsClient == null)
            {
                _logger.LogWarning("⚠️ Azure AI Agents not available for custom CSV analysis");
                return;
            }

            _logger.LogInformation("📊 Starting custom CSV analysis for file: {FilePath}", csvFilePath);

            // Upload the custom CSV file
            using var fileStream = File.OpenRead(csvFilePath);
            PersistentAgentFileInfo fileInfo = await _aiAgentsClient.Files.UploadFileAsync(fileStream, PersistentAgentFilePurpose.Agents, System.IO.Path.GetFileName(csvFilePath));

            // Get model deployment name from configuration
            var modelDeploymentName = _configuration["Values:AzureOpenAI:DeploymentName"] ?? 
                                    _configuration["AzureOpenAI:DeploymentName"] ?? 
                                    "gpt4mini";

            // Define the agent using configuration
            PersistentAgent definition = await _aiAgentsClient.Administration.CreateAgentAsync(
                modelDeploymentName,
                name: "Custom CSV Analyzer",
                instructions: """
            You are a professional data analyst specializing in CSV file analysis using Python code interpreter.
            
            When analyzing a CSV file:
            1. ALWAYS use the code interpreter tool to write and execute Python code
            2. Start by loading the CSV file from your workspace using pandas
            3. Examine the file structure: columns, data types, number of rows
            4. Provide thorough data analysis based on the user's questions
            5. Create visualizations when appropriate (matplotlib, seaborn)
            6. Provide specific numerical insights and findings
            7. Answer each question with concrete evidence from the data
            
            Python libraries available: pandas, numpy, matplotlib, seaborn, scipy, sklearn
            
            Always start your analysis by loading and inspecting the CSV file.
            Be thorough and provide detailed, data-driven responses in a clear format.
            """,
                tools: [new CodeInterpreterToolDefinition()]);

            AzureAIAgent agent = new(definition, _aiAgentsClient);
            AzureAIAgentThread thread = new(_aiAgentsClient);

            try
            {
                // Ask each question
                if (questions.Length == 0)
                {
                    // Default analysis if no questions provided
                    await InvokeAgentAsync("Please analyze this CSV file and provide a comprehensive overview including data structure, summary statistics, and key insights.");
                }
                else
                {
                    foreach (var question in questions)
                    {
                        await InvokeAgentAsync(question);
                    }
                }
            }
            finally
            {
                await thread.DeleteAsync();
                await _aiAgentsClient.Administration.DeleteAgentAsync(agent.Id);
                await _aiAgentsClient.Files.DeleteFileAsync(fileInfo.Id);
            }

            // Local function to invoke agent and display the conversation messages.
            async Task InvokeAgentAsync(string input)
            {
                ChatMessageContent message = new(AuthorRole.User, input);
                WriteAgentChatMessage(message);

                await foreach (ChatMessageContent response in agent.InvokeAsync(message, thread))
                {
                    WriteAgentChatMessage(response);
                    await DownloadContentAsync(response);
                }
            }
        }

        /// <summary>
        /// Analyze CSV data using inline content (bypasses file upload issues)
        /// </summary>
        /// <param name="csvContent">CSV content as string</param>
        /// <param name="fileName">Name for the data</param>
        /// <param name="question">Question to ask about the data</param>
        /// <returns>Task representing the async operation</returns>
        public async Task AnalyzeInlineCSVAsync(string csvContent, string fileName, string question)
        {
            if (_aiAgentsClient == null)
            {
                _logger.LogWarning("⚠️ Azure AI Agents not available for inline CSV analysis");
                return;
            }

            _logger.LogInformation("📊 Starting inline CSV analysis for: {FileName}", fileName);

            // Get model deployment name from configuration
            var modelDeploymentName = _configuration["Values:AzureOpenAI:DeploymentName"] ?? 
                                    _configuration["AzureOpenAI:DeploymentName"] ?? 
                                    "gpt4mini";

            // Define the agent using configuration
            PersistentAgent definition = await _aiAgentsClient.Administration.CreateAgentAsync(
                modelDeploymentName,
                name: "Inline CSV Analyzer",
                instructions: """
            You are a professional data analyst specializing in CSV data analysis using Python code interpreter.
            
            When analyzing CSV data:
            1. ALWAYS use the code interpreter tool to write and execute Python code
            2. Use pandas and StringIO to load CSV data from provided text
            3. Examine the data structure: columns, data types, number of rows
            4. Provide thorough data analysis based on the user's questions
            5. Create visualizations when appropriate (matplotlib, seaborn)
            6. Provide specific numerical insights and findings
            7. Answer questions with concrete evidence from the data
            
            Python libraries available: pandas, numpy, matplotlib, seaborn, scipy, sklearn
            
            Always start by loading the CSV data using pandas.read_csv(StringIO(csv_text)).
            Be thorough and provide detailed, data-driven responses.
            """,
                tools: [new CodeInterpreterToolDefinition()]);

            AzureAIAgent agent = new(definition, _aiAgentsClient);
            AzureAIAgentThread thread = new(_aiAgentsClient);

            try
            {
                // Create message with inline CSV data
                var message = $"""
            Please analyze the following CSV data and answer this question: {question}

            CSV Data:
            ```csv
            {csvContent}
            ```

            Use Python code interpreter to:
            1. Load this CSV data using pandas and StringIO
            2. Explore the data structure and content
            3. Answer the specific question with data-driven insights
            4. Provide visualizations if relevant

            Start your analysis now using code interpreter.
            """;

                await InvokeAgentAsync(message);
            }
            finally
            {
                await thread.DeleteAsync();
                await _aiAgentsClient.Administration.DeleteAgentAsync(agent.Id);
            }

            // Local function to invoke agent and display the conversation messages.
            async Task InvokeAgentAsync(string input)
            {
                ChatMessageContent message = new(AuthorRole.User, input);
                WriteAgentChatMessage(message);

                await foreach (ChatMessageContent response in agent.InvokeAsync(message, thread))
                {
                    WriteAgentChatMessage(response);
                    await DownloadContentAsync(response);
                }
            }
        }

        /// <summary>
        /// Extract the RespuestaFinal from JSON response if present
        /// </summary>
        private string ExtractFinalResponseFromJson(string agentResponse)
        {
            try
            {
                _logger.LogInformation("🔍 Attempting to extract RespuestaFinal from agent response");
                
                // Look for JSON in the agent response
                var lines = agentResponse.Split('\n');
                var jsonFound = false;
                var jsonContent = new StringBuilder();
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    // Start collecting JSON when we find a line that starts with {
                    if (trimmedLine.StartsWith("{") && !jsonFound)
                    {
                        jsonFound = true;
                        jsonContent.AppendLine(trimmedLine);
                    }
                    // Continue collecting until we find the closing }
                    else if (jsonFound)
                    {
                        jsonContent.AppendLine(trimmedLine);
                        // Stop when we find a line that ends with }
                        if (trimmedLine.EndsWith("}"))
                        {
                            break;
                        }
                    }
                }
                
                if (jsonFound && jsonContent.Length > 0)
                {
                    var jsonString = jsonContent.ToString().Trim();
                    _logger.LogInformation("📄 Found JSON content: {JsonLength} characters", jsonString.Length);
                    
                    try
                    {
                        // Parse the JSON
                        var jsonObject = JObject.Parse(jsonString);
                        
                        // Extract RespuestaFinal
                        var respuestaFinal = jsonObject["RespuestaFinal"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(respuestaFinal))
                        {
                            _logger.LogInformation("✅ Successfully extracted RespuestaFinal");
                            return $"📊 **Análisis CSV Completado**\n\n{respuestaFinal}";
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ RespuestaFinal not found in JSON");
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogWarning(jsonEx, "⚠️ Failed to parse JSON: {Message}", jsonEx.Message);
                    }
                }
                else
                {
                    _logger.LogInformation("📝 No JSON found in agent response, returning original content");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting final response: {Message}", ex.Message);
            }
            
            // Return original response if JSON extraction fails
            return agentResponse;
        }

        /// <summary>
        /// Provide basic fallback analysis when Azure AI Agent fails
        /// </summary>
        private async Task<string> ProvideFallbackAnalysis(Stream csvStream, string question)
        {
            _logger.LogInformation("🔄 Providing fallback CSV analysis");

            try
            {
                csvStream.Position = 0;

                using var reader = new StreamReader(csvStream);
                var csvContent = await reader.ReadToEndAsync();

                var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var headerLine = lines.FirstOrDefault();
                var dataLines = lines.Skip(1).ToArray();

                var fallbackResult = new StringBuilder();
                fallbackResult.AppendLine("📊 **Fallback CSV Analysis**");
                fallbackResult.AppendLine("(Basic analysis when Azure AI Agent is unavailable)");
                fallbackResult.AppendLine();

                if (headerLine != null)
                {
                    var headers = headerLine.Split(',').Select(h => h.Trim('"')).ToArray();
                    fallbackResult.AppendLine($"📋 **Dataset Overview:**");
                    fallbackResult.AppendLine($"   • Total columns: {headers.Length}");
                    fallbackResult.AppendLine($"   • Total rows: {dataLines.Length}");
                    fallbackResult.AppendLine();

                    fallbackResult.AppendLine($"📄 **Column Names:**");
                    foreach (var header in headers.Take(20)) // Show first 20 columns
                    {
                        fallbackResult.AppendLine($"   • {header}");
                    }
                    if (headers.Length > 20)
                    {
                        fallbackResult.AppendLine($"   • ... and {headers.Length - 20} more columns");
                    }
                    fallbackResult.AppendLine();

                    // Try to identify financial columns
                    var financialColumns = headers.Where(h =>
                        h.ToLower().Contains("total") ||
                        h.ToLower().Contains("amount") ||
                        h.ToLower().Contains("tax") ||
                        h.ToLower().Contains("price")).ToArray();

                    if (financialColumns.Any())
                    {
                        fallbackResult.AppendLine($"💰 **Financial Columns Detected:**");
                        foreach (var col in financialColumns)
                        {
                            fallbackResult.AppendLine($"   • {col}");
                        }
                        fallbackResult.AppendLine();
                    }

                    // Check for date columns
                    var dateColumns = headers.Where(h =>
                        h.ToLower().Contains("date") ||
                        h.ToLower().Contains("created") ||
                        h.ToLower().Contains("invoice")).ToArray();

                    if (dateColumns.Any())
                    {
                        fallbackResult.AppendLine($"📅 **Date Columns Detected:**");
                        foreach (var col in dateColumns)
                        {
                            fallbackResult.AppendLine($"   • {col}");
                        }
                        fallbackResult.AppendLine();
                    }

                    // Specific handling for histogram/visualization requests
                    if (question.ToLower().Contains("histograma") || question.ToLower().Contains("histogram") ||
                        question.ToLower().Contains("gráfico") || question.ToLower().Contains("chart") ||
                        question.ToLower().Contains("diagram"))
                    {
                        fallbackResult.AppendLine($"📊 **Visualization Request Detected:**");
                        fallbackResult.AppendLine($"   • Request type: Histogram/Chart");
                        fallbackResult.AppendLine($"   • ⚠️ **Azure AI Agent required for visualizations**");
                        fallbackResult.AppendLine($"   • The Azure AI Agent with Code Interpreter can create:");
                        fallbackResult.AppendLine($"     - Histograms of invoice totals");
                        fallbackResult.AppendLine($"     - Monthly spending charts");
                        fallbackResult.AppendLine($"     - Vendor comparison plots");
                        fallbackResult.AppendLine($"     - Time series analysis");
                        fallbackResult.AppendLine();

                        // Try to provide basic stats for histogram data
                        if (financialColumns.Any())
                        {
                            fallbackResult.AppendLine($"   📈 **Available Data for Histograms:**");
                            foreach (var col in financialColumns.Take(5))
                            {
                                fallbackResult.AppendLine($"     • {col}: {dataLines.Length} data points");
                            }
                            fallbackResult.AppendLine();
                        }
                    }

                    // Try to extract some sample data for context
                    if (dataLines.Length > 0)
                    {
                        fallbackResult.AppendLine($"📝 **Sample Data (First Row):**");
                        var firstDataLine = dataLines[0];
                        var values = firstDataLine.Split(',').Take(Math.Min(10, headers.Length)).ToArray();

                        for (int i = 0; i < values.Length; i++)
                        {
                            var cleanValue = values[i].Trim('"').Trim();
                            if (!string.IsNullOrEmpty(cleanValue))
                            {
                                fallbackResult.AppendLine($"   • {headers[i]}: {cleanValue}");
                            }
                        }
                        fallbackResult.AppendLine();
                    }

                    fallbackResult.AppendLine($"❓ **Question:** {question}");
                    fallbackResult.AppendLine($"⚠️ **Note:** This is a basic analysis. For detailed calculations, visualization, and advanced analysis, the Azure AI Agent with Code Interpreter is needed.");
                    fallbackResult.AppendLine();
                    fallbackResult.AppendLine($"🔄 **Recommendations:**");
                    fallbackResult.AppendLine($"   • Try the question again (Azure AI Agent may be temporarily unavailable)");
                    fallbackResult.AppendLine($"   • Simplify the request if it's very complex");
                    fallbackResult.AppendLine($"   • For histograms, ensure data contains numerical columns");
                }
                else
                {
                    fallbackResult.AppendLine("❌ Unable to parse CSV headers");
                }

                return fallbackResult.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in fallback analysis: {Message}", ex.Message);
                return $"❌ Fallback analysis error: {ex.Message}";
            }
        }
    }

    public class CsvAnalysis
    {

        public string Question { get; set; } = string.Empty;
        public string AIResponse { get; set; } = string.Empty;

        public string FileID { get; set; } = string.Empty;
    }
}
