using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwinAgentsLibrary.Models;
using TwinFx.Models;
using TwinFx.Services;
using TwinFxTests.Services;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Agents
{
    public class ProcessDocumentDataAgent
    {
        private readonly ILogger<ProcessDocumentDataAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly Kernel _kernel;

        public ProcessDocumentDataAgent(ILogger<ProcessDocumentDataAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("✅ DocumentIntelligenceService initialized successfully");

                // Initialize Semantic Kernel for AI processing
                var builder = Kernel.CreateBuilder();
                
                // Add Azure OpenAI chat completion
                var endpoint = configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                var apiKey = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                var deploymentName = configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4";
                deploymentName = "gpt-5-mini";

                // Create HttpClient with extended timeout for long document processing operations
                var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
                
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: deploymentName,
                    endpoint: endpoint,
                    apiKey: apiKey,
                    httpClient: httpClient);
                    
                _kernel = builder.Build();
                _logger.LogInformation("✅ Semantic Kernel initialized successfully with 20-minute timeout");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize ProcessDocumentDataAgent");
                throw;
            }
        }

        public async Task<ProcessAiDocumentsResult> ProcessAiDocuments(string containerName,
            string filePath, string fileName, string documentType = "Invoice", string? educationId = null)
        {
            _logger.LogInformation("🚀 Starting ProcessAiDocuments for {DocumentType} document: {FileName}", documentType, fileName);

            if (documentType.ToUpperInvariant() == "EDUCATION" && !string.IsNullOrEmpty(educationId))
            {
                _logger.LogInformation("🎓 Processing Education document for education record: {EducationId}", educationId);
            }

            try
            {
                // Step 1: Extract data using Document Intelligence
                _logger.LogInformation("📄 Step 1: Extracting data with Document Intelligence...");
                
                InvoiceAnalysisResult? extractionResult = null;
                EducationAnalysisResult? educationResult = null;
                DocumentAnalysisResult? generalResult = null;
                string processedText = "";

                DocumentExtractionResult? agentResult = null;
                InvoiceExtractionResult? invoiceAnalysisResult = null;
                switch (documentType.ToUpperInvariant())
                {
                    case "INVOICE":
                    case "FACTURA":
                        extractionResult = await _documentIntelligenceService.ExtractInvoiceDataAsync(containerName, filePath, fileName);
                        if (extractionResult.Success)
                        {
                            processedText = BuildInvoiceText(extractionResult);
                            _logger.LogInformation("✅ Invoice extraction completed successfully");
                        }
                        break;

                    case "EDUCATION":
                        educationResult = await _documentIntelligenceService.ExtractEducationDataAsync(containerName, filePath, fileName);
                        if (educationResult.Success)
                        {
                            processedText = educationResult.TextContent;
                            _logger.LogInformation("✅ Education extraction completed successfully");
                            
                            // If we have an educationId, save the analysis to that education record
                            if (!string.IsNullOrEmpty(educationId))
                            {
                                _logger.LogInformation("💾 Saving education analysis to record: {EducationId}", educationId);
                                try
                                {
                                    agentResult = await ProcessDocumentWithAI(processedText, documentType, educationId);
                                    var cosmosLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                                    var cosmosLogger = cosmosLoggerFactory.CreateLogger<CosmosDbService>();
                                    var cosmosService = _configuration.CreateCosmosService(cosmosLogger);

                                    var saved = await cosmosService.SaveEducationAnalysisAsync(agentResult, educationId, containerName, fileName, filePath);
                                    if (saved)
                                    {
                                        _logger.LogInformation("✅ Education analysis saved to Cosmos DB successfully");
                                    }
                                    else
                                    {
                                        _logger.LogWarning("⚠️ Failed to save education analysis to Cosmos DB");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "❌ Error saving education analysis to Cosmos DB");
                                }
                            }
                        }
                        break;

                    default:
                        // Use general document analysis
                        var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                        var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync($"{filePath}/{fileName}", TimeSpan.FromHours(1));
                        
                        if (!string.IsNullOrEmpty(sasUrl))
                        {
                            generalResult = await _documentIntelligenceService.AnalyzeDocumentAsync(sasUrl);
                            if (generalResult.Success)
                            {
                                processedText = generalResult.TextContent;
                                _logger.LogInformation("✅ General document extraction completed successfully");
                            }
                        }
                        break;
                }

                // Step 2: Process with AI for enhanced analysis
                _logger.LogInformation("🤖 Step 2: Processing with AI for enhanced analysis...");
                 

                if (!string.IsNullOrEmpty(processedText))
                {
                    if (documentType.ToUpperInvariant() == "INVOICE" || documentType.ToUpperInvariant() == "FACTURA")
                    {
                        invoiceAnalysisResult = await ProcessInvoiceWithAIFull(
                            fileName, filePath,
                             processedText,
                            containerName,
                             extractionResult);
                        _logger.LogInformation("✅ AI invoice analysis completed");
                    }
                    else
                    {
                        
                        _logger.LogInformation("✅ AI document analysis completed");
                    }
                }

                // Build comprehensive result
                var result = new ProcessAiDocumentsResult
                {
                    Success = true,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedText = processedText,
                    ProcessedAt = DateTime.UtcNow,
                    ExtractionResult = extractionResult,
                    AgentResult = agentResult,
                    InvoiceAnalysisResult = invoiceAnalysisResult
                };

                _logger.LogInformation("✅ ProcessAiDocuments completed successfully for {DocumentType}: {FileName}", documentType, fileName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing document {FileName} of type {DocumentType}", fileName, documentType);
                
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

        private async Task<InvoiceExtractionResult> ProcessInvoiceWithAI(string extractedText, InvoiceAnalysisResult? extractionResult)
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var prompt = $@"
Analiza esta factura y proporciona un resumen ejecutivo detallado en español.

DATOS EXTRAÍDOS:
{extractedText}

DATOS ESTRUCTURADOS:
{(extractionResult != null ? JsonSerializer.Serialize(extractionResult.InvoiceData, new JsonSerializerOptions { WriteIndented = true }) : "No disponible")}

TABLAS:
{(extractionResult?.Tables != null ? DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables) : "No hay tablas")}

Proporciona:
1. Resumen ejecutivo en HTML
2. Resumen en texto plano
3. Análisis de líneas de productos
4. Identificación de impuestos y totales
5. Recomendaciones o alertas si hay discrepancias
6. NUnca inicies con ```json ni termines con ``` IMPORTANTE
Formato de respuesta en JSON:
{{
    ""textSummary"": ""resumen en texto plano"",
    ""htmlOutput"": ""<div>resumen ejecutivo en HTML</div>"",
    ""structuredData"": {{
        ""vendorAnalysis"": ""análisis del proveedor"",
        ""customerAnalysis"": ""análisis del cliente"",
        ""financialSummary"": ""resumen financiero"",
        ""lineItemsAnalysis"": ""análisis de productos/servicios"",
        ""taxAnalysis"": ""análisis de impuestos"",
        ""recommendations"": ""recomendaciones""
    }},
    ""textReport"": ""reporte detallado en texto"",
    ""tablesContent"": ""análisis de tablas encontradas""
}}";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";
                
                // Try to parse AI response
                var aiData = JsonSerializer.Deserialize<Dictionary<string, object>>(aiResponse);

                return new InvoiceExtractionResult
                {
                    Success = true,
                    TextSummary = aiData?.GetValueOrDefault("textSummary")?.ToString() ?? "",
                    HtmlOutput = aiData?.GetValueOrDefault("htmlOutput")?.ToString() ?? "",
                    StructuredData = aiData?.GetValueOrDefault("structuredData") as Dictionary<string, object> ?? new(),
                    TextReport = aiData?.GetValueOrDefault("textReport")?.ToString() ?? "",
                    TablesContent = aiData?.GetValueOrDefault("tablesContent")?.ToString() ?? "",
                    RawResponse = aiResponse,
                    Metadata = new DocumentMetadataInvoices
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = extractedText.Length,
                        OutputLength = aiResponse.Length,
                        AgentModel = "Azure OpenAI",
                        AnalysisType = "Invoice",
                        ExtractionSchemaUsed = true
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in AI invoice processing");
                return new InvoiceExtractionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Metadata = new DocumentMetadataInvoices
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = 0,
                        OutputLength = 0,
                        AgentModel = "Azure OpenAI",
                        AnalysisType = "Invoice",
                        ExtractionSchemaUsed = false
                    }
                };
            }
        }

        private async Task<InvoiceExtractionResult> ProcessInvoiceWithAIFull(
            string fileName, string filePath,
            string extractedText,
            string TwinID,
            InvoiceAnalysisResult? extractionResult)
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var prompt = $@"
Analiza esta factura y crea un documento InvoiceDocument completo en JSON.

DATOS EXTRAÍDOS:
{extractedText}

DATOS ESTRUCTURADOS:
{(extractionResult != null ? JsonSerializer.Serialize(extractionResult.InvoiceData, new JsonSerializerOptions { WriteIndented = true }) : "No disponible")}

TABLAS:
{(extractionResult?.Tables != null ? DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables) : "No hay tablas")}

INSTRUCCIONES IMPORTANTES:
1. Genera un JSON completo que represente un InvoiceDocument
2. Incluye TODOS los campos necesarios para la estructura InvoiceDocument
3. Asegúrate de incluir TODOS los LineItems encontrados (sin límite de 10)
4. Calcula correctamente todos los totales y subtotales
5. Incluye análisis de AI mejorado en los campos correspondientes
6. Nunca inicies con ```json ni termines con ``` IMPORTANTE
7. El JSON debe ser válido y completo 

9. IMPORTANT: Use the language of the Invoice to generate the data do not translate to spanish or other languages.
10 

ESTRUCTURA REQUERIDA del InvoiceDocument:
{{
    ""id"": ""ID único del documento"",
    ""twinID"": ""ID del Twin"",
    ""fileName"": ""nombre del archivo"",
    ""filePath"": ""ruta del archivo"",
    ""createdAt"": ""fecha de creación ISO 8601"",
    ""source"": ""Document Intelligence"",
    ""processedAt"": ""fecha de procesamiento ISO 8601"",
    ""success"": true,
    ""errorMessage"": null,
    ""totalPages"": número de páginas,
    
    ""vendorName"": ""nombre del proveedor"",
    ""vendorNameConfidence"": nivel de confianza (0-1),
    ""customerName"": ""nombre del cliente"",
    ""customerNameConfidence"": nivel de confianza (0-1),
    ""invoiceNumber"": ""número de factura"",
    ""invoiceDate"": ""fecha de factura ISO 8601"",
    ""dueDate"": ""fecha de vencimiento ISO 8601"",
    ""subTotal"": monto subtotal,
    ""subTotalConfidence"": nivel de confianza,
    ""totalTax"": total de impuestos,
    ""invoiceTotal"": total de la factura,
    ""invoiceTotalConfidence"": nivel de confianza,
    ""lineItemsCount"": número de line items,
    ""tablesCount"": número de tablas,
    ""rawFieldsCount"": número de campos raw,
    
    ""invoiceData"": {{
        ""vendorName"": ""nombre del proveedor"",
        ""vendorNameConfidence"": nivel de confianza,
        ""vendorAddress"": ""dirección del proveedor"",
        ""customerName"": ""nombre del cliente"",
        ""customerNameConfidence"": nivel de confianza,
        ""customerAddress"": ""dirección del cliente"",
        ""invoiceNumber"": ""número de factura"",
        ""invoiceDate"": ""fecha de factura ISO 8601"",
        ""dueDate"": ""fecha de vencimiento ISO 8601"",
        ""subTotal"": monto subtotal,
        ""subTotalConfidence"": nivel de confianza,
        ""totalTax"": total de impuestos,
        ""invoiceTotal"": total de la factura,
        ""invoiceTotalConfidence"": nivel de confianza,
        ""lineItems"": [
            {{
                ""description"": ""descripción del item"",
                ""descriptionConfidence"": nivel de confianza,
                ""quantity"": cantidad,
                ""unitPrice"": precio unitario,
                ""amount"": monto del item,
                ""amountConfidence"": nivel de confianza
            }}
        ]
    }},
    
    ""aiExecutiveSummaryHtml"": ""<div>Resumen ejecutivo en HTML elegante con colores y formato</div>"",
    ""aiExecutiveSummaryText"": ""Resumen ejecutivo en texto plano"",
    ""aiTextSummary"": ""Resumen de la factura"",
    ""aiTextReport"": ""Detalle Invoice"",   
    ""aiTablesContent"": ""Análisis de las tablas encontradas"",
    ""aiStructuredData"": ""Datos estructurados adicionales"",
    ""aiProcessedText"": ""Texto procesado por AI"",
    ""aiCompleteSummary"": ""Resumen completo del análisis"",
    ""aiCompleteInsights"": ""Insights completos sobre la factura""
}}

IMPORTANT :
In  the  ""aiHtmlOutput"": "" "", and ""aiTextReport"": "" ""

I need yu to set a full description of what you found in the invoice here = aiTextReport.
Include information that is critical to know like totals, taxes, line items, vendor and customer information.
The  aiTextReport mowst have all the detail, use nice sentences to epxlain the content,
use titles, start with a summary, use bulets, etc. OD NOT USE HTML keep it simple

Validate the JSON structure before sending it to me.
Genera el JSON completo del InvoiceDocument sin usar marcadores de código.";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";
                
                // Clean up any potential markdown formatting
                if (aiResponse.StartsWith("```json"))
                {
                    aiResponse = aiResponse.Substring(7).Trim();
                }
                if (aiResponse.StartsWith("```"))
                {
                    aiResponse = aiResponse.Substring(3).Trim();
                }
                if (aiResponse.EndsWith("```"))
                {
                    aiResponse = aiResponse.Substring(0, aiResponse.Length - 3).Trim();
                }


                var InvoiceExtracted = JsonConvert.DeserializeObject<InvoiceDocument>(aiResponse);

                InvoiceExtracted.TwinID = TwinID;
                InvoiceExtracted.ProcessedAt = DateTime.UtcNow;
                InvoiceExtracted.id = Guid.NewGuid().ToString();

                // Extract clean text from AiHtmlOutput and set it to AiTextReport
                if (!string.IsNullOrEmpty(InvoiceExtracted.AiHtmlOutput))
                {
                    InvoiceExtracted.AiTextReport = StripHtmlTags(InvoiceExtracted.AiHtmlOutput);
                    _logger.LogInformation("✅ HTML content converted to clean text for AiTextReport");
                }

                // Save InvoiceDocument to CosmosDB using UnStructuredDocumentsCosmosDB
                try
                {
                    _logger.LogInformation("💾 Saving InvoiceDocument to CosmosDB - FileName: {FileName}, TwinID: {TwinID}", 
                        InvoiceExtracted.FileName, InvoiceExtracted.TwinID);

                    var cosmosLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                    var unstructuredCosmosLogger = cosmosLoggerFactory.CreateLogger<UnStructuredDocumentsCosmosDB>();
                    var unstructuredCosmosService = new UnStructuredDocumentsCosmosDB(unstructuredCosmosLogger, _configuration);
                    InvoiceExtracted.FileName = fileName;
                    InvoiceExtracted.FilePath = filePath;
                    var saved = await unstructuredCosmosService.SaveInvoiceDocumentAsync(InvoiceExtracted);
                    if (saved)
                    {
                        _logger.LogInformation("✅ InvoiceDocument saved to CosmosDB successfully - DocumentId: {DocumentId}", InvoiceExtracted.id);
                        
                        // Upload to Semistructured Search Index after successful Cosmos DB save
                        try
                        {
                            _logger.LogInformation("🔍 Uploading document to Semistructured Search Index - DocumentId: {DocumentId}", InvoiceExtracted.id);
                            
                            // Create semistructured index service
                            var semistructuredLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                            var semistructuredLogger = semistructuredLoggerFactory.CreateLogger<TwinFxTests.Services.Semistructured_Index>();
                            var semistructuredIndexService = new TwinFxTests.Services.Semistructured_Index(semistructuredLogger, _configuration);
                            
                            // Create SemistructuredDocument from invoice data
                            var semistructuredDocument = new  SemistructuredDocument
                            {
                                Id = InvoiceExtracted.id,
                                TwinId = TwinID,
                                DocumentType = "Invoice",
                                FileName = fileName,
                                ProcessedAt = DateTimeOffset.UtcNow,
                                ReporteTextoPlano = InvoiceExtracted.AiTextReport ?? extractedText,
                                FilePath = filePath,
                                ContainerName = TwinID,
                                ProcessingStatus = "completed",
                                Metadata = new Dictionary<string, object>
                                {
                                    ["invoiceNumber"] = InvoiceExtracted.InvoiceNumber ?? "",
                                    ["vendorName"] = InvoiceExtracted.VendorName ?? "",
                                    ["customerName"] = InvoiceExtracted.CustomerName ?? "",
                                    ["invoiceTotal"] = InvoiceExtracted.InvoiceTotal,
                                    ["invoiceDate"] = InvoiceExtracted.InvoiceDate.ToString("yyyy-MM-dd"),
                                    ["source"] = "Document Intelligence + AI Processing",
                                    ["aiProcessed"] = true
                                }
                            };
                            
                            // Upload to semistructured index
                            var uploadResult = await semistructuredIndexService.UploadDocumentToIndexAsync(semistructuredDocument);
                            
                            if (uploadResult.Success)
                            {
                                _logger.LogInformation("✅ Document uploaded to Semistructured Search Index successfully - DocumentId: {DocumentId}, HasVectorEmbeddings: {HasVectorEmbeddings}, VectorDimensions: {VectorDimensions}", 
                                    uploadResult.DocumentId, uploadResult.HasVectorEmbeddings, uploadResult.VectorDimensions);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Failed to upload document to Semistructured Search Index - DocumentId: {DocumentId}, Error: {Error}", 
                                    InvoiceExtracted.id, uploadResult.Error);
                            }
                        }
                        catch (Exception indexEx)
                        {
                            _logger.LogError(indexEx, "❌ Error uploading document to Semistructured Search Index - DocumentId: {DocumentId}", InvoiceExtracted.id);
                            // Continue with the response even if indexing fails
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to save InvoiceDocument to CosmosDB - DocumentId: {DocumentId}", InvoiceExtracted.id);
                    }
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "❌ Error saving InvoiceDocument to CosmosDB - DocumentId: {DocumentId}", InvoiceExtracted.id);
                    // Continue with the response even if saving fails
                }

                return new InvoiceExtractionResult
                {
                    Success = true,
                    TextSummary = "InvoiceDocument completo generado por AI",
                    HtmlOutput = "<div>InvoiceDocument JSON generado exitosamente</div>",
                    StructuredData = new Dictionary<string, object>
                    {
                        ["invoiceDocumentJson"] = aiResponse
                    },
                    TextReport = "Documento InvoiceDocument completo creado con todos los campos requeridos",
                    TablesContent = extractionResult?.Tables != null ? DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables) : "",
                    RawResponse = aiResponse,
                    Metadata = new DocumentMetadataInvoices
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = extractedText.Length,
                        OutputLength = aiResponse.Length,
                        AgentModel = "Azure OpenAI",
                        AnalysisType = "InvoiceDocument Full",
                        ExtractionSchemaUsed = true
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in AI invoice full processing");
                return new InvoiceExtractionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Metadata = new DocumentMetadataInvoices
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = 0,
                        OutputLength = 0,
                        AgentModel = "Azure OpenAI",
                        AnalysisType = "InvoiceDocument Full",
                        ExtractionSchemaUsed = false
                    }
                };
            }
        }

        private async Task<DocumentExtractionResult> ProcessDocumentWithAI(string extractedText, string documentType, string? educationId = null)
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var educationContext = !string.IsNullOrEmpty(educationId) && documentType.ToUpperInvariant() == "EDUCATION" 
                    ? $"\n\nIMPORTANTE: Este documento debe actualizarse en el registro de educación con ID: {educationId}"
                    : "";

                                    var prompt = $@"
                    Analiza este documento de tipo '{documentType}' y extrae información estructurada relevante.
                    En tu respuesta Nunca comiences con ```json ni termines con ``` IMPORTANTE
                    CONTENIDO DEL DOCUMENTO:
                    {extractedText}
                    {educationContext}

                    Proporciona un análisis completo incluyendo:
                    Este documento ocntiene informacion relevante para el perfil educativo. 
                    Este puede ser :
                    1. Certificado de estudios    
                    2. Título académico
                    3. Diplomas
                    4. Transcripciones académicas
                    5. Cartas de recomendación
                    6. Premios y reconocimientos
                    7. Otros documentos relacionados con la educación
                    8. Resumen ejecutivo
                    9. Nunca empieces con ```json
                    10. Nunca termines con ```

    
                    Formato de respuesta en JSON: Nunca comiences con ```json ni termines con ``` IMPORTANTE
                    {{
                        ""documentType"": ""{documentType}"",
                        ""educationId"": ""{educationId}"", 
                        ""resumenEjecutivo"": ""resumen ejecutivo en español"",
                        ""DocumentHTMLContent"": ""contenido HTML completo en forma colores, listas grids no omitas nada del documento"", 
                        ""DocumentTextContent"": ""contenido de texto completo no omitas nada del documento"",
                    }}";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";

                aiResponse = aiResponse.Trim().Trim('`'); // Remove any surrounding backticks
                aiResponse = aiResponse.Replace("```json", "", StringComparison.OrdinalIgnoreCase).Trim(); // Remove any leading 'json' text

                // Try to parse AI response
                var aiData = JsonSerializer.Deserialize<Dictionary<string, object>>(aiResponse);

                return new DocumentExtractionResult
                {
                    Success = true,
                    ExtractedData = aiData?.GetValueOrDefault("extractedData") as Dictionary<string, object> ?? new(),
                    RawResponse = aiResponse,
                    Metadata = new DocumentMetadata
                    {
                        DocumentType = documentType,
                        EducationId = educationId,
                        ResumenEjecutivo = aiData?.GetValueOrDefault("resumenEjecutivo")?.ToString() ?? "",
                        DocumentHTMLContent = aiData?.GetValueOrDefault("DocumentHTMLContent")?.ToString() ?? "",
                        DocumentTextContent = aiData?.GetValueOrDefault("DocumentTextContent")?.ToString() ?? ""
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in AI document processing");
                return new DocumentExtractionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Metadata = new DocumentMetadata
                    {
                      Timestamp = DateTime.UtcNow,
                    }
                };
            }
        }

        private string BuildInvoiceText(InvoiceAnalysisResult extractionResult)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("=== INVOICE ANALYSIS RESULT ===");
            sb.AppendLine($"Processed At: {extractionResult.ProcessedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Pages: {extractionResult.TotalPages}");
            sb.AppendLine();

            var invoice = extractionResult.InvoiceData;
            
            sb.AppendLine("VENDOR INFORMATION:");
            sb.AppendLine($"  Name: {invoice.VendorName} (Confidence: {invoice.VendorNameConfidence:P1})");
            sb.AppendLine($"  Address: {invoice.VendorAddress}");
            sb.AppendLine();

            sb.AppendLine("CUSTOMER INFORMATION:");
            sb.AppendLine($"  Name: {invoice.CustomerName} (Confidence: {invoice.CustomerNameConfidence:P1})");
            sb.AppendLine($"  Address: {invoice.CustomerAddress}");
            sb.AppendLine();

            sb.AppendLine("INVOICE DETAILS:");
            sb.AppendLine($"  Number: {invoice.InvoiceNumber}");
            sb.AppendLine($"  Date: {invoice.InvoiceDate:yyyy-MM-dd}");
            sb.AppendLine($"  Due Date: {invoice.DueDate:yyyy-MM-dd}");
            sb.AppendLine();

            sb.AppendLine("FINANCIAL SUMMARY:");
            sb.AppendLine($"  Subtotal: ${invoice.SubTotal:F2} (Confidence: {invoice.SubTotalConfidence:P1})");
            sb.AppendLine($"  Total Tax: ${invoice.TotalTax:F2}");
            sb.AppendLine($"  Total: ${invoice.InvoiceTotal:F2} (Confidence: {invoice.InvoiceTotalConfidence:P1})");
            sb.AppendLine();

            if (invoice.LineItems.Count > 0)
            {
                sb.AppendLine($"LINE ITEMS ({invoice.LineItems.Count}):");
                foreach (var item in invoice.LineItems)
                {
                    sb.AppendLine($"  • {item.Description} - Qty: {item.Quantity}, Unit: ${item.UnitPrice:F2}, Amount: ${item.Amount:F2}");
                }
                sb.AppendLine();
            }

            if (extractionResult.Tables.Count > 0)
            {
                sb.AppendLine("TABLES FOUND:");
                sb.AppendLine(DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Strip HTML tags from text and return clean text content
        /// </summary>
        /// <param name="htmlContent">HTML content to clean</param>
        /// <returns>Clean text without HTML tags</returns>
        private string StripHtmlTags(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return string.Empty;

            try
            {
                // Remove HTML tags using regex
                var htmlTagPattern = @"<[^>]+>";
                var withoutTags = Regex.Replace(htmlContent, htmlTagPattern, " ");

                // Decode common HTML entities
                var decoded = withoutTags
                    .Replace("&nbsp;", " ")
                    .Replace("&amp;", "&")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&quot;", "\"")
                    .Replace("&#39;", "'")
                    .Replace("&apos;", "'");

                // Clean up whitespace - replace multiple spaces, tabs, and newlines with single spaces
                var cleanedText = Regex.Replace(decoded, @"\s+", " ");

                // Trim and return
                return cleanedText.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error stripping HTML tags, returning original content");
                return htmlContent;
            }
        }
    }

    public class ProcessAiDocumentsResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public InvoiceAnalysisResult? ExtractionResult { get; set; }
        public DocumentExtractionResult? AgentResult { get; set; }
        public InvoiceExtractionResult? InvoiceAnalysisResult { get; set; }
        public string ProcessedText { get; set; } = string.Empty;
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
                return $"❌ Processing failed: {ErrorMessage}";
            }

            return $"✅ Successfully processed: {FileName}\n📍 Location: {FullPath}\n📅 Processed: {ProcessedAt:yyyy-MM-dd HH:mm} UTC\n📝 Text Length: {ProcessedText.Length} characters";
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
                ["processedAt"] = ProcessedAt,
                ["textLength"] = ProcessedText.Length
            };

            if (ExtractionResult != null)
            {
                insights["documentIntelligence"] = new
                {
                    success = ExtractionResult.Success,
                    totalPages = ExtractionResult.TotalPages
                };
            }

            return insights;
        }
    }

    public class DocumentExtractionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object>? ExtractedData { get; set; }
        public string? RawResponse { get; set; }
        public DocumentMetadata? Metadata { get; set; }
    }

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
        public DocumentMetadataInvoices? Metadata { get; set; }
    }

    public class DocumentMetadata
    {
        public DateTime Timestamp { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string? EducationId { get; set; }
        public string ResumenEjecutivo { get; set; } = string.Empty;
        public string DocumentHTMLContent { get; set; } = string.Empty;
        public string DocumentTextContent { get; set; } = string.Empty;
    }

    public class DocumentMetadataInvoices
    {
        public DateTime Timestamp { get; set; }
        public int InputLength { get; set; }
        public int OutputLength { get; set; }
        public string AgentModel { get; set; } = "Azure OpenAI";
        public string AnalysisType { get; set; } = string.Empty;
        public bool ExtractionSchemaUsed { get; set; }
    }
}