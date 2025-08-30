// Copyright (c) Microsoft. All rights reserved.
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using TwinFx.Clients;
using TwinFx.Services;

namespace TwinFx.Agents;

/// <summary>
/// Process Document Data Agent
/// ===========================
/// 
/// Agente especializado en extraer y procesar datos de facturas usando Semantic Kernel.
/// Genera tres tipos de salida: resumen en texto, HTML formateado y datos JSON estructurados.
/// 
/// Características:
/// - Configurado con Azure OpenAI (compatible con el proyecto)
/// - Extracción determinística con temperature=0.0
/// - Triple salida: texto, HTML y JSON estructurado
/// - Análisis comprehensivo de facturas
/// - Manejo de errores robusto
/// - Logging detallado para transparencia
/// - Basado en Semantic Kernel para estabilidad
/// 
/// Salidas generadas:
/// 1. text_summary: Resumen narrativo para humanos
/// 2. html_output: Contenido formateado para UI web
/// 3. structured_data: Registros JSON para procesamiento digital
/// 4. text_report: Contenido sin HTML para lectura
/// 5. tablesContent: Datos de tablas en formato texto
/// 
/// Author: TwinAgent MCP Project
/// Date: January 15, 2025
/// </summary>
public class ProcessDocumentDataAgent : BaseTwinAgentTest<object>
{
    private readonly ILogger<ProcessDocumentDataAgent> _logger;
    private readonly IConfiguration _configuration;
    private Kernel? _kernel;

    public ProcessDocumentDataAgent(ILogger<ProcessDocumentDataAgent> logger, IConfiguration configuration)
        : base(logger)
    {
        _logger = logger;
        _configuration = configuration;
        
        _logger.LogInformation("?? ProcessDocumentDataAgent initialized with Semantic Kernel");
    }

    /// <summary>
    /// Initialize Semantic Kernel agent with Azure OpenAI configuration
    /// </summary>
    private async Task InitializeAgentAsync()
    {
        if (_kernel != null)
            return; // Already initialized

        _logger.LogInformation("?? Initializing Process Document Data Agent with Semantic Kernel...");

        try
        {
            // Create kernel builder
            IKernelBuilder builder = Kernel.CreateBuilder();

            // Add Azure OpenAI chat completion using base class method
            base.AddChatClientToKernel(builder);

            // Build the kernel
            _kernel = builder.Build();

            _logger.LogInformation("? Process Document Data Agent initialized successfully!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to initialize agent");
            throw;
        }
    }

    /// <summary>
    /// Extract structured data from unstructured text
    /// </summary>
    /// <param name="text">Input text to process</param>
    /// <param name="extractionSchema">Optional JSON schema to guide extraction</param>
    /// <returns>Dictionary with extracted data and metadata</returns>
    public async Task<DocumentExtractionResult> ExtractDataFromTextAsync(string text, object? extractionSchema = null)
    {
        _logger.LogInformation("?? Starting text data extraction...");
        _logger.LogInformation($"?? Input text length: {text?.Length ?? 0} characters");

        try
        {
            // Initialize the agent if not already done
            await InitializeAgentAsync();

            if (string.IsNullOrWhiteSpace(text))
            {
                return new DocumentExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Empty or invalid input text",
                    ExtractedData = null,
                    Metadata = new DocumentMetadata
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = 0
                    }
                };
            }

            // Create the extraction prompt focused on executive summaries
            var prompt = $"""
Analyze the following invoice data and create an executive summary in TWO formats:

INVOICE DATA TO ANALYZE:
{text}

INSTRUCTIONS:
Create a JSON response with exactly TWO fields: "executive_summary_html" and "executive_summary_text"

1. "executive_summary_html": A comprehensive executive summary formatted in HTML with:
   - Professional styling using inline CSS
   - All financial values highlighted
   - Key vendor and customer information prominently displayed
   - Line items and services clearly organized in tables
   - Use vibrant colors and modern styling
   - Include all amounts, dates, and important details from the invoice

2. "executive_summary_text": The exact same content as the HTML version but in plain text format:
   - No HTML tags whatsoever
   - Same comprehensive information
   - Well-structured with clear sections
   - Easy to read format with proper spacing

IMPORTANT: 
- Include ALL financial data (totals, subtotals, taxes, individual line items)
- Include ALL vendor and customer information
- Include ALL dates and reference numbers
- Make it comprehensive and executive-level detailed
- Both versions must contain identical information, just different formatting

REQUIRED JSON FORMAT EXAMPLE:
The response should be a JSON object with two string fields:
- executive_summary_html: containing HTML formatted summary
- executive_summary_text: containing plain text formatted summary

CRITICAL: Return ONLY the JSON with these TWO fields, no additional text before or after.
""";

            _logger.LogInformation("?? Sending executive summary request to Semantic Kernel...");

            // Get chat completion service from kernel
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();

            // Create chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(prompt);

            // Create execution settings with temperature=0.0 for deterministic results
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 6000 },
                    { "temperature", 0.0 }
                }
            };

            // Get the response
            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var responseContent = response.Content ?? string.Empty;

            _logger.LogInformation("? Received executive summary response from Semantic Kernel");
            _logger.LogInformation($"?? Response length: {responseContent.Length} characters");

            // Parse the response
            var extractedData = ParseAgentResponse(responseContent);

            // Create the final result with metadata
            var result = new DocumentExtractionResult
            {
                Success = true,
                ExtractedData = extractedData,
                RawResponse = responseContent,
                Metadata = new DocumentMetadata
                {
                    Timestamp = DateTime.UtcNow,
                    InputLength = text.Length,
                    OutputLength = responseContent.Length,
                    AgentModel = "Azure OpenAI",
                    ExtractionSchemaUsed = extractionSchema != null,
                    AnalysisType = "executive_summary"
                }
            };

            _logger.LogInformation("?? Executive summary extraction completed successfully!");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in executive summary extraction");

            return new DocumentExtractionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ExtractedData = null,
                RawResponse = null,
                Metadata = new DocumentMetadata
                {
                    Timestamp = DateTime.UtcNow,
                    InputLength = text?.Length ?? 0,
                    ErrorDetails = ex.Message
                }
            };
        }
    }

    /// <summary>
    /// Extract complete invoice analysis with text summary, HTML, and structured JSON data
    /// </summary>
    /// <param name="invoiceText">Raw invoice text content</param>
    /// <param name="tablesContent">Tables content from the invoice</param>
    /// <returns>Dictionary containing text summary, HTML output, and structured JSON data</returns>
    public async Task<InvoiceExtractionResult> ExtractInvoiceDataAsync(string invoiceText, string tablesContent = "")
    {
        _logger.LogInformation("?? Starting comprehensive invoice data extraction...");

        try
        {
            // Initialize the agent if not already done
            await InitializeAgentAsync();

            // Create the invoice-specific extraction prompt
            var prompt = $$"""
Analiza la siguiente factura y genera CINCO salidas específicas:

TEXTO DE LA FACTURA:
{{invoiceText}}

TABLAS DE LA FACTURA:
{{tablesContent}}

INSTRUCCIONES:
Genera un JSON con exactamente CINCO campos: "text_summary", "text_report", "tablesContent", "html_output", y "structured_data"

1. "text_summary": Un resumen detallado en texto plano para humanos que explique:
   - Información del proveedor y cliente
   - Detalles financieros (montos, fechas de pago)
   - Servicios incluidos y sus costos (extrae TODA la información de las tablas)
   - Estado de la cuenta y pagos anteriores
   - Fechas importantes y información de autopago
   - Incluye TODOS los datos de las tablas en formato texto legible

2. "text_report": El mismo contenido que html_output pero SIN etiquetas HTML, solo texto plano para lectura fácil.

3. "tablesContent": Todos los datos de las tablas encontradas en formato texto legible, incluyendo:
   - Información de Sold To y Bill To
   - Resumen de facturación
   - Items de línea con descripciones y montos
   - Cualquier otra tabla encontrada en el documento

4. "html_output": El mismo contenido formateado en HTML profesional con:
   - Estructura clara con encabezados
   - Tablas para datos financieros (usa los datos de las tablas extraídas)
   - Secciones bien organizadas
   - Estilos CSS inline básicos para presentación
   - Formato legible y profesional

5. "structured_data": Datos JSON estructurados para procesamiento digital con:
   - invoice_info: número, fechas, moneda
   - vendor: nombre, dirección, contacto
   - customer: nombre, dirección, cuenta
   - financial_summary: totales, balances, cargos
   - line_items: array de servicios con descripción, monto, período (extrae de las tablas)
   - payment_info: fechas de autopago, métodos de pago
   - account_activity: historial de pagos y balances
   - tables_data: datos estructurados de todas las tablas encontradas

IMPORTANTE: Extrae TODA la información de las tablas y úsala en todos los campos.
Responde ÚNICAMENTE con JSON válido que contenga estos CINCO campos.

FORMATO JSON REQUERIDO:
{
  "text_summary": "texto del resumen aquí...",
  "text_report": "mismo contenido que html_output pero sin etiquetas HTML, solo texto plano para lectura...",
  "tablesContent": "datos de las tablas encontradas aquí...",
  "html_output": "<div>HTML aquí...</div>",
  "structured_data": {
    "invoice_info": {
      "number": "G055864003",
      "date": "08/09/2024",
      "currency": "USD"
    },
    "vendor": {
      "name": "Microsoft Corporation",
      "address": "One Microsoft Way, Redmond WA 98052, United States",
      "contact": "FEIN: 91-1144442"
    },
    "customer": {
      "name": "FlatBit Inc",
      "address": "112 Loch Lomond St, Hutto, TX 78634-5707, United States"
    },
    "financial_summary": {
      "totals": {
        "charges": 247.9,
        "credits": 0.0,
        "subtotal": 247.9,
        "sales_tax": 16.81,
        "total": 264.71
      }
    },
    "line_items": [
      {
        "description": "Microsoft Azure Support - 1 Month",
        "amount": 31.39,
        "period": "07/12/2024 - 08/11/2024"
      }
    ],
    "payment_info": {
      "autopay_dates": ["08/09/2024"],
      "methods": ["Electronic Funds Transfer"]
    },
    "tables_data": {
      "sold_to": {
        "company": "FlatBit Inc",
        "address": "112 Loch Lomond St, Hutto, TX 78634-5707, US"
      },
      "bill_to": {
        "company": "FlatBit Inc",
        "address": "112 Loch Lomond St, Hutto, TX 78634-5707, US"
      }
    }
  }
}

CRÍTICO: Responde SOLO con el JSON válido que contenga los CINCO campos, sin texto adicional antes o después.
""";

            _logger.LogInformation("?? Sending comprehensive invoice extraction request...");

            // Get chat completion service from kernel
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();

            // Create chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(prompt);

            // Create execution settings with temperature=0.0 for deterministic results
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 6000 },
                    { "temperature", 0.0 }
                }
            };

            // Get the response
            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var responseContent = response.Content ?? string.Empty;

            _logger.LogInformation("?? Received comprehensive invoice analysis");
            _logger.LogInformation($"?? Response length: {responseContent.Length} characters");
            _logger.LogInformation($"?? RAW OPENAI RESPONSE PREVIEW: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}...");

            // Parse the agent's response
            var parsedData = ParseAgentResponse(responseContent);

            // Log the parsed data for debugging
            _logger.LogInformation("?? PARSED DATA FROM OPENAI:");
            _logger.LogInformation($"   • text_summary: {GetValuePreview(parsedData, "text_summary")}");
            _logger.LogInformation($"   • text_report: {GetValuePreview(parsedData, "text_report")}");
            _logger.LogInformation($"   • structured_data: {GetValuePreview(parsedData, "structured_data")}");
            _logger.LogInformation($"   • html_output: {GetValuePreview(parsedData, "html_output")}");
            _logger.LogInformation($"   • tablesContent: {GetValuePreview(parsedData, "tablesContent")}");
            _logger.LogInformation($"   • All keys: {string.Join(", ", parsedData.Keys)}");

            // Create the final result with metadata
            var result = new InvoiceExtractionResult
            {
                Success = true,
                TextSummary = GetStringValue(parsedData, "text_summary"),
                HtmlOutput = GetStringValue(parsedData, "html_output"),
                StructuredData = GetObjectValue(parsedData, "structured_data"),
                TextReport = GetStringValue(parsedData, "text_report"),
                TablesContent = GetStringValue(parsedData, "tablesContent"),
                RawResponse = responseContent,
                Metadata = new DocumentMetadata
                {
                    Timestamp = DateTime.UtcNow,
                    InputLength = invoiceText.Length,
                    OutputLength = responseContent.Length,
                    AgentModel = "Azure OpenAI",
                    AnalysisType = "comprehensive_invoice"
                }
            };

            _logger.LogInformation("? Comprehensive invoice analysis completed successfully!");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error in invoice analysis");

            return new InvoiceExtractionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                TextSummary = string.Empty,
                HtmlOutput = string.Empty,
                StructuredData = new Dictionary<string, object>(),
                TextReport = string.Empty,
                TablesContent = string.Empty,
                Metadata = new DocumentMetadata
                {
                    Timestamp = DateTime.UtcNow,
                    InputLength = invoiceText?.Length ?? 0,
                    ErrorDetails = ex.Message
                }
            };
        }
    }

    /// <summary>
    /// Extract structured data specifically from contract text
    /// </summary>
    /// <param name="contractText">Raw contract text content</param>
    /// <returns>Structured contract data</returns>
    public async Task<DocumentExtractionResult> ExtractContractDataAsync(string contractText)
    {
        _logger.LogInformation("?? Starting contract-specific data extraction...");

        // Define a contract-specific schema
        var contractSchema = new
        {
            contract_info = new
            {
                title = "string",
                number = "string",
                date = "string",
                effective_date = "string",
                expiration_date = "string"
            },
            parties = new[]
            {
                new
                {
                    role = "string",
                    name = "string",
                    address = "string",
                    representative = "string"
                }
            },
            terms = new
            {
                duration = "string",
                renewal_terms = "string",
                termination_conditions = "string"
            },
            financial = new
            {
                total_value = "number",
                payment_schedule = "string",
                currency = "string"
            },
            key_obligations = new[] { "string" },
            deliverables = new[] { "string" }
        };

        // Use the generic extraction method with the contract schema
        return await ExtractDataFromTextAsync(contractText, contractSchema);
    }

    /// <summary>
    /// Parse and validate the agent's response
    /// </summary>
    /// <param name="response">Raw response from the agent</param>
    /// <returns>Parsed JSON data or a structured fallback if parsing fails</returns>
    private Dictionary<string, object> ParseAgentResponse(string response)
    {
        try
        {
            var responseStr = response.Trim();

            _logger.LogInformation($"?? PARSING RESPONSE:");
            _logger.LogInformation($"   • Response length: {responseStr.Length}");
            _logger.LogInformation($"   • Starts with {{: {responseStr.StartsWith('{')}");
            _logger.LogInformation($"   • Ends with }}: {responseStr.EndsWith('}')}");
            _logger.LogInformation($"   • First 200 chars: {responseStr.Substring(0, Math.Min(200, responseStr.Length))}...");
            _logger.LogInformation($"   • Last 200 chars: ...{responseStr.Substring(Math.Max(0, responseStr.Length - 200))}");

            // Try to parse the response as JSON if it starts and ends with braces
            if (responseStr.StartsWith('{') && responseStr.EndsWith('}'))
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(responseStr);
                if (parsed != null)
                {
                    _logger.LogInformation("? Direct JSON parsing successful");
                    _logger.LogInformation($"   • Keys found: {string.Join(", ", parsed.Keys)}");
                    return parsed;
                }
            }

            // Otherwise, look for a JSON substring inside the response
            var jsonStart = responseStr.IndexOf('{');
            var jsonEnd = responseStr.LastIndexOf('}') + 1;
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonContent = responseStr.Substring(jsonStart, jsonEnd - jsonStart);
                _logger.LogInformation($"?? Found JSON substring from position {jsonStart} to {jsonEnd}");
                _logger.LogInformation($"   • JSON content length: {jsonContent.Length}");
                
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                if (parsed != null)
                {
                    _logger.LogInformation("? JSON substring parsing successful");
                    _logger.LogInformation($"   • Keys found: {string.Join(", ", parsed.Keys)}");
                    return parsed;
                }
            }

            _logger.LogWarning("?? No valid JSON found in response, creating structured fallback");
            _logger.LogWarning($"   • Full response: {responseStr}");
            
            return new Dictionary<string, object>
            {
                { "raw_text", responseStr },
                { "extraction_note", "Agent response was not in JSON format" },
                { "parsed_at", DateTime.UtcNow.ToString("O") }
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "?? JSON parsing failed");
            _logger.LogWarning($"   • Full response: {response}");
            
            return new Dictionary<string, object>
            {
                { "raw_text", response },
                { "parsing_error", ex.Message },
                { "extraction_note", "Failed to parse agent response as JSON" },
                { "parsed_at", DateTime.UtcNow.ToString("O") }
            };
        }
    }

    /// <summary>
    /// Helper method to get string value from parsed data
    /// </summary>
    private static string GetStringValue(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                return jsonElement.GetString() ?? string.Empty;
            }
            return value?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// Helper method to get object value from parsed data
    /// </summary>
    private static Dictionary<string, object> GetObjectValue(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText()) 
                       ?? new Dictionary<string, object>();
            }
            if (value is Dictionary<string, object> dict)
            {
                return dict;
            }
        }
        return new Dictionary<string, object>();
    }

    /// <summary>
    /// Helper method to get preview of a value for logging
    /// </summary>
    private static string GetValuePreview(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            var str = value?.ToString() ?? "NULL";
            return str.Length > 100 ? $"{str.Substring(0, 100)}..." : str;
        }
        return "NOT_FOUND";
    }

    /// <summary>
    /// Cleanup resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _kernel = null;
            _logger.LogInformation("? ProcessDocumentDataAgent resources cleaned up successfully!");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error during cleanup");
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Save comprehensive invoice analysis to Cosmos DB including all AI Agent data
    /// </summary>
    /// <param name="cosmosService">Cosmos DB service</param>
    /// <param name="extractionResult">Document Intelligence result</param>
    /// <param name="agentResult">AI Agent general processing result</param>
    /// <param name="invoiceAnalysisResult">AI Agent specialized invoice analysis result</param>
    /// <param name="containerName">Container name</param>
    /// <param name="fileName">File name</param>
    private async Task SaveComprehensiveInvoiceAnalysisAsync(
        CosmosDbTwinProfileService cosmosService,
        InvoiceAnalysisResult extractionResult,
        DocumentExtractionResult? agentResult,
        InvoiceExtractionResult? invoiceAnalysisResult,
        string containerName,
        string fileName)
    {
        try
        {
            // Get AI Agent executive summaries if available
            string executiveSummaryHtml = string.Empty;
            string executiveSummaryText = string.Empty;
            
            if (agentResult?.Success == true && agentResult.ExtractedData != null)
            {
                if (agentResult.ExtractedData.TryGetValue("executive_summary_html", out var htmlValue))
                {
                    executiveSummaryHtml = GetStringValue(agentResult.ExtractedData, "executive_summary_html");
                }
                if (agentResult.ExtractedData.TryGetValue("executive_summary_text", out var textValue))
                {
                    executiveSummaryText = GetStringValue(agentResult.ExtractedData, "executive_summary_text");
                }
            }

            // Get specialized invoice analysis data if available
            string aiTextSummary = string.Empty;
            string aiHtmlOutput = string.Empty;
            string aiTextReport = string.Empty;
            string aiTablesContent = string.Empty;
            Dictionary<string, object> aiStructuredData = new();

            if (invoiceAnalysisResult?.Success == true)
            {
                aiTextSummary = invoiceAnalysisResult.TextSummary ?? string.Empty;
                aiHtmlOutput = invoiceAnalysisResult.HtmlOutput ?? string.Empty;
                aiTextReport = invoiceAnalysisResult.TextReport ?? string.Empty;
                aiTablesContent = invoiceAnalysisResult.TablesContent ?? string.Empty;
                aiStructuredData = invoiceAnalysisResult.StructuredData ?? new Dictionary<string, object>();
            }

            // Create enhanced extraction result with AI data included
            var enhancedResult = new InvoiceAnalysisResult
            {
                Success = extractionResult.Success,
                ErrorMessage = extractionResult.ErrorMessage,
                InvoiceData = extractionResult.InvoiceData,
                Tables = extractionResult.Tables,
                RawDocumentFields = new Dictionary<string, DocumentFieldInfo>(extractionResult.RawDocumentFields)
                {
                    // Add AI Agent data as special fields
                    ["AI_ExecutiveSummaryHtml"] = new DocumentFieldInfo 
                    { 
                        Value = executiveSummaryHtml, 
                        Confidence = 1.0f, 
                        FieldType = "AI_Generated_HTML" 
                    },
                    ["AI_ExecutiveSummaryText"] = new DocumentFieldInfo 
                    { 
                        Value = executiveSummaryText, 
                        Confidence = 1.0f, 
                        FieldType = "AI_Generated_Text" 
                    },
                    ["AI_TextSummary"] = new DocumentFieldInfo 
                    { 
                        Value = aiTextSummary, 
                        Confidence = 1.0f, 
                        FieldType = "AI_Invoice_Summary" 
                    },
                    ["AI_HtmlOutput"] = new DocumentFieldInfo 
                    { 
                        Value = aiHtmlOutput, 
                        Confidence = 1.0f, 
                        FieldType = "AI_Invoice_HTML" 
                    },
                    ["AI_TextReport"] = new DocumentFieldInfo 
                    { 
                        Value = aiTextReport, 
                        Confidence = 1.0f, 
                        FieldType = "AI_Invoice_Report" 
                    },
                    ["AI_TablesContent"] = new DocumentFieldInfo 
                    { 
                        Value = aiTablesContent, 
                        Confidence = 1.0f, 
                        FieldType = "AI_Tables_Content" 
                    },
                    ["AI_StructuredData"] = new DocumentFieldInfo 
                    { 
                        Value = JsonSerializer.Serialize(aiStructuredData), 
                        Confidence = 1.0f, 
                        FieldType = "AI_Structured_JSON" 
                    }
                },
                ProcessedAt = extractionResult.ProcessedAt,
                SourceUri = extractionResult.SourceUri,
                TotalPages = extractionResult.TotalPages
            };

            // Save to Cosmos DB
            await cosmosService.SaveInvoiceAnalysisAsync(enhancedResult, containerName, fileName);
            
            _logger.LogInformation("?? Comprehensive invoice analysis saved including AI data:");
            _logger.LogInformation($"   ?? Document Intelligence: ? Complete");
            _logger.LogInformation($"   ?? Executive Summary HTML: {(string.IsNullOrEmpty(executiveSummaryHtml) ? "? Empty" : "? Saved")} ({executiveSummaryHtml.Length} chars)");
            _logger.LogInformation($"   ?? Executive Summary Text: {(string.IsNullOrEmpty(executiveSummaryText) ? "? Empty" : "? Saved")} ({executiveSummaryText.Length} chars)");
            _logger.LogInformation($"   ?? AI Text Summary: {(string.IsNullOrEmpty(aiTextSummary) ? "? Empty" : "? Saved")} ({aiTextSummary.Length} chars)");
            _logger.LogInformation($"   ?? AI HTML Output: {(string.IsNullOrEmpty(aiHtmlOutput) ? "? Empty" : "? Saved")} ({aiHtmlOutput.Length} chars)");
            _logger.LogInformation($"   ?? AI Structured Data: {(aiStructuredData.Count == 0 ? "? Empty" : "? Saved")} ({aiStructuredData.Count} fields)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error saving comprehensive invoice analysis to Cosmos DB");
            throw;
        }
    }

    /// <summary>
    /// Save COMPLETE ProcessAiDocumentsResult to Cosmos DB with ALL data from Document Intelligence and OpenAI
    /// </summary>
    /// <param name="cosmosService">Cosmos DB service</param>
    /// <param name="result">Complete ProcessAiDocumentsResult with all analysis data</param>
    private async Task SaveCompleteProcessAiDocumentsResultAsync(
        CosmosDbTwinProfileService cosmosService, 
        ProcessAiDocumentsResult result)
    {
        try
        {
            _logger.LogInformation("?? Saving COMPLETE ProcessAiDocumentsResult to Cosmos DB...");

            // Extract all data from the complete result
            var extractionResult = result.ExtractionResult;
            var agentResult = result.AgentResult;
            var invoiceAnalysisResult = result.InvoiceAnalysisResult;

            // Get AI Agent executive summaries if available
            string executiveSummaryHtml = string.Empty;
            string executiveSummaryText = string.Empty;
            
            if (agentResult?.Success == true && agentResult.ExtractedData != null)
            {
                executiveSummaryHtml = GetStringValue(agentResult.ExtractedData, "executive_summary_html");
                executiveSummaryText = GetStringValue(agentResult.ExtractedData, "executive_summary_text");
            }

            // Get specialized invoice analysis data if available  
            string aiTextSummary = invoiceAnalysisResult?.TextSummary ?? string.Empty;
            string aiHtmlOutput = invoiceAnalysisResult?.HtmlOutput ?? string.Empty;
            string aiTextReport = invoiceAnalysisResult?.TextReport ?? string.Empty;
            string aiTablesContent = invoiceAnalysisResult?.TablesContent ?? string.Empty;
            var aiStructuredData = invoiceAnalysisResult?.StructuredData ?? new Dictionary<string, object>();

            // Create enhanced DocumentFieldInfo dictionary with ALL AI data
            var allAiData = new Dictionary<string, DocumentFieldInfo>(extractionResult?.RawDocumentFields ?? new Dictionary<string, DocumentFieldInfo>())
            {
                // OpenAI Executive Summaries (from AgentResult)
                ["AI_ExecutiveSummaryHtml"] = new DocumentFieldInfo 
                { 
                    Value = executiveSummaryHtml, 
                    Confidence = 1.0f, 
                    FieldType = "AI_Executive_HTML" 
                },
                ["AI_ExecutiveSummaryText"] = new DocumentFieldInfo 
                { 
                    Value = executiveSummaryText, 
                    Confidence = 1.0f, 
                    FieldType = "AI_Executive_Text" 
                },
                // OpenAI Invoice Analysis (from InvoiceAnalysisResult - 5 campos)
                ["AI_TextSummary"] = new DocumentFieldInfo 
                { 
                    Value = aiTextSummary, 
                    Confidence = 1.0f, 
                    FieldType = "AI_Invoice_Summary" 
                },
                ["AI_HtmlOutput"] = new DocumentFieldInfo 
                { 
                    Value = aiHtmlOutput, 
                    Confidence = 1.0f, 
                    FieldType = "AI_Invoice_HTML" 
                },
                ["AI_TextReport"] = new DocumentFieldInfo 
                { 
                    Value = aiTextReport, 
                    Confidence = 1.0f, 
                    FieldType = "AI_Invoice_Report" 
                },
                ["AI_TablesContent"] = new DocumentFieldInfo 
                { 
                    Value = aiTablesContent, 
                    Confidence = 1.0f, 
                    FieldType = "AI_Tables_Content" 
                },
                ["AI_StructuredData"] = new DocumentFieldInfo 
                { 
                    Value = JsonSerializer.Serialize(aiStructuredData), 
                    Confidence = 1.0f, 
                    FieldType = "AI_Structured_JSON" 
                },
                // Add the complete processed text
                ["AI_ProcessedText"] = new DocumentFieldInfo 
                { 
                    Value = result.ProcessedText, 
                    Confidence = 1.0f, 
                    FieldType = "AI_Complete_ProcessedText" 
                },
                // Add metadata about the complete analysis
                ["AI_CompleteAnalysis_Summary"] = new DocumentFieldInfo 
                { 
                    Value = result.GetComprehensiveSummary(), 
                    Confidence = 1.0f, 
                    FieldType = "AI_Complete_Summary" 
                },
                ["AI_CompleteAnalysis_Insights"] = new DocumentFieldInfo 
                { 
                    Value = JsonSerializer.Serialize(result.GetKeyInsights()), 
                    Confidence = 1.0f, 
                    FieldType = "AI_Complete_Insights" 
                }
            };

            // Create enhanced extraction result with ALL data
            var completeEnhancedResult = new InvoiceAnalysisResult
            {
                Success = extractionResult?.Success ?? false,
                ErrorMessage = extractionResult?.ErrorMessage,
                InvoiceData = extractionResult?.InvoiceData ?? new StructuredInvoiceData(),
                Tables = extractionResult?.Tables ?? new List<ExtractedTable>(),
                RawDocumentFields = allAiData,
                ProcessedAt = result.ProcessedAt,
                SourceUri = $"{result.ContainerName}/{result.FilePath}/{result.FileName}",
                TotalPages = extractionResult?.TotalPages ?? 0
            };

            // Save to Cosmos DB using the existing method but with enhanced data
            await cosmosService.SaveInvoiceAnalysisAsync(completeEnhancedResult, result.ContainerName, result.FileName);
            
            _logger.LogInformation("?? ? COMPLETE ProcessAiDocumentsResult saved to Cosmos DB successfully!");
            _logger.LogInformation("?? Data saved includes:");
            _logger.LogInformation($"   ?? Document Intelligence: ? ({extractionResult?.RawDocumentFields.Count ?? 0} fields)");
            _logger.LogInformation($"   ?? Executive Summary HTML: {(string.IsNullOrEmpty(executiveSummaryHtml) ? "?" : "?")} ({executiveSummaryHtml.Length} chars)");
            _logger.LogInformation($"   ?? Executive Summary Text: {(string.IsNullOrEmpty(executiveSummaryText) ? "?" : "?")} ({executiveSummaryText.Length} chars)");
            _logger.LogInformation($"   ?? AI Text Summary: {(string.IsNullOrEmpty(aiTextSummary) ? "?" : "?")} ({aiTextSummary.Length} chars)");
            _logger.LogInformation($"   ?? AI HTML Output: {(string.IsNullOrEmpty(aiHtmlOutput) ? "?" : "?")} ({aiHtmlOutput.Length} chars)");
            _logger.LogInformation($"   ?? AI Text Report: {(string.IsNullOrEmpty(aiTextReport) ? "?" : "?")} ({aiTextReport.Length} chars)");
            _logger.LogInformation($"   ?? AI Tables Content: {(string.IsNullOrEmpty(aiTablesContent) ? "?" : "?")} ({aiTablesContent.Length} chars)");
            _logger.LogInformation($"   ?? AI Structured Data: {(aiStructuredData.Count == 0 ? "?" : "?")} ({aiStructuredData.Count} fields)");
            _logger.LogInformation($"   ?? Complete Processed Text: ? ({result.ProcessedText.Length} chars)");
            _logger.LogInformation($"   ?? Complete Summary: ?");
            _logger.LogInformation($"   ?? Key Insights: ?");
            _logger.LogInformation($"   ?? TOTAL AI FIELDS SAVED: {allAiData.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error saving COMPLETE ProcessAiDocumentsResult to Cosmos DB");
            throw;
        }
    }

    /// <summary>
    /// Process AI documents by extracting data from Azure Storage and then processing with AI agent
    /// </summary>
    /// <param name="containerName">Container name where the document is stored</param>
    /// <param name="filePath">Path to the document within the container</param>
    /// <param name="fileName">Original file name for reference</param>
    /// <returns>Complete document processing result with AI analysis</returns>
    public async Task<ProcessAiDocumentsResult> ProcessAiDocuments(string containerName, string filePath, string fileName)
    {
        _logger.LogInformation($"?? Starting ProcessAiDocuments for container: {containerName}, path: {filePath}, file: {fileName}");

        try
        {
            // Step 1: Extract data from document using DocumentIntelligenceService
            _logger.LogInformation("?? Step 1: Extracting data using DocumentIntelligenceService");
            
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, _configuration);

            var extractionResult = await documentIntelligenceService.ExtractInvoiceDataAsync(containerName, filePath, fileName);

            if (!extractionResult.Success)
            {
                _logger.LogError($"? Document extraction failed: {extractionResult.ErrorMessage}");
                return new ProcessAiDocumentsResult
                {
                    Success = false,
                    ErrorMessage = $"Document extraction failed: {extractionResult.ErrorMessage}",
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedAt = DateTime.UtcNow
                };
            }

            _logger.LogInformation($"? Document extraction successful. Found {extractionResult.Tables.Count} tables and {extractionResult.RawDocumentFields.Count} fields");

            // Step 2: Prepare comprehensive text content for AI processing
            _logger.LogInformation("?? Step 2: Preparing comprehensive text content for AI processing");
            
            var textBuilder = new StringBuilder();
            
            // Add invoice header information
            textBuilder.AppendLine("=== DOCUMENT INTELLIGENCE ANALYSIS ===");
            textBuilder.AppendLine($"Source: {containerName}/{filePath}/{fileName}");
            textBuilder.AppendLine($"Total Pages: {extractionResult.TotalPages}");
            textBuilder.AppendLine($"Processing Date: {extractionResult.ProcessedAt:yyyy-MM-dd HH:mm:ss}");
            textBuilder.AppendLine();

            // Add structured invoice data as text
            if (extractionResult.InvoiceData != null)
            {
                textBuilder.AppendLine("=== INVOICE STRUCTURED DATA ===");
                textBuilder.AppendLine($"Vendor: {extractionResult.InvoiceData.VendorName} (Confidence: {extractionResult.InvoiceData.VendorNameConfidence:P1})");
                textBuilder.AppendLine($"Vendor Address: {extractionResult.InvoiceData.VendorAddress}");
                textBuilder.AppendLine($"Customer: {extractionResult.InvoiceData.CustomerName} (Confidence: {extractionResult.InvoiceData.CustomerNameConfidence:P1})");
                textBuilder.AppendLine($"Customer Address: {extractionResult.InvoiceData.CustomerAddress}");
                textBuilder.AppendLine($"Invoice Number: {extractionResult.InvoiceData.InvoiceNumber}");
                textBuilder.AppendLine($"Invoice Date: {extractionResult.InvoiceData.InvoiceDate?.ToString("yyyy-MM-dd") ?? "N/A"}");
                textBuilder.AppendLine($"Due Date: {extractionResult.InvoiceData.DueDate?.ToString("yyyy-MM-dd") ?? "N/A"}");
                textBuilder.AppendLine($"SubTotal: {extractionResult.InvoiceData.SubTotal:C} (Confidence: {extractionResult.InvoiceData.SubTotalConfidence:P1})");
                textBuilder.AppendLine($"Total Tax: {extractionResult.InvoiceData.TotalTax:C}");
                textBuilder.AppendLine($"Invoice Total: {extractionResult.InvoiceData.InvoiceTotal:C} (Confidence: {extractionResult.InvoiceData.InvoiceTotalConfidence:P1})");
                textBuilder.AppendLine();

                // Add line items with confidence information
                if (extractionResult.InvoiceData.LineItems.Any())
                {
                    textBuilder.AppendLine("=== LINE ITEMS DETAILS ===");
                    for (int i = 0; i < extractionResult.InvoiceData.LineItems.Count; i++)
                    {
                        var item = extractionResult.InvoiceData.LineItems[i];
                        textBuilder.AppendLine($"Item #{i + 1}:");
                        textBuilder.AppendLine($"  Description: {item.Description} (Confidence: {item.DescriptionConfidence:P1})");
                        textBuilder.AppendLine($"  Quantity: {item.Quantity}");
                        textBuilder.AppendLine($"  Unit Price: {item.UnitPrice:C}");
                        textBuilder.AppendLine($"  Amount: {item.Amount:C} (Confidence: {item.AmountConfidence:P1})");
                        textBuilder.AppendLine();
                    }
                }
            }

            // Add detailed tables content
            if (extractionResult.Tables.Any())
            {
                textBuilder.AppendLine("=== EXTRACTED TABLES DATA ===");
                var tablesText = DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables);
                textBuilder.AppendLine(tablesText);
            }

            // Add raw document fields with confidence scores
            if (extractionResult.RawDocumentFields.Any())
            {
                textBuilder.AppendLine("=== RAW DOCUMENT FIELDS WITH CONFIDENCE ===");
                foreach (var field in extractionResult.RawDocumentFields.OrderByDescending(f => f.Value.Confidence))
                {
                    textBuilder.AppendLine($"{field.Key}: {field.Value.Value} (Type: {field.Value.FieldType}, Confidence: {field.Value.Confidence:P2})");
                }
                textBuilder.AppendLine();
            }

            var extractedText = textBuilder.ToString();
            _logger.LogInformation($"?? Prepared comprehensive text content with {extractedText.Length} characters");

            // Step 3: Process with ProcessDocumentDataAgent AI capabilities
            _logger.LogInformation("?? Step 3: Processing with AI Agent for enhanced analysis");

            // Use the existing ExtractDataFromTextAsync method for AI processing
            var agentResult = await ExtractDataFromTextAsync(extractedText);

            if (!agentResult.Success)
            {
                _logger.LogError($"? AI agent processing failed: {agentResult.ErrorMessage}");
                return new ProcessAiDocumentsResult
                {
                    Success = false,
                    ErrorMessage = $"AI agent processing failed: {agentResult.ErrorMessage}",
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ExtractionResult = extractionResult,
                    ProcessedText = extractedText,
                    ProcessedAt = DateTime.UtcNow
                };
            }

            _logger.LogInformation("? AI agent processing successful");

            // Step 4: También ejecutar análisis de factura especializado para obtener resultados mejorados
            _logger.LogInformation("?? Step 4: Running specialized invoice analysis");
            
            var specializedTablesText = extractionResult.Tables.Any() 
                ? DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables)
                : string.Empty;

            var invoiceAnalysisResult = await ExtractInvoiceDataAsync(extractedText, specializedTablesText);

            if (!invoiceAnalysisResult.Success)
            {
                _logger.LogWarning($"?? Specialized invoice analysis failed: {invoiceAnalysisResult.ErrorMessage}");
            }
            else
            {
                _logger.LogInformation("? Specialized invoice analysis successful");
            }

            // Step 5: Save combined results to Cosmos DB
            _logger.LogInformation("?? Step 5: Saving combined analysis results to Cosmos DB");
            try
            {
                var cosmosLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = cosmosLoggerFactory.CreateLogger<CosmosDbTwinProfileService>();
                var cosmosService = new CosmosDbTwinProfileService(cosmosLogger, _configuration);

                // Create the complete result first
                var completeResult = new ProcessAiDocumentsResult
                {
                    Success = true,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ExtractionResult = extractionResult,
                    AgentResult = agentResult,
                    InvoiceAnalysisResult = invoiceAnalysisResult.Success ? invoiceAnalysisResult : null,
                    ProcessedText = extractedText,
                    ProcessedAt = DateTime.UtcNow
                };

                // Save the COMPLETE ProcessAiDocumentsResult to Cosmos DB
                await SaveCompleteProcessAiDocumentsResultAsync(cosmosService, completeResult);
                _logger.LogInformation("? COMPLETE ProcessAiDocumentsResult saved to Cosmos DB successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "?? Failed to save complete analysis to Cosmos DB, but processing completed successfully");
            }

            // Step 6: Create comprehensive final result
            var result = new ProcessAiDocumentsResult
            {
                Success = true,
                ContainerName = containerName,
                FilePath = filePath,
                FileName = fileName,
                ExtractionResult = extractionResult,
                AgentResult = agentResult,
                InvoiceAnalysisResult = invoiceAnalysisResult.Success ? invoiceAnalysisResult : null,
                ProcessedText = extractedText,
                ProcessedAt = DateTime.UtcNow
            };

            _logger.LogInformation($"?? ProcessAiDocuments completed successfully for {fileName}");
            _logger.LogInformation($"?? Final statistics:");
            _logger.LogInformation($"   • Document pages: {extractionResult.TotalPages}");
            _logger.LogInformation($"   • Tables extracted: {extractionResult.Tables.Count}");
            _logger.LogInformation($"   • Fields extracted: {extractionResult.RawDocumentFields.Count}");
            _logger.LogInformation($"   • Line items: {extractionResult.InvoiceData.LineItems.Count}");
            _logger.LogInformation($"   • Invoice total: {extractionResult.InvoiceData.InvoiceTotal:C}");
            _logger.LogInformation($"   • AI analysis fields: {agentResult.ExtractedData?.Count ?? 0}");
            _logger.LogInformation($"   • Text processed: {extractedText.Length} characters");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error in ProcessAiDocuments for {containerName}/{filePath}/{fileName}");
            
            return new ProcessAiDocumentsResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ContainerName = containerName,
                FilePath = filePath,
                FileName = fileName,
                ProcessedAt = DateTime.UtcNow
            };
        }
    }
}

/// <summary>
/// Result of document data extraction
/// </summary>
public class DocumentExtractionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? ExtractedData { get; set; }
    public string? RawResponse { get; set; }
    public DocumentMetadata? Metadata { get; set; }
}

/// <summary>
/// Result of invoice-specific extraction
/// </summary>
public class InvoiceExtractionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string TextSummary { get; set; } = string.Empty;
    public string HtmlOutput { get; set; } = string.Empty;
    public Dictionary<string, object> StructuredData { get; set; } = new();
    public string TextReport { get; set; } = string.Empty;
    public string TablesContent { get; set; } = string.Empty;
    public string? RawResponse { get; set; }
    public DocumentMetadata? Metadata { get; set; }
}

/// <summary>
/// Metadata about the document processing
/// </summary>
public class DocumentMetadata
{
    public DateTime Timestamp { get; set; }
    public int InputLength { get; set; }
    public int OutputLength { get; set; }
    public string AgentModel { get; set; } = "Azure OpenAI";
    public bool ExtractionSchemaUsed { get; set; }
    public string? AnalysisType { get; set; }
    public string? ErrorDetails { get; set; }
}

/// <summary>
/// Result of ProcessAiDocuments operation combining Document Intelligence and AI Agent processing
/// </summary>
public class ProcessAiDocumentsResult
{
    /// <summary>
    /// Whether the processing was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Container name where the document was stored
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Path to the document within the container
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Original file name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Result from Azure Document Intelligence Service extraction
    /// </summary>
    public InvoiceAnalysisResult? ExtractionResult { get; set; }

    /// <summary>
    /// Result from AI Agent general processing
    /// </summary>
    public DocumentExtractionResult? AgentResult { get; set; }

    /// <summary>
    /// Result from specialized invoice analysis by AI Agent
    /// </summary>
    public InvoiceExtractionResult? InvoiceAnalysisResult { get; set; }

    /// <summary>
    /// The comprehensive processed text that was sent to the AI agent
    /// </summary>
    public string ProcessedText { get; set; } = string.Empty;

    /// <summary>
    /// When the processing was completed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Get the full path of the document
    /// </summary>
    public string FullPath => $"{ContainerName}/{FilePath}/{FileName}";

    /// <summary>
    /// Get a comprehensive summary of all processing results
    /// </summary>
    public string GetComprehensiveSummary()
    {
        if (!Success)
        {
            return $"? Processing failed: {ErrorMessage}";
        }

        var summary = new StringBuilder();
        summary.AppendLine($"?? Successfully processed: {FileName}");
        summary.AppendLine($"?? Location: {FullPath}");
        summary.AppendLine($"?? Processed: {ProcessedAt:yyyy-MM-dd HH:mm} UTC");
        summary.AppendLine();

        // Document Intelligence results
        if (ExtractionResult != null)
        {
            summary.AppendLine("?? Document Intelligence Analysis:");
            summary.AppendLine($"   • Pages: {ExtractionResult.TotalPages}");
            summary.AppendLine($"   • Tables: {ExtractionResult.Tables.Count}");
            summary.AppendLine($"   • Raw Fields: {ExtractionResult.RawDocumentFields.Count}");
            summary.AppendLine($"   • Line Items: {ExtractionResult.InvoiceData.LineItems.Count}");
            summary.AppendLine($"   • Invoice Total: {ExtractionResult.InvoiceData.InvoiceTotal:C}");
            summary.AppendLine($"   • Vendor: {ExtractionResult.InvoiceData.VendorName}");
            summary.AppendLine($"   • Customer: {ExtractionResult.InvoiceData.CustomerName}");
            summary.AppendLine();
        }

        // AI Agent general processing results
        if (AgentResult != null)
        {
            summary.AppendLine("?? AI Agent General Analysis:");
            summary.AppendLine($"   • Processing: {(AgentResult.Success ? "? Success" : "? Failed")}");
            summary.AppendLine($"   • Extracted Data Fields: {AgentResult.ExtractedData?.Count ?? 0}");
            if (AgentResult.Metadata != null)
            {
                summary.AppendLine($"   • Input Length: {AgentResult.Metadata.InputLength:N0} characters");
                summary.AppendLine($"   • Output Length: {AgentResult.Metadata.OutputLength:N0} characters");
                summary.AppendLine($"   • AI Model: {AgentResult.Metadata.AgentModel}");
            }
            summary.AppendLine();
        }

        // AI Agent specialized invoice analysis results
        if (InvoiceAnalysisResult != null)
        {
            summary.AppendLine("?? AI Agent Invoice Specialization:");
            summary.AppendLine($"   • Processing: {(InvoiceAnalysisResult.Success ? "? Success" : "? Failed")}");
            summary.AppendLine($"   • Text Summary: {(string.IsNullOrEmpty(InvoiceAnalysisResult.TextSummary) ? "? No" : "? Yes")}");
            summary.AppendLine($"   • HTML Output: {(string.IsNullOrEmpty(InvoiceAnalysisResult.HtmlOutput) ? "? No" : "? Yes")}");
            summary.AppendLine($"   • Structured Data: {(InvoiceAnalysisResult.StructuredData.Any() ? "? Yes" : "? No")} ({InvoiceAnalysisResult.StructuredData.Count} fields)");
            summary.AppendLine($"   • Text Report: {(string.IsNullOrEmpty(InvoiceAnalysisResult.TextReport) ? "? No" : "? Yes")}");
            summary.AppendLine($"   • Tables Content: {(string.IsNullOrEmpty(InvoiceAnalysisResult.TablesContent) ? "? No" : "? Yes")}");
            summary.AppendLine();
        }

        // Processing statistics
        summary.AppendLine("?? Processing Statistics:");
        summary.AppendLine($"   • Total Text Length: {ProcessedText.Length:N0} characters");
        
        if (ExtractionResult != null)
        {
            summary.AppendLine($"   • Document Confidence Scores:");
            summary.AppendLine($"     - Vendor Name: {ExtractionResult.InvoiceData.VendorNameConfidence:P1}");
            summary.AppendLine($"     - Customer Name: {ExtractionResult.InvoiceData.CustomerNameConfidence:P1}");
            summary.AppendLine($"     - Invoice Total: {ExtractionResult.InvoiceData.InvoiceTotalConfidence:P1}");
            summary.AppendLine($"     - SubTotal: {ExtractionResult.InvoiceData.SubTotalConfidence:P1}");
        }

        return summary.ToString();
    }

    /// <summary>
    /// Get key insights from the document processing
    /// </summary>
    public Dictionary<string, object> GetKeyInsights()
    {
        var insights = new Dictionary<string, object>
        {
            ["success"] = Success,
            ["fileName"] = FileName,
            ["fullPath"] = FullPath,
            ["processedAt"] = ProcessedAt
        };

        if (ExtractionResult != null)
        {
            insights["documentIntelligence"] = new
            {
                totalPages = ExtractionResult.TotalPages,
                tablesCount = ExtractionResult.Tables.Count,
                fieldsCount = ExtractionResult.RawDocumentFields.Count,
                lineItemsCount = ExtractionResult.InvoiceData.LineItems.Count,
                invoiceTotal = ExtractionResult.InvoiceData.InvoiceTotal,
                vendorName = ExtractionResult.InvoiceData.VendorName,
                customerName = ExtractionResult.InvoiceData.CustomerName,
                invoiceNumber = ExtractionResult.InvoiceData.InvoiceNumber,
                invoiceDate = ExtractionResult.InvoiceData.InvoiceDate
            };
        }

        if (AgentResult != null)
        {
            insights["aiAgentGeneral"] = new
            {
                success = AgentResult.Success,
                extractedFieldsCount = AgentResult.ExtractedData?.Count ?? 0,
                inputLength = AgentResult.Metadata?.InputLength ?? 0,
                outputLength = AgentResult.Metadata?.OutputLength ?? 0
            };
        }

        if (InvoiceAnalysisResult != null)
        {
            insights["aiAgentInvoice"] = new
            {
                success = InvoiceAnalysisResult.Success,
                hasTextSummary = !string.IsNullOrEmpty(InvoiceAnalysisResult.TextSummary),
                hasHtmlOutput = !string.IsNullOrEmpty(InvoiceAnalysisResult.HtmlOutput),
                structuredDataFields = InvoiceAnalysisResult.StructuredData.Count,
                hasTextReport = !string.IsNullOrEmpty(InvoiceAnalysisResult.TextReport),
                hasTablesContent = !string.IsNullOrEmpty(InvoiceAnalysisResult.TablesContent)
            };
        }

        insights["processingStats"] = new
        {
            textLength = ProcessedText.Length,
            processingSteps = GetProcessingStepsCompleted()
        };

        return insights;
    }

    /// <summary>
    /// Get the number of processing steps that were completed successfully
    /// </summary>
    private int GetProcessingStepsCompleted()
    {
        int completedSteps = 0;

        if (ExtractionResult?.Success == true) completedSteps++; // Document Intelligence
        if (AgentResult?.Success == true) completedSteps++; // AI General Processing
        if (InvoiceAnalysisResult?.Success == true) completedSteps++; // AI Invoice Specialization
        
        return completedSteps;
    }

    /// <summary>
    /// Check if all processing steps were successful
    /// </summary>
    public bool IsFullyProcessed => Success && 
                                   ExtractionResult?.Success == true && 
                                   AgentResult?.Success == true && 
                                   (InvoiceAnalysisResult?.Success == true || InvoiceAnalysisResult == null);
}