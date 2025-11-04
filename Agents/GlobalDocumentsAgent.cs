using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Services;

namespace TwinFx.Agents
{
    /// <summary>
    /// Agent for processing global unstructured documents like contracts, policies, 
    /// procedures, auto insurance, home insurance, etc.
    /// </summary>
    public class GlobalDocumentsAgent
    {
        private readonly ILogger<GlobalDocumentsAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly Kernel _kernel;

        public GlobalDocumentsAgent(ILogger<GlobalDocumentsAgent> logger, IConfiguration configuration)
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

                builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
                _kernel = builder.Build();
                _logger.LogInformation("✅ Semantic Kernel initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize GlobalDocumentsAgent");
                throw;
            }
        }

        /// <summary>
        /// Process global unstructured documents through complete pipeline:
        /// 1. Get SAS URL for the document
        /// 2. Extract data using Document Intelligence  
        /// 3. Structure data by pages and tables using AI
        /// 4. Return comprehensive analysis result
        /// </summary>
        /// <param name="containerName">DataLake container name (twinId)</param>
        /// <param name="filePath">Path within the container</param>
        /// <param name="fileName">Document file name</param>
        /// <param name="documentType">Type of document (CONTRACTS, POLICIES, etc.)</param>
        /// <param name="category">Document category</param>
        /// <param name="description">Document description</param>
        /// <returns>Structured global document analysis result</returns>
        public async Task<GlobalDocumentResult> ProcessGlobalDocumentAsync(
            string containerName, 
            string filePath, 
            string fileName,
            string documentType = "GLOBAL_DOCUMENT", 
            string category = "GENERAL",
            string description = "")
        {
            _logger.LogInformation("🌐 Starting Global Document Processing for: {FileName}", fileName);
            _logger.LogInformation("📋 Document Type: {DocumentType}, Category: {Category}", documentType, category);

            var result = new GlobalDocumentResult
            {
                Success = false,
                ContainerName = containerName,
                FilePath = filePath,
                FileName = fileName,
                DocumentType = documentType,
                Category = category,
                Description = description,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                // STEP 1: Generate SAS URL for Document Intelligence access
                _logger.LogInformation("📤 STEP 1: Generating SAS URL for document access...");
                
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                var fullFilePath = $"{filePath}/{fileName}";
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(2));

                if (string.IsNullOrEmpty(sasUrl))
                {
                    result.ErrorMessage = "Failed to generate SAS URL for document access";
                    _logger.LogError("❌ Failed to generate SAS URL for: {FullFilePath}", fullFilePath);
                    return result;
                }

                result.DocumentUrl = sasUrl;
                _logger.LogInformation("✅ SAS URL generated successfully");

                // STEP 2: Extract data using Document Intelligence
                _logger.LogInformation("📄 STEP 2: Extracting data with Document Intelligence...");
                
                var documentAnalysis = await _documentIntelligenceService.AnalyzeDocumentAsync(sasUrl);
                
                if (!documentAnalysis.Success)
                {
                    result.ErrorMessage = $"Document Intelligence extraction failed: {documentAnalysis.ErrorMessage}";
                    _logger.LogError("❌ Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                    return result;
                }

                result.TextContent = documentAnalysis.TextContent;
                result.TotalPages = documentAnalysis.TotalPages;
                _logger.LogInformation("✅ Document Intelligence extraction completed - {Pages} pages, {TextLength} chars", 
                    documentAnalysis.TotalPages, documentAnalysis.TextContent.Length);

                // STEP 3: Structure data by pages and tables using AI
                _logger.LogInformation("🤖 STEP 3: Structuring data by pages and tables with AI...");
                
                var structuredData = await ProcessDocumentWithAI(documentAnalysis, documentType, category, description);
                
                if (!structuredData.Success)
                {
                    result.ErrorMessage = $"AI processing failed: {structuredData.ErrorMessage}";
                    _logger.LogError("❌ AI processing failed: {Error}", structuredData.ErrorMessage);
                    return result;
                }

                // Populate result with structured data
                result.Success = true;
                result.StructuredData = structuredData;
                result.PageData = structuredData.PageData;
                result.TableData = structuredData.TableData;
                result.ExecutiveSummary = structuredData.ExecutiveSummary;
                result.KeyInsights = structuredData.KeyInsights;

                // STEP 4: Save to Cosmos DB TwinSemiStructured container
                _logger.LogInformation("💾 STEP 4: Saving document to Cosmos DB TwinSemiStructured...");
                
                var cosmosSuccess = await SaveGlobalDocumentToCosmosAsync(result, structuredData);
                if (cosmosSuccess)
                {
                    _logger.LogInformation("✅ Document saved successfully to Cosmos DB");
                    result.CosmosDbSaved = true;
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to save document to Cosmos DB, but processing completed");
                    result.CosmosDbSaved = false;
                }

                _logger.LogInformation("✅ Global document processing completed successfully");
                _logger.LogInformation("📊 Results: {Pages} pages, {Tables} tables, {KeyInsights} insights", 
                    result.PageData.Count, result.TableData.Count, result.KeyInsights.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing global document {FileName}", fileName);
                
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Process document with AI to structure data by pages and tables
        /// </summary>
        private async Task<GlobalDocumentStructuredData> ProcessDocumentWithAI(
            DocumentAnalysisResult documentAnalysis, 
            string documentType, 
            string category, 
            string description)
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var prompt = $@"
Analiza este documento global de tipo '{documentType}' y categoría '{category}' y extrae información estructurada por páginas y tablas.

DESCRIPCIÓN DEL DOCUMENTO: {description}

CONTENIDO COMPLETO DEL DOCUMENTO:
{documentAnalysis.TextContent}

TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}

INSTRUCCIONES ESPECÍFICAS:
1. Extrae el contenido organizándolo por páginas numeradas
2. Identifica y extrae todas las tablas con su contenido estructurado
3. Genera un resumen ejecutivo comprensivo
4. Extrae insights clave relevantes para el tipo de documento
5. Para CONTRATOS: identifica partes, términos, fechas, montos
6. Para PÓLIZAS: identifica coberturas, primas, beneficiarios, vigencia
7. Para PROCEDIMIENTOS: identifica pasos, responsables, requisitos
8. Para SEGUROS: identifica asegurado, coberturas, deducibles, vigencia

IMPORTANTE: Responde ÚNICAMENTE en formato JSON válido, sin markdown:
IMPORTANTE: Convierte el 'pageData' a un HTML completo que incluya todo el contenido, tablas, grids, colores, etc. A continuación, se presenta un ejemplo de cómo debería estructurarse el HTML utilizando los datos proporcionados en el JSON. Asegúrate de incluir estilos y formatos adecuados para que el documento sea visualmente atractivo y fácil de leer."",
  ""exampleHTML"": ""<!DOCTYPE html>\n<html lang=\""es\"">\n<head>\n    <meta charset=\""UTF-8\"">\n    <meta name=\""viewport\"" content=\""width=device-width, initial-scale=1.0\"">\n    <title>Documento</title>\n    <style>\n        body {{ font-family: Arial, sans-serif; margin: 20px; }}\n        h1 {{ color: #2c3e50; }}\n        h2 {{ color: #2980b9; }}\n        table {{ width: 100%; border-collapse: collapse; margin: 20px 0; }}\n        th, td {{ border: 1px solid #bdc3c7; padding: 10px; text-align: left; }}\n        th {{ background-color: #ecf0f1; }}\n        .key-points {{ background-color: #f9e79f; padding: 10px; margin: 10px 0; }}\n        .insight {{ border: 1px solid #e74c3c; padding: 10px; margin: 10px 0; }}\n    </style>\n</head>\n<body>\n    <h1>{{{{documentType}}}}</h1>\n    <h2>Categoría: {{{{category}}}}</h2>\n    <p>{{{{description}}}}</p>\n    <h2>Resumen Ejecutivo</h2>\n    <p>{{{{executiveSummary}}}}</p>\n    <h2>Páginas</h2>\n    {{{{#each pageData}}}}\n        <h3>Página {{{{this.pageNumber}}}}</h3>\n        <p>{{{{this.content}}}}</p>\n        <div class=\""key-points\"">\n            <strong>Puntos Clave:</strong>\n            <ul>\n                {{{{#each this.keyPoints}}}}\n                    <li>{{{{this}}}}</li>\n                {{{{/each}}}}\n            </ul>\n        </div>\n        <h4>Entidades</h4>\n        <ul>\n            <li>Nombres: {{{{this.entities.names}}}}</li>\n            <li>Fechas: {{{{this.entities.dates}}}}</li>\n            <li>Montos: {{{{this.entities.amounts}}}}</li>\n            <li>Ubicaciones: {{{{this.entities.locations}}}}</li>\n        </ul>\n    {{{{/each}}}}\n    <h2>Tablas</h2>\n    {{{{#each tableData}}}}\n        <h3>{{{{this.title}}}}</h3>\n        <table>\n            <thead>\n                <tr>\n                    {{{{#each this.headers}}}}\n                        <th>{{{{this}}}}</th>\n                    {{{{/each}}}}\n                </tr>\n            </thead>\n            <tbody>\n                {{{{#each this.rows}}}}\n                    <tr>\n                        {{{{#each this}}}}\n                            <td>{{{{this}}}}</td>\n                        {{{{/each}}}}\n                    </tr>\n                {{{{/each}}}}\n            </tbody>\n        </table>\n        <p><em>{{{{this.summary}}}}</em></p>\n    {{{{/each}}}}\n    <h2>Perspectivas Clave</h2>\n    {{{{#each keyInsights}}}}\n        <div class=\""insight\"">\n            <strong>Tipo:</strong> {{{{this.type}}}}<br>\n            <strong>Título:</strong> {{{{this.title}}}}<br>\n            <strong>Descripción:</strong> {{{{this.description}}}}<br>\n            <strong>Valor:</strong> {{{{this.value}}}}<br>\n            <strong>Importancia:</strong> {{{{this.importance}}}}<br>\n        </div>\n    {{{{/each}}}}\n</body>\n</html>


{{
    ""documentType"": ""{documentType}"",
    ""category"": ""{category}"",
    ""description"": ""{description}"",
    ""executiveSummary"": ""resumen ejecutivo completo del documento"",
    ""pageData"": [
        {{
            ""pageNumber"": 1,
            ""content"": ""contenido completo de la página 1"",
            ""keyPoints"": [""punto clave 1"", ""punto clave 2""],
            ""entities"": {{
                ""names"": [""nombres encontrados""],
                ""dates"": [""fechas encontradas""],
                ""amounts"": [""montos encontrados""],
                ""locations"": [""ubicaciones encontradas""]
            }}
        }}
    ],
    ""tableData"": [
        {{
            ""tableNumber"": 1,
            ""pageNumber"": 1,
            ""title"": ""título o descripción de la tabla"",
            ""headers"": [""columna1"", ""columna2"", ""columna3""],
            ""rows"": [
                [""valor1"", ""valor2"", ""valor3""],
                [""valor4"", ""valor5"", ""valor6""]
            ],
            ""summary"": ""resumen de lo que representa esta tabla""
        }}
    ],
    ""keyInsights"": [
        {{
            ""type"": ""FINANCIAL"",
            ""title"": ""insight financiero"",
            ""description"": ""descripción del insight"",
            ""value"": ""valor específico"",
            ""importance"": ""HIGH""
        }},
        {{
            ""type"": ""LEGAL"",
            ""title"": ""insight legal"",
            ""description"": ""descripción del insight"",
            ""value"": ""valor específico"",
            ""importance"": ""MEDIUM""
        }}
    ],
    ""HtmlOutput"": ""<!DOCTYPE html>... (HTML completo del documento)...""
}}
 
";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";
                
                // Clean response of any markdown formatting
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }

                _logger.LogInformation("📝 AI Response Length: {Length} characters", aiResponse.Length);

                // Parse the AI response
                var structuredData = System.Text.Json.JsonSerializer.Deserialize<GlobalDocumentStructuredData>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (structuredData == null)
                {
                    throw new InvalidOperationException("Failed to deserialize AI response to structured data");
                }

                structuredData.Success = true;
                structuredData.RawAIResponse = aiResponse;
                structuredData.ProcessedAt = DateTime.UtcNow;

                _logger.LogInformation("✅ AI processing completed successfully");
                _logger.LogInformation("📊 Structured data: {Pages} pages, {Tables} tables, {Insights} insights", 
                    structuredData.PageData?.Count ?? 0, 
                    structuredData.TableData?.Count ?? 0, 
                    structuredData.KeyInsights?.Count ?? 0);

                return structuredData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in AI document processing");
                return new GlobalDocumentStructuredData
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Save processed global document to Cosmos DB TwinSemiStructured container
        /// </summary>
        private async Task<bool> SaveGlobalDocumentToCosmosAsync(GlobalDocumentResult result, GlobalDocumentStructuredData structuredData)
        {
            try
            {
                _logger.LogInformation("💾 Saving global document to Cosmos DB TwinSemiStructured for Twin: {TwinId}", 
                    result.ContainerName);

                // Create Cosmos DB client
                var accountName = _configuration.GetValue<string>("Values:COSMOS_ACCOUNT_NAME") ??
                                 _configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb";

                var databaseName = _configuration.GetValue<string>("Values:COSMOS_DATABASE_NAME") ??
                                  _configuration.GetValue<string>("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";

                var endpoint = $"https://{accountName}.documents.azure.com:443/";
                var key = _configuration.GetValue<string>("Values:COSMOS_KEY") ??
                         _configuration.GetValue<string>("COSMOS_KEY");

                if (string.IsNullOrEmpty(key))
                {
                    throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration.");
                }

                using var client = new Microsoft.Azure.Cosmos.CosmosClient(endpoint, key);
                var database = client.GetDatabase(databaseName);
                var container = database.GetContainer("TwinSemiStructured");

                // Create global document for Cosmos DB with structured nested objects
                var globalDocument = new Dictionary<string, object?>
                {
                    ["id"] = Guid.NewGuid().ToString(), // Generate unique ID
                    ["TwinID"] = result.ContainerName, // Partition key (twinId)
                    ["documentType"] = "global_document",
                    ["fileName"] = result.FileName,
                    ["filePath"] = result.FilePath,
                    ["fullFilePath"] = result.FullPath,
                    ["containerName"] = result.ContainerName,
                    ["documentUrl"] = result.DocumentUrl,
                    ["originalDocumentType"] = result.DocumentType,
                    ["category"] = result.Category,
                    ["description"] = result.Description,
                    ["totalPages"] = result.TotalPages,
                    ["textContent"] = result.TextContent,
                    ["processedAt"] = result.ProcessedAt.ToString("O"),
                    ["createdAt"] = DateTime.UtcNow.ToString("O"),
                    ["success"] = result.Success,

                    // ✅ Store the complete structured data as NESTED OBJECTS
                    ["structuredData"] = new Dictionary<string, object?>
                    {
                        ["documentType"] = structuredData.DocumentType,
                        ["category"] = structuredData.Category,
                        ["description"] = structuredData.Description,
                        ["executiveSummary"] = structuredData.ExecutiveSummary,
                        ["htmlOutput"] = structuredData.HtmlOutput ?? "",
                        
                        // Page data with complete structure
                        ["pageData"] = structuredData.PageData.Select(p => new Dictionary<string, object?>
                        {
                            ["pageNumber"] = p.PageNumber,
                            ["content"] = p.Content,
                            ["keyPoints"] = p.KeyPoints,
                            ["entities"] = new Dictionary<string, object?>
                            {
                                ["names"] = p.Entities.Names,
                                ["dates"] = p.Entities.Dates,
                                ["amounts"] = p.Entities.Amounts,
                                ["locations"] = p.Entities.Locations
                            }
                        }).ToList(),

                        // Table data with complete structure
                        ["tableData"] = structuredData.TableData.Select(t => new Dictionary<string, object?>
                        {
                            ["tableNumber"] = t.TableNumber,
                            ["pageNumber"] = t.PageNumber,
                            ["title"] = t.Title,
                            ["headers"] = t.Headers,
                            ["rows"] = t.Rows,
                            ["summary"] = t.Summary
                        }).ToList(),

                        // Key insights with complete structure
                        ["keyInsights"] = structuredData.KeyInsights.Select(i => new Dictionary<string, object?>
                        {
                            ["type"] = i.Type,
                            ["title"] = i.Title,
                            ["description"] = i.Description,
                            ["value"] = i.Value,
                            ["importance"] = i.Importance
                        }).ToList(),

                        ["rawAIResponse"] = structuredData.RawAIResponse,
                        ["aiProcessedAt"] = structuredData.ProcessedAt.ToString("O")
                    },

                    // Summary statistics for quick queries
                    ["summary"] = new Dictionary<string, object?>
                    {
                        ["pageCount"] = result.PageData.Count,
                        ["tableCount"] = result.TableData.Count,
                        ["insightCount"] = result.KeyInsights.Count,
                        ["hasExecutiveSummary"] = !string.IsNullOrEmpty(result.ExecutiveSummary),
                        ["hasHtmlOutput"] = !string.IsNullOrEmpty(structuredData.HtmlOutput)
                    }
                };

                // Save to Cosmos DB using partition key
                await container.UpsertItemAsync(globalDocument, new Microsoft.Azure.Cosmos.PartitionKey(result.ContainerName));

                _logger.LogInformation("✅ Global document saved successfully to TwinSemiStructured: {DocumentType} for Twin: {TwinId}",
                    result.DocumentType, result.ContainerName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to save global document to Cosmos DB for Twin: {TwinId}",
                    result.ContainerName);
                return false;
            }
        }

        /// <summary>
        /// Get all global documents by TwinID and optionally filter by category
        /// </summary>
        /// <param name="twinId">Twin ID to search for</param>
        /// <param name="category">Optional category filter (e.g., "CASA_VIVIENDA", "CONTRATOS", "SEGUROS")</param>
        /// <param name="limit">Maximum number of documents to return</param>
        /// <param name="offset">Number of documents to skip for pagination</param>
        /// <returns>List of global documents for the Twin</returns>
        public async Task<GlobalDocumentsListResult> GetGlobalDocumentsByTwinIdAsync(
            string twinId,
            string? category = null,
            int limit = 50,
            int offset = 0)
        {
            _logger.LogInformation("📋 Getting global documents for Twin: {TwinId}, Category: {Category}, Limit: {Limit}, Offset: {Offset}",
                twinId, category ?? "ALL", limit, offset);

            var result = new GlobalDocumentsListResult
            {
                Success = false,
                TwinId = twinId,
                Category = category,
                RetrievedAt = DateTime.UtcNow
            };

            try
            {
                // Create Cosmos DB client
                var accountName = _configuration.GetValue<string>("Values:COSMOS_ACCOUNT_NAME") ??
                                 _configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb";

                var databaseName = _configuration.GetValue<string>("Values:COSMOS_DATABASE_NAME") ??
                                  _configuration.GetValue<string>("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";

                var endpoint = $"https://{accountName}.documents.azure.com:443/";
                var key = _configuration.GetValue<string>("Values:COSMOS_KEY") ??
                         _configuration.GetValue<string>("COSMOS_KEY");

                if (string.IsNullOrEmpty(key))
                {
                    throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration.");
                }

                using var client = new Microsoft.Azure.Cosmos.CosmosClient(endpoint, key);
                var database = client.GetDatabase(databaseName);
                var container = database.GetContainer("TwinSemiStructured");

                // ✅ Build query with proper filtering for global documents
                Microsoft.Azure.Cosmos.QueryDefinition query;
                
                if (!string.IsNullOrEmpty(category))
                {
                    // Query with category filter - ensure we only get global documents
                    query = new Microsoft.Azure.Cosmos.QueryDefinition(@"
                        SELECT * FROM c 
                        WHERE c.TwinID = @twinId 
                        AND c.documentType = 'global_document'
                        AND c.category = @category
                        ORDER BY c.processedAt DESC
                        OFFSET @offset LIMIT @limit")
                        .WithParameter("@twinId", twinId)
                        .WithParameter("@category", category)
                        .WithParameter("@offset", offset)
                        .WithParameter("@limit", limit);
                }
                else
                {
                    // Query without category filter - ensure we only get global documents
                    query = new Microsoft.Azure.Cosmos.QueryDefinition(@"
                        SELECT * FROM c 
                        WHERE c.TwinID = @twinId
                        AND c.documentType = 'global_document'
                        ORDER BY c.processedAt DESC
                        OFFSET @offset LIMIT @limit")
                        .WithParameter("@twinId", twinId)
                        .WithParameter("@offset", offset)
                        .WithParameter("@limit", limit);
                }

                var iterator = container.GetItemQueryIterator<Dictionary<string, object?>>(query);
                 var documents = new List<GlobalDocumentSummary>();
                var documentsAll = new List<Document>();
                while (iterator.HasMoreResults)
                {

                    // Deserializar el JSON a un objeto Document  
                    var response = await iterator.ReadNextAsync(); // Obtener la respuesta del iterador  
                    foreach (var item in response)
                    {
                        // Convertir el Dictionary<string, object?> a un JSON string  
                        string jsonString = JsonConvert.SerializeObject(item);

                        // Deserializar el JSON a un objeto Document  
                        Document documentNew = JsonConvert.DeserializeObject<Document>(jsonString);

                        // Agregar el documento a la lista  
                        documentsAll.Add(documentNew);
                    }
                    var response2 = await iterator.ReadNextAsync();
                    foreach (var document in response)
                    {
                        // ✅ This will create a COMPLETE document summary with ALL fields
                        var summary = GlobalDocumentSummary.FromCosmosDocument(document);
                        documents.Add(summary);
                    }
                }

                // ✅ Generate fresh SAS URLs for each document
                _logger.LogInformation("🔗 Generating fresh SAS URLs for {Count} documents", documents.Count);
                
                try
                {
                    var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                    
                    foreach (var doc in documentsAll)
                    {
                        try
                        {
                            // Use containerName if available, otherwise fallback to twinId
                            var containerName = !string.IsNullOrEmpty(doc.ContainerName) ? doc.ContainerName : twinId;
                            var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                            
                            // ✅ FIXED: Use the exact path structure from Cosmos DB
                            // Priority: 1. fullFilePath, 2. filePath/fileName, 3. fileName only
                            string filePathForSas = "";
                            
                            if (!string.IsNullOrEmpty(doc.FullFilePath))
                            {
                                // Use fullFilePath but remove container prefix if present
                                filePathForSas = doc.FullFilePath;
                                if (filePathForSas.StartsWith($"{containerName}/", StringComparison.OrdinalIgnoreCase))
                                {
                                    filePathForSas = filePathForSas.Substring($"{containerName}/".Length);
                                }
                            }
                            else if (!string.IsNullOrEmpty(doc.FilePath) && !string.IsNullOrEmpty(doc.FileName))
                            {
                                // Construct from filePath + fileName
                                filePathForSas = $"{doc.FilePath.Trim('/')}/{doc.FileName}";
                            }
                            else if (!string.IsNullOrEmpty(doc.FileName))
                            {
                                // Fallback to just fileName
                                filePathForSas = doc.FileName;
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ No valid file path found for document: {DocumentId}", doc.Id);
                                continue;
                            }
                            
                            _logger.LogDebug("🔍 Generando SAS para el documento: {FileName}, Ruta: {FilePath}", 
                                doc.FileName, filePathForSas);
                            
                            // Generate fresh SAS URL (24 hours validity)
                            var freshSasUrl = await dataLakeClient.GenerateSasUrlAsync(filePathForSas, TimeSpan.FromHours(24));
                            
                            if (!string.IsNullOrEmpty(freshSasUrl))
                            {
                                doc.DocumentUrl = freshSasUrl;
                                _logger.LogDebug("✅ Generated fresh SAS URL for document: {FileName}", doc.FileName);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Could not generate SAS URL for document: {FileName} at path: {FilePath}", 
                                    doc.FileName, filePathForSas);
                            }
                        }
                        catch (Exception sasEx)
                        {
                            _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for document: {FileName}, keeping original URL", 
                                doc.FileName);
                            // Keep the original DocumentUrl from Cosmos DB if SAS generation fails
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error creating DataLake factory for SAS URL generation, using stored URLs");
                    // Continue with stored URLs from Cosmos DB
                }

                result.Success = true;
                result.Documents = documents;
                result.TotalDocuments = documents.Count;
                result.Limit = limit;
                result.DocumentsMain = documentsAll;
                result.Offset = offset;

                _logger.LogInformation("✅ Found {Count} global documents for Twin: {TwinId} with category filter: {Category}",
                    documents.Count, twinId, category ?? "ALL");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting global documents for Twin: {TwinId}", twinId);
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Get document categories available for a Twin
        /// </summary>
        /// <param name="twinId">Twin ID to search for</param>
        /// <returns>List of available categories with document counts</returns>
        public async Task<GlobalDocumentCategoriesResult> GetDocumentCategoriesAsync(string twinId)
        {
            _logger.LogInformation("📂 Getting document categories for Twin: {TwinId}", twinId);

            var result = new GlobalDocumentCategoriesResult
            {
                Success = false,
                TwinId = twinId,
                RetrievedAt = DateTime.UtcNow
            };

            try
            {
                // Create Cosmos DB client
                var accountName = _configuration.GetValue<string>("Values:COSMOS_ACCOUNT_NAME") ??
                                 _configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb";

                var databaseName = _configuration.GetValue<string>("Values:COSMOS_DATABASE_NAME") ??
                                  _configuration.GetValue<string>("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";

                var endpoint = $"https://{accountName}.documents.azure.com:443/";
                var key = _configuration.GetValue<string>("Values:COSMOS_KEY") ??
                         _configuration.GetValue<string>("COSMOS_KEY");

                if (string.IsNullOrEmpty(key))
                {
                    throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration.");
                }

                using var client = new Microsoft.Azure.Cosmos.CosmosClient(endpoint, key);
                var database = client.GetDatabase(databaseName);
                var container = database.GetContainer("TwinSemiStructured");

                // ✅ First, get all documents for the Twin to manually group categories
                // This ensures we handle category variations properly
                var query = new Microsoft.Azure.Cosmos.QueryDefinition(@"
                    SELECT c.category 
                    FROM c 
                    WHERE c.TwinID = @twinId 
                    AND c.documentType = 'global_document'
                    AND IS_DEFINED(c.category) 
                    AND c.category != null")
                    .WithParameter("@twinId", twinId);

                var iterator = container.GetItemQueryIterator<Dictionary<string, object?>>(query);
                var categoryList = new List<string>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        var category = item.GetValueOrDefault("category")?.ToString();
                        if (!string.IsNullOrWhiteSpace(category))
                        {
                            categoryList.Add(category.Trim());
                        }
                    }
                }

                _logger.LogInformation("📊 Raw categories found: {Count}, Categories: [{Categories}]", 
                    categoryList.Count, string.Join(", ", categoryList));

                // ✅ Group and count categories manually to handle duplicates properly
                var categoryGroups = categoryList
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .GroupBy(c => c.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new DocumentCategoryInfo
                    {
                        Category = g.First().Trim(), // Use the first occurrence as the canonical form
                        DocumentCount = g.Count()
                    })
                    .OrderByDescending(c => c.DocumentCount)
                    .ThenBy(c => c.Category)
                    .ToList();

                _logger.LogInformation("📊 Grouped categories: {Count}", categoryGroups.Count);
                foreach (var cat in categoryGroups)
                {
                    _logger.LogInformation("  📂 {Category}: {Count} document(s)", cat.Category, cat.DocumentCount);
                }

                result.Success = true;
                result.Categories = categoryGroups;
                result.TotalCategories = categoryGroups.Count;

                _logger.LogInformation("✅ Found {Count} unique categories for Twin: {TwinId}", categoryGroups.Count, twinId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting document categories for Twin: {TwinId}", twinId);
                result.ErrorMessage = ex.Message;
                return result;
            }
        }
    }

    /// <summary>
    /// Result of global document processing
    /// </summary>
    public class GlobalDocumentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? DocumentUrl { get; set; }
        public string TextContent { get; set; } = string.Empty;
        public int TotalPages { get; set; }
        public DateTime ProcessedAt { get; set; }
        public bool CosmosDbSaved { get; set; } = false;
        
        // Structured data from AI processing
        public GlobalDocumentStructuredData? StructuredData { get; set; }
        public List<DocumentPageData> PageData { get; set; } = new();
        public List<DocumentTableData> TableData { get; set; } = new();
        public string ExecutiveSummary { get; set; } = string.Empty;
        public List<DocumentInsight> KeyInsights { get; set; } = new();

        /// <summary>
        /// Get full path of the document
        /// </summary>
        public string FullPath => $"{ContainerName}/{FilePath}/{FileName}";

        /// <summary>
        /// Get comprehensive summary of processing results
        /// </summary>
        public string GetComprehensiveSummary()
        {
            if (!Success)
            {
                return $"❌ Processing failed: {ErrorMessage}";
            }

            return $"✅ Successfully processed: {FileName}\n" +
                   $"📍 Location: {FullPath}\n" +
                   $"📋 Type: {DocumentType} ({Category})\n" +
                   $"📄 Pages: {TotalPages}\n" +
                   $"📊 Tables: {TableData.Count}\n" +
                   $"💡 Insights: {KeyInsights.Count}\n" +
                   $"💾 Cosmos DB: {(CosmosDbSaved ? "Saved" : "Not saved")}\n" +
                   $"📅 Processed: {ProcessedAt:yyyy-MM-dd HH:mm} UTC";
        }
    }

    /// <summary>
    /// Structured data extracted from global document using AI
    /// </summary>
    public class GlobalDocumentStructuredData
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ExecutiveSummary { get; set; } = string.Empty;
        public List<DocumentPageData> PageData { get; set; } = new();
        public List<DocumentTableData> TableData { get; set; } = new();
        public List<DocumentInsight> KeyInsights { get; set; } = new();
        public string? RawAIResponse { get; set; }

        public string? HtmlOutput { get; set; }
        public DateTime ProcessedAt { get; set; }
    }

    /// <summary>
    /// Data extracted from a specific page of the document
    /// </summary>
    public class DocumentPageData
    {
        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; set; }
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
        
        [JsonPropertyName("keyPoints")]
        public List<string> KeyPoints { get; set; } = new();
        
        [JsonPropertyName("entities")]
        public DocumentEntities Entities { get; set; } = new();
    }

    /// <summary>
    /// Named entities extracted from a page
    /// </summary>
    public class DocumentEntities
    {
        [JsonPropertyName("names")]
        public List<string> Names { get; set; } = new();
        
        [JsonPropertyName("dates")]
        public List<string> Dates { get; set; } = new();
        
        [JsonPropertyName("amounts")]
        public List<string> Amounts { get; set; } = new();
        
        [JsonPropertyName("locations")]
        public List<string> Locations { get; set; } = new();
    }

    /// <summary>
    /// Data extracted from a table in the document
    /// </summary>
    public class DocumentTableData
    {
        [JsonPropertyName("tableNumber")]
        public int TableNumber { get; set; }
        
        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonPropertyName("headers")]
        public List<string> Headers { get; set; } = new();
        
        [JsonPropertyName("rows")]
        public List<List<string>> Rows { get; set; } = new();
        
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Key insight extracted from the document
    /// </summary>
    public class DocumentInsight
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty; // FINANCIAL, LEGAL, OPERATIONAL, etc.
        
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
        
        [JsonPropertyName("importance")]
        public string Importance { get; set; } = "MEDIUM"; // HIGH, MEDIUM, LOW
    }

    /// <summary>
    /// Result of getting global documents list by TwinID
    /// </summary>
    public class GlobalDocumentsListResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string? Category { get; set; }
        public int TotalDocuments { get; set; }
        public List<GlobalDocumentSummary> Documents { get; set; } = new();

        public List<Document> DocumentsMain { get; set; } = new();
        public int Limit { get; set; }
        public int Offset { get; set; }
        public DateTime RetrievedAt { get; set; }
    }

    /// <summary>
    /// Summary of a global document for list responses
    /// </summary>
    public class GlobalDocumentSummary
    {
        public string Id { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FullFilePath { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string DocumentUrl { get; set; } = string.Empty;
        public string OriginalDocumentType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int TotalPages { get; set; }
        public string TextContent { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Success { get; set; }
        
        // Structured data from AI processing
        public GlobalDocumentStructuredData? StructuredData { get; set; }
        public string ExecutiveSummary { get; set; } = string.Empty;
        public List<DocumentPageData> PageData { get; set; } = new();
        public List<DocumentTableData> TableData { get; set; } = new();
        public List<DocumentInsight> KeyInsights { get; set; } = new();
        public string? HtmlOutput { get; set; }
        public string? RawAIResponse { get; set; }
        public DateTime AiProcessedAt { get; set; }
        
        // Summary statistics
        public int PageCount { get; set; }
        public int TableCount { get; set; }
        public int InsightCount { get; set; }
        public bool HasExecutiveSummary { get; set; }
        public bool HasHtmlOutput { get; set; }

        /// <summary>
        /// Create summary from Cosmos DB document
        /// </summary>
        public static GlobalDocumentSummary FromCosmosDocument(Dictionary<string, object?> document)
        {
            var summary = new GlobalDocumentSummary
            {
                // Basic document fields
                Id = GetStringValue(document, "id"),
                TwinId = GetStringValue(document, "TwinID"),
                DocumentType = GetStringValue(document, "documentType"),
                FileName = GetStringValue(document, "fileName"),
                FilePath = GetStringValue(document, "filePath"),
                FullFilePath = GetStringValue(document, "fullFilePath"),
                ContainerName = GetStringValue(document, "containerName"),
                DocumentUrl = GetStringValue(document, "documentUrl"),
                OriginalDocumentType = GetStringValue(document, "originalDocumentType"),
                Category = GetStringValue(document, "category"),
                Description = GetStringValue(document, "description"),
                TotalPages = GetIntValue(document, "totalPages"),
                TextContent = GetStringValue(document, "textContent"),
                ProcessedAt = GetDateTimeValue(document, "processedAt"),
                CreatedAt = GetDateTimeValue(document, "createdAt"),
                Success = GetBoolValue(document, "success")
            };

            // Extract structured data from nested object
            if (document.TryGetValue("structuredData", out var structuredObj))
            {
                var structuredDict = ConvertToStringObjectDict(structuredObj);
                if (structuredDict != null)
                {
                    // Basic structured data fields
                    summary.ExecutiveSummary = GetStringValue(structuredDict, "executiveSummary");
                    summary.HtmlOutput = GetStringValue(structuredDict, "htmlOutput");
                    summary.RawAIResponse = GetStringValue(structuredDict, "rawAIResponse");
                    summary.AiProcessedAt = GetDateTimeValue(structuredDict, "aiProcessedAt");

                    // Extract page data
                    if (structuredDict.TryGetValue("pageData", out var pageDataObj))
                    {
                        summary.PageData = ConvertToPageDataList(pageDataObj);
                    }

                    // Extract table data
                    if (structuredDict.TryGetValue("tableData", out var tableDataObj))
                    {
                        summary.TableData = ConvertToTableDataList(tableDataObj);
                    }

                    // Extract key insights
                    if (structuredDict.TryGetValue("keyInsights", out var insightsObj))
                    {
                        summary.KeyInsights = ConvertToInsightsList(insightsObj);
                    }

                    // Create structured data object
                    summary.StructuredData = new GlobalDocumentStructuredData
                    {
                        Success = true,
                        DocumentType = GetStringValue(structuredDict, "documentType"),
                        Category = GetStringValue(structuredDict, "category"),
                        Description = GetStringValue(structuredDict, "description"),
                        ExecutiveSummary = summary.ExecutiveSummary,
                        PageData = summary.PageData,
                        TableData = summary.TableData,
                        KeyInsights = summary.KeyInsights,
                        HtmlOutput = summary.HtmlOutput,
                        RawAIResponse = summary.RawAIResponse,
                        ProcessedAt = summary.AiProcessedAt
                    };
                }
            }

            // Get summary statistics if available
            if (document.TryGetValue("summary", out var summaryObj))
            {
                var summaryDict = ConvertToStringObjectDict(summaryObj);
                if (summaryDict != null)
                {
                    summary.PageCount = GetIntValue(summaryDict, "pageCount");
                    summary.TableCount = GetIntValue(summaryDict, "tableCount");
                    summary.InsightCount = GetIntValue(summaryDict, "insightCount");
                    summary.HasExecutiveSummary = GetBoolValue(summaryDict, "hasExecutiveSummary");
                    summary.HasHtmlOutput = GetBoolValue(summaryDict, "hasHtmlOutput");
                }
            }

            return summary;
        }

        /// <summary>
        /// Convert various object types to Dictionary<string, object?> for robust parsing
        /// </summary>
        private static Dictionary<string, object?>? ConvertToStringObjectDict(object? obj)
        {
            if (obj == null) return null;

            if (obj is Dictionary<string, object?> dict)
                return dict;

            if (obj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
            {
                var result = new Dictionary<string, object?>();
                foreach (var property in jsonElement.EnumerateObject())
                {
                    result[property.Name] = JsonElementToObject(property.Value);
                }
                return result;
            }

            // Try to convert other dictionary types
            if (obj is IDictionary<string, object> genericDict)
            {
                return genericDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            return null;
        }

        /// <summary>
        /// Convert JsonElement to appropriate object type
        /// </summary>
        private static object? JsonElementToObject(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
                JsonValueKind.Object => ConvertToStringObjectDict(element),
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        /// <summary>
        /// Convert object to list of DocumentPageData
        /// </summary>
        private static List<DocumentPageData> ConvertToPageDataList(object? obj)
        {
            var result = new List<DocumentPageData>();
            
            if (obj == null) return result;

            var list = ConvertToObjectList(obj);
            foreach (var item in list)
            {
                var dict = ConvertToStringObjectDict(item);
                if (dict != null)
                {
                    var pageData = new DocumentPageData
                    {
                        PageNumber = GetIntValue(dict, "pageNumber"),
                        Content = GetStringValue(dict, "content"),
                        KeyPoints = ConvertToStringList(dict.GetValueOrDefault("keyPoints")),
                        Entities = ConvertToDocumentEntities(dict.GetValueOrDefault("entities"))
                    };
                    result.Add(pageData);
                }
            }

            return result;
        }

        /// <summary>
        /// Convert object to list of DocumentTableData
        /// </summary>
        private static List<DocumentTableData> ConvertToTableDataList(object? obj)
        {
            var result = new List<DocumentTableData>();
            
            if (obj == null) return result;

            var list = ConvertToObjectList(obj);
            foreach (var item in list)
            {
                var dict = ConvertToStringObjectDict(item);
                if (dict != null)
                {
                    var tableData = new DocumentTableData
                    {
                        TableNumber = GetIntValue(dict, "tableNumber"),
                        PageNumber = GetIntValue(dict, "pageNumber"),
                        Title = GetStringValue(dict, "title"),
                        Headers = ConvertToStringList(dict.GetValueOrDefault("headers")),
                        Rows = ConvertToStringListList(dict.GetValueOrDefault("rows")),
                        Summary = GetStringValue(dict, "summary")
                    };
                    result.Add(tableData);
                }
            }

            return result;
        }

        /// <summary>
        /// Convert object to list of DocumentInsight
        /// </summary>
        private static List<DocumentInsight> ConvertToInsightsList(object? obj)
        {
            var result = new List<DocumentInsight>();
            
            if (obj == null) return result;

            var list = ConvertToObjectList(obj);
            foreach (var item in list)
            {
                var dict = ConvertToStringObjectDict(item);
                if (dict != null)
                {
                    var insight = new DocumentInsight
                    {
                        Type = GetStringValue(dict, "type"),
                        Title = GetStringValue(dict, "title"),
                        Description = GetStringValue(dict, "description"),
                        Value = GetStringValue(dict, "value"),
                        Importance = GetStringValue(dict, "importance")
                    };
                    result.Add(insight);
                }
            }

            return result;
        }

        /// <summary>
        /// Convert object to DocumentEntities
        /// </summary>
        private static DocumentEntities ConvertToDocumentEntities(object? obj)
        {
            var result = new DocumentEntities();
            
            var dict = ConvertToStringObjectDict(obj);
            if (dict != null)
            {
                result.Names = ConvertToStringList(dict.GetValueOrDefault("names"));
                result.Dates = ConvertToStringList(dict.GetValueOrDefault("dates"));
                result.Amounts = ConvertToStringList(dict.GetValueOrDefault("amounts"));
                result.Locations = ConvertToStringList(dict.GetValueOrDefault("locations"));
            }

            return result;
        }

        /// <summary>
        /// Convert object to List<object>
        /// </summary>
        private static List<object> ConvertToObjectList(object? obj)
        {
            if (obj == null) return new List<object>();

            if (obj is List<object> list)
                return list;

            if (obj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                return jsonElement.EnumerateArray().Select(JsonElementToObject).Where(x => x != null).ToList()!;
            }

            if (obj is IEnumerable<object> enumerable)
                return enumerable.ToList();

            return new List<object>();
        }

        /// <summary>
        /// Convert object to List<string>
        /// </summary>
        private static List<string> ConvertToStringList(object? obj)
        {
            if (obj == null) return new List<string>();

            var objectList = ConvertToObjectList(obj);
            return objectList.Select(x => x?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        /// <summary>
        /// Convert object to List<List<string>>
        /// </summary>
        private static List<List<string>> ConvertToStringListList(object? obj)
        {
            if (obj == null) return new List<List<string>>();

            var result = new List<List<string>>();
            var objectList = ConvertToObjectList(obj);
            
            foreach (var item in objectList)
            {
                var innerList = ConvertToStringList(item);
                result.Add(innerList);
            }

            return result;
        }

        private static string GetStringValue(Dictionary<string, object?> data, string key)
        {
            if (data.TryGetValue(key, out var value) && value != null)
            {
                if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                    return jsonElement.GetString() ?? string.Empty;
                return value.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        private static int GetIntValue(Dictionary<string, object?> data, string key)
        {
            if (data.TryGetValue(key, out var value) && value != null)
            {
                if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number)
                    return jsonElement.GetInt32();
                if (int.TryParse(value.ToString(), out var result))
                    return result;
            }
            return 0;
        }

        private static bool GetBoolValue(Dictionary<string, object?> data, string key)
        {
            if (data.TryGetValue(key, out var value) && value != null)
            {
                if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.True)
                    return true;
                if (value is JsonElement jsonElement2 && jsonElement2.ValueKind == JsonValueKind.False)
                    return false;
                if (bool.TryParse(value.ToString(), out var result))
                    return result;
            }
            return false;
        }

        private static DateTime GetDateTimeValue(Dictionary<string, object?> data, string key)
        {
            if (data.TryGetValue(key, out var value) && value != null)
            {
                if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(jsonElement.GetString(), out var result))
                        return result;
                }
                if (DateTime.TryParse(value.ToString(), out var dateResult))
                    return dateResult;
            }
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Result of getting document categories
    /// </summary>
    public class GlobalDocumentCategoriesResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public int TotalCategories { get; set; }
        public List<DocumentCategoryInfo> Categories { get; set; } = new();
        public DateTime RetrievedAt { get; set; }
    }

    /// <summary>
    /// Information about a document category
    /// </summary>
    public class DocumentCategoryInfo
    {
        public string Category { get; set; } = string.Empty;
        public int DocumentCount { get; set; }
    }
}
 
public class Document
{
    public string Id { get; set; }
    public string TwinID { get; set; }
    public string DocumentType { get; set; }
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public string FullFilePath { get; set; }
    public string ContainerName { get; set; }
    public string DocumentUrl { get; set; }
    public string OriginalDocumentType { get; set; }
    public string Category { get; set; }
    public string Description { get; set; }
    public int TotalPages { get; set; }
    public string TextContent { get; set; }
    public DateTime ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Success { get; set; }
    public StructuredData StructuredData { get; set; }
    public Summary Summary { get; set; }
}

public class StructuredData
{
    public string DocumentType { get; set; }
    public string Category { get; set; }
    public string Description { get; set; }
    public string ExecutiveSummary { get; set; }
    public string HtmlOutput { get; set; }
    public List<PageData> PageData { get; set; }
    public List<TableData> TableData { get; set; }
    public List<KeyInsight> KeyInsights { get; set; }
    public string RawAIResponse { get; set; }
    public DateTime AiProcessedAt { get; set; }
}

public class PageData
{
    public int PageNumber { get; set; }
    public string Content { get; set; }
    public List<string> KeyPoints { get; set; }
    public Entities Entities { get; set; }
}

public class Entities
{
    public List<string> Names { get; set; }
    public List<string> Dates { get; set; }
    public List<string> Amounts { get; set; }
    public List<string> Locations { get; set; }
}

public class TableData
{
    public int TableNumber { get; set; }
    public int PageNumber { get; set; }
    public string Title { get; set; }
    public List<string> Headers { get; set; }
    public List<List<string>> Rows { get; set; }
    public string Summary { get; set; }
}

public class KeyInsight
{
    public string Type { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Value { get; set; }
    public string Importance { get; set; }
}

public class Summary
{
    public int PageCount { get; set; }
    public int TableCount { get; set; }
    public int InsightCount { get; set; }
    public bool HasExecutiveSummary { get; set; }
    public bool HasHtmlOutput { get; set; }
}