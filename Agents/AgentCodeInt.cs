using Azure.AI.Agents.Persistent;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Resources;
using System.Text;

namespace Agents;

/// <summary>
/// Demonstrate using code-interpreter to manipulate and generate CSV files with <see cref="AzureAIAgent"/>.
/// </summary>
public class AgentCodeInt : BaseAzureAgent
{
    public AgentCodeInt(ILogger<AgentCodeInt> logger) : base(logger)
    {
        Output.LogInformation("📊 AgentCodeInt initialized with Azure AI Agents + Code Interpreter");
    }
    
    public async Task<string> AnalyzeCSVFileUsingAzureAIAgentAsync(Stream csvStream, string question)
    {
        // StringBuilder to collect all results
        var allResults = new StringBuilder();
        allResults.AppendLine("🔍 CSV Analysis Results:");
        allResults.AppendLine(new string('=', 50));
        
        try
        {
            csvStream.Position = 0;
            Output.LogInformation("📤 Uploading CSV data to Azure AI Agents...");
            
            // Validate CSV size (Azure AI Agents has file size limits)
            var csvSize = csvStream.Length;
            Output.LogInformation("📊 CSV file size: {SizeKB} KB", csvSize / 1024);
            
            if (csvSize > 100 * 1024 * 1024) // 100MB limit
            {
                Output.LogWarning("⚠️ CSV file is very large ({SizeKB} KB), this may cause issues", csvSize / 1024);
            }
            
            PersistentAgentFileInfo fileInfo = await this.Client.Files.UploadFileAsync(csvStream, PersistentAgentFilePurpose.Agents, "invoices.csv");
            Output.LogInformation("✅ CSV file uploaded successfully. File ID: {FileId}", fileInfo.Id);

            // Get model deployment name from configuration
            var configuration = LoadConfiguration();
            var modelDeploymentName = GetConfigurationValue(configuration, "AzureOpenAI:DeploymentName", "gpt4mini");

            // For visualization tasks, try to use a more powerful model if available
            if (question.ToLower().Contains("histograma") || question.ToLower().Contains("histogram") ||
                question.ToLower().Contains("gráfico") || question.ToLower().Contains("chart") ||
                question.ToLower().Contains("diagram"))
            {
                // Try to use gpt-4 for complex visualizations
                var alternativeModel = GetConfigurationValue(configuration, "AzureOpenAI:AlternativeDeploymentName", modelDeploymentName, false);
                if (!string.IsNullOrEmpty(alternativeModel) && alternativeModel != modelDeploymentName)
                {
                    modelDeploymentName = alternativeModel;
                    Output.LogInformation("🎨 Using alternative model for visualization: {ModelName}", modelDeploymentName);
                }
            }

            Output.LogInformation("🤖 Creating Azure AI Agent with model: {ModelName}", modelDeploymentName);
            Output.LogInformation("📏 Instructions length: {Length} characters", """
                You are a professional data analyst with code interpreter capabilities.
                
                CORE RULES:
                - NEVER ask questions - provide immediate analysis
                - ALWAYS create visualizations when requested (histograms, charts, plots)
                - Use pandas for data loading and matplotlib for visualizations
                - Handle encoding errors gracefully (try utf-8, latin-1, cp1252)
                - Complete analysis without stopping for user input
                
                VISUALIZATION REQUIREMENTS:
                - Create clear histograms and charts as requested
                - Use proper figure size: plt.figure(figsize=(12, 8))
                - Add titles, labels, and save as PNG
                - Handle Spanish text properly in plots
                
                SAMPLE CODE PATTERN:
                ```python
                import pandas as pd
                import matplotlib.pyplot as plt
                
                # Load CSV with error handling
                try:
                    df = pd.read_csv('/mnt/data/invoices.csv')
                except:
                    df = pd.read_csv('/mnt/data/invoices.csv', encoding='latin-1')
                
                # Create visualization
                plt.figure(figsize=(12, 8))
                plt.hist(df['InvoiceTotal'], bins=20, alpha=0.7)
                plt.title('Invoice Totals Histogram')
                plt.xlabel('Amount ($)')
                plt.ylabel('Frequency')
                plt.savefig('histogram.png', dpi=300, bbox_inches='tight')
                plt.show()
                ```
                
                FORBIDDEN: Do not ask "¿Quieres que...?" or similar questions.
                EXECUTE analysis and visualizations immediately.
                """.Length);
            // Define the agent using configuration with improved instructions
            PersistentAgent definition = null;
            try
            {
                Output.LogInformation("🤖 Creating Azure AI Agent...");
                Output.LogInformation("   • Model: {ModelName}", modelDeploymentName);
                Output.LogInformation("   • Tools: Code Interpreter");
                Output.LogInformation("   • File ID: {FileId}", fileInfo.Id);
                
                definition = await this.Client.Administration.CreateAgentAsync(
                    modelDeploymentName,
                    name: "CSV Data Analyzer",
                    instructions: """
                    You are a professional data analyst with code interpreter capabilities.
                    
                    CORE RULES:
                    - NEVER ask questions - provide immediate analysis
                    - ALWAYS create visualizations when requested (histograms, charts, plots)
                    - Use pandas for data loading and matplotlib for visualizations
                    - Handle encoding errors gracefully (try utf-8, latin-1, cp1252)
                    - Complete analysis without stopping for user input
                    
                    VISUALIZATION REQUIREMENTS:
                    - Create clear histograms and charts as requested
                    - Use proper figure size: plt.figure(figsize=(12, 8))
                    - Add titles, labels, and save as PNG
                    - Handle Spanish text properly in plots
                    
                    SAMPLE CODE PATTERN:
                    ```python
                    import pandas as pd
                    import matplotlib.pyplot as plt
                    
                    # Load CSV with error handling
                    try:
                        df = pd.read_csv('/mnt/data/invoices.csv')
                    except:
                        df = pd.read_csv('/mnt/data/invoices.csv', encoding='latin-1')
                    
                    # Create visualization
                    plt.figure(figsize=(12, 8))
                    plt.hist(df['InvoiceTotal'], bins=20, alpha=0.7)
                    plt.title('Invoice Totals Histogram')
                    plt.xlabel('Amount ($)')
                    plt.ylabel('Frequency')
                    plt.savefig('histogram.png', dpi=300, bbox_inches='tight')
                    plt.show()
                    ```
                    
                    FORBIDDEN: Do not ask "¿Quieres que...?" or similar questions.
                    EXECUTE analysis and visualizations immediately.
                    """,
                    tools: [new CodeInterpreterToolDefinition()],
                    toolResources:
                        new()
                        {
                            CodeInterpreter = new()
                            {
                                FileIds = { fileInfo.Id },
                            }
                        });
                        
                Output.LogInformation("✅ Azure AI Agent definition created successfully");
            }
            catch (Exception agentCreationEx)
            {
                Output.LogError(agentCreationEx, "❌ Failed to create Azure AI Agent: {Message}", agentCreationEx.Message);
                throw new InvalidOperationException($"Agent creation failed: {agentCreationEx.Message}", agentCreationEx);
            }
            AzureAIAgent agent = new(definition, this.Client);
            AzureAIAgentThread thread = new(this.Client);

            Output.LogInformation("✅ Azure AI Agent created successfully. Agent ID: {AgentId}", agent.Id);

            // Respond to user input and collect results with timeout and retry logic
            try
            {
                Output.LogInformation("🔍 Starting CSV analysis with Code Interpreter...");
                
                // Add timeout for the operation
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // 5 minute timeout
                
                var result = await InvokeAgentWithTimeoutAsync(question, cts.Token);
                allResults.AppendLine(result);
                allResults.AppendLine();
                
                Output.LogInformation("✅ CSV analysis completed successfully!");
                
                // Log and return the complete results
                var finalResults = allResults.ToString();
                Output.LogInformation("📊 Complete Analysis Results:\n{Results}", finalResults);
                
                return finalResults;
            }
            catch (OperationCanceledException)
            {
                var timeoutMsg = "⏰ CSV analysis timed out after 5 minutes. The dataset might be too complex or large.";
                Output.LogWarning(timeoutMsg);
                allResults.AppendLine(timeoutMsg);
                return allResults.ToString();
            }
            finally
            {
                Output.LogInformation("🧹 Cleaning up Azure AI resources...");
                try
                {
                    await thread.DeleteAsync();
                    await this.Client.Administration.DeleteAgentAsync(agent.Id);
                    await this.Client.Files.DeleteFileAsync(fileInfo.Id);
                    Output.LogInformation("✅ Cleanup completed");
                }
                catch (Exception cleanupEx)
                {
                    Output.LogError(cleanupEx, "⚠️ Error during cleanup: {Message}", cleanupEx.Message);
                }
            }

            // Improved InvokeAgentAsync with timeout support
            async Task<string> InvokeAgentWithTimeoutAsync(string input, CancellationToken cancellationToken)
            {
                var responseBuilder = new StringBuilder();
                
                try
                {
                    ChatMessageContent message = new(AuthorRole.User, input);
                    this.WriteAgentChatMessage(message);
                    
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
                            Output.LogWarning("🔄 Maximum response count reached, stopping conversation");
                            responseBuilder.AppendLine("⚠️ Analysis stopped: Maximum response count reached");
                            break;
                        }

                        this.WriteAgentChatMessage(response);
                        await this.DownloadContentAsync(response);
                        
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
                            Output.LogInformation("🎯 Analysis completion detected");
                            break;
                        }
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    var errorMsg = $"❌ Error processing question '{input}': {ex.Message}";
                    Output.LogError(ex, "❌ Error in InvokeAgentWithTimeoutAsync: {Message}", ex.Message);
                    responseBuilder.AppendLine(errorMsg);
                    
                    // Try to provide a basic fallback analysis
                    responseBuilder.AppendLine("");
                    responseBuilder.AppendLine("🔄 Attempting fallback analysis...");
                    
                    try
                    {
                        var fallbackResult = await ProvideFallbackAnalysis(csvStream, question);
                        responseBuilder.AppendLine(fallbackResult);
                    }
                    catch (Exception fallbackEx)
                    {
                        Output.LogError(fallbackEx, "❌ Fallback analysis also failed: {Message}", fallbackEx.Message);
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
                            responseBuilder.AppendLine($"   • Question: {question}");
                            responseBuilder.AppendLine("   • ⚠️ **Azure AI Agent with Code Interpreter required for full analysis**");
                        }
                        catch (Exception basicEx)
                        {
                            Output.LogError(basicEx, "❌ Even basic CSV info failed: {Message}", basicEx.Message);
                            responseBuilder.AppendLine($"❌ Complete analysis failure. Question: {question}");
                        }
                    }
                }
                
                return responseBuilder.ToString();
            }
        }
        catch (Exception ex)
        {
            var errorResult = $"❌ Error during CSV analysis: {ex.Message}";
            allResults.AppendLine(errorResult);
            Output.LogError(ex, "❌ Error during CSV analysis: {Message}", ex.Message);
            
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
                Output.LogError(fallbackEx, "❌ Fallback analysis failed: {Message}", fallbackEx.Message);
                allResults.AppendLine($"❌ Fallback analysis failed: {fallbackEx.Message}");
            }
            
            // Still return the results collected so far plus the error
            return allResults.ToString();
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
            Output.LogError("❌ CSV file not found: {FilePath}", csvFilePath);
            return;
        }

        Output.LogInformation("📊 Starting custom CSV analysis for file: {FilePath}", csvFilePath);

        // Upload the custom CSV file
        using var fileStream = File.OpenRead(csvFilePath);
        PersistentAgentFileInfo fileInfo = await this.Client.Files.UploadFileAsync(fileStream, PersistentAgentFilePurpose.Agents, Path.GetFileName(csvFilePath));

        // Get model deployment name from configuration
        var configuration = LoadConfiguration();
        var modelDeploymentName = GetConfigurationValue(configuration, "AzureOpenAI:DeploymentName", "gpt4mini");

        // Define the agent using configuration
        PersistentAgent definition = await this.Client.Administration.CreateAgentAsync(
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

        AzureAIAgent agent = new(definition, this.Client);
        AzureAIAgentThread thread = new(this.Client);

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
            await this.Client.Administration.DeleteAgentAsync(agent.Id);
            await this.Client.Files.DeleteFileAsync(fileInfo.Id);
        }

        // Local function to invoke agent and display the conversation messages.
        async Task InvokeAgentAsync(string input)
        {
            ChatMessageContent message = new(AuthorRole.User, input);
            this.WriteAgentChatMessage(message);

            await foreach (ChatMessageContent response in agent.InvokeAsync(message, thread))
            {
                this.WriteAgentChatMessage(response);
                await this.DownloadContentAsync(response);
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
        Output.LogInformation("📊 Starting inline CSV analysis for: {FileName}", fileName);

        // Get model deployment name from configuration
        var configuration = LoadConfiguration();
        var modelDeploymentName = GetConfigurationValue(configuration, "AzureOpenAI:DeploymentName", "gpt4mini");

        // Define the agent using configuration
        PersistentAgent definition = await this.Client.Administration.CreateAgentAsync(
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

        AzureAIAgent agent = new(definition, this.Client);
        AzureAIAgentThread thread = new(this.Client);

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
            await this.Client.Administration.DeleteAgentAsync(agent.Id);
        }

        // Local function to invoke agent and display the conversation messages.
        async Task InvokeAgentAsync(string input)
        {
            ChatMessageContent message = new(AuthorRole.User, input);
            this.WriteAgentChatMessage(message);

            await foreach (ChatMessageContent response in agent.InvokeAsync(message, thread))
            {
                this.WriteAgentChatMessage(response);
                await this.DownloadContentAsync(response);
            }
        }
    }

    /// <summary>
    /// Provide basic fallback analysis when Azure AI Agent fails
    /// </summary>
    private async Task<string> ProvideFallbackAnalysis(Stream csvStream, string question)
    {
        Output.LogInformation("🔄 Providing fallback CSV analysis");
        
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
            Output.LogError(ex, "❌ Error in fallback analysis: {Message}", ex.Message);
            return $"❌ Fallback analysis error: {ex.Message}";
        }
    }
}