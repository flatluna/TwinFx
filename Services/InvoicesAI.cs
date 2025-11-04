using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Models;
using Azure.AI.Agents.Persistent;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using Azure.Identity;
using Agents;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using TwinFx.Services;

namespace TwinFx.Services
{
    public class InvoicesAI
    {
        private readonly ILogger<InvoicesAI> _logger;
        private readonly IConfiguration _configuration;
        private readonly UnStructuredDocumentsCosmosDB _cosmosService;
        private PersistentAgentsClient _aiAgentsClient;
        private Kernel _kernel;

        public InvoicesAI(ILogger<InvoicesAI> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Crear logger específico para UnStructuredDocumentsCosmosDB
            var cosmosLogger = LoggerFactory.Create(builder => builder.AddConsole())
                .CreateLogger<UnStructuredDocumentsCosmosDB>();
            _cosmosService = new UnStructuredDocumentsCosmosDB(cosmosLogger, configuration);

            // Inicializar Azure AI Agents Client
            InitializeAzureAIServices();
        }

        private void InitializeAzureAIServices()
        {
            try
            {
                _logger.LogInformation("🔧 Initializing Azure AI services for invoice analysis");

                // Initialize Semantic Kernel
                var builder = Kernel.CreateBuilder();
                var endpoint = _configuration["Values:AzureOpenAI:Endpoint"] ?? 
                              _configuration["AzureOpenAI:Endpoint"] ?? 
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                              
                var apiKey = _configuration["Values:AzureOpenAI:ApiKey"] ?? 
                            _configuration["AzureOpenAI:ApiKey"] ?? 
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                                                      
                var deploymentName = _configuration["Values:AzureOpenAI:DeploymentName"] ?? 
                                   _configuration["AzureOpenAI:DeploymentName"] ?? 
                                   "gpt4mini";

                builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
                _kernel = builder.Build();
                _logger.LogInformation("✅ Semantic Kernel initialized successfully");

                // Initialize Azure AI Agents Client
                try
                {
                    var projectConnectionString = _configuration["Values:PROJECT_CONNECTION_STRING"] ?? 
                                                _configuration["PROJECT_CONNECTION_STRING"];
                                                
                    if (!string.IsNullOrEmpty(projectConnectionString))
                    {
                        _aiAgentsClient = AzureAIAgent.CreateAgentsClient(projectConnectionString, new DefaultAzureCredential());
                        _logger.LogInformation("✅ Azure AI Agents client initialized with PROJECT_CONNECTION_STRING");
                    }
                    else
                    {
                        _aiAgentsClient = AzureAIAgent.CreateAgentsClient(endpoint, new DefaultAzureCredential());
                        _logger.LogInformation("✅ Azure AI Agents client initialized with DefaultAzureCredential");
                    }
                }
                catch (Exception aiAgentsEx)
                {
                    _logger.LogWarning(aiAgentsEx, "⚠️ Azure AI Agents initialization failed, some features may be limited");
                    _aiAgentsClient = null!;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Azure AI services");
                throw;
            }
        }

        /// <summary>
        /// Obtiene facturas por vendor y las convierte a formato CSV con un registro por LineItem
        /// Cada LineItem de cada factura será un registro separado en el CSV
        /// </summary>
        /// <param name="twinId">Twin ID para filtrar facturas</param>
        /// <param name="vendorName">Nombre del vendor para filtrar</param>
        /// <returns>Lista de strings en formato CSV, cada string representa un LineItem</returns>
        public async Task<List<string>> GetInvoicesByVendorAsCsvRecordsAsync(string twinId, string vendorName)
        {
            try
            {
                _logger.LogInformation("🔍 Converting vendor invoices to CSV records - TwinID: {TwinID}, VendorName: {VendorName}", 
                    twinId, vendorName);

                // 1. Llamar al método existente para obtener facturas por vendor
                var invoiceDocuments = await _cosmosService.GetInvoiceDocumentsByVendorNameAsync(twinId, vendorName);

                if (!invoiceDocuments.Any())
                {
                    _logger.LogWarning("⚠️ No se encontraron facturas para TwinID: {TwinID}, VendorName: {VendorName}", 
                        twinId, vendorName);
                    return new List<string>();
                }

                // 2. Crear lista de registros CSV
                var csvRecords = new List<string>();

                // 3. Agregar encabezado CSV
                var header = "id,TwinID,FileName,FilePath,VendorName,InvoiceDate,InvoiceTotal,Description,Quantity,UnitPrice,Amount";
                csvRecords.Add(header);

                // 4. Procesar cada factura y sus LineItems
                foreach (var invoice in invoiceDocuments)
                {
                    // Validar que la factura tenga LineItems
                    if (invoice.InvoiceData?.LineItems == null || !invoice.InvoiceData.LineItems.Any())
                    {
                        _logger.LogWarning("⚠️ Factura {InvoiceId} no tiene LineItems", invoice.id);
                        continue;
                    }

                    // 5. Crear un registro CSV por cada LineItem
                    foreach (var lineItem in invoice.InvoiceData.LineItems)
                    {
                        // Construir registro CSV con las columnas especificadas
                        var csvRecord = string.Join(",", 
                            EscapeCsvValue(invoice.id),
                            EscapeCsvValue(invoice.TwinID),
                            EscapeCsvValue(invoice.FileName),
                            EscapeCsvValue(invoice.FilePath),
                            EscapeCsvValue(invoice.VendorName),
                            EscapeCsvValue(invoice.InvoiceDate.ToString("yyyy-MM-dd")),
                            EscapeCsvValue(invoice.InvoiceTotal.ToString("F2")),
                            EscapeCsvValue(lineItem.Description),
                            EscapeCsvValue(lineItem.Quantity.ToString("F2")),
                            EscapeCsvValue(lineItem.UnitPrice.ToString("F2")),
                            EscapeCsvValue(lineItem.Amount.ToString("F2"))
                        );

                        csvRecords.Add(csvRecord);
                    }
                }

                _logger.LogInformation("✅ Converted {InvoiceCount} invoices to {RecordCount} CSV records (including header)", 
                    invoiceDocuments.Count, csvRecords.Count);

                // Log estadísticas detalladas
                var totalLineItems = invoiceDocuments.Sum(i => i.InvoiceData?.LineItems?.Count ?? 0);
                var totalAmount = invoiceDocuments.Sum(i => i.InvoiceTotal);
                
                // Corregir el cálculo de LineItems amount
                var lineItemsAmount = 0m;
                foreach (var invoice in invoiceDocuments)
                {
                    if (invoice.InvoiceData?.LineItems != null)
                    {
                        lineItemsAmount += invoice.InvoiceData.LineItems.Sum(li => li.Amount);
                    }
                }

                _logger.LogInformation("📊 CSV Conversion Summary:");
                _logger.LogInformation("   • Facturas procesadas: {InvoiceCount}", invoiceDocuments.Count);
                _logger.LogInformation("   • Total LineItems: {LineItemCount}", totalLineItems);
                _logger.LogInformation("   • Total facturas: ${TotalAmount:F2}", totalAmount);
                _logger.LogInformation("   • Total LineItems: ${LineItemsAmount:F2}", lineItemsAmount);
                _logger.LogInformation("   • Registros CSV generados: {CsvRecordCount} (sin header)", csvRecords.Count - 1);

                return csvRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error converting vendor invoices to CSV records - TwinID: {TwinID}, VendorName: {VendorName}", 
                    twinId, vendorName);
                throw;
            }
        }

        /// <summary>
        /// Analiza facturas de un vendor específico usando Azure AI Agent con Code Interpreter
        /// Convierte las facturas a CSV y realiza análisis con IA
        /// </summary>
        /// <param name="twinId">Twin ID para filtrar facturas</param>
        /// <param name="vendorName">Nombre del vendor para filtrar</param>
        /// <param name="question">Pregunta específica sobre las facturas del vendor</param>
        /// <param name="fileID">ID del archivo existente, o "null" para subir uno nuevo</param>
        /// <returns>Análisis completo de las facturas del vendor</returns>
        public async Task<CsvAnalysis> AiInvoicesAgent(string twinId, string vendorName, string question, string fileID = "null")
        {
            try
            {
                _logger.LogInformation("🔍 Starting AI invoice analysis - TwinID: {TwinID}, VendorName: {VendorName}, Question: {Question}, FileID: {FileID}", 
                    twinId, vendorName, question, fileID);

                // 1. Obtener CSV de facturas del vendor
                var csvRecords = await GetInvoicesByVendorAsCsvRecordsAsync(twinId, vendorName);

                if (!csvRecords.Any() || csvRecords.Count <= 1) // Solo header o vacío
                {
                    return new CsvAnalysis
                    {
                        Question = question,
                        AIResponse = $"📭 No se encontraron facturas para el proveedor '{vendorName}' en TwinID: {twinId}",
                        FileID = ""
                    };
                }

                // 2. Convertir List<string> a Stream
                var csvContent = string.Join("\n", csvRecords);
                var csvBytes = Encoding.UTF8.GetBytes(csvContent);
                using var csvStream = new MemoryStream(csvBytes);

                _logger.LogInformation("📊 CSV generated - Records: {RecordCount}, Size: {SizeKB} KB", 
                    csvRecords.Count - 1, csvBytes.Length / 1024);

                // 3. Verificar que Azure AI Agents esté disponible
                if (_aiAgentsClient == null)
                {
                    return new CsvAnalysis
                    {
                        Question = question,
                        AIResponse = "❌ Azure AI Agents no está disponible para el análisis de facturas",
                        FileID = ""
                    };
                }

                // 4. Lógica condicional para manejo del FileID
                string actualFileID;
                
              //  if (string.IsNullOrEmpty(fileID) || fileID.Equals("null", StringComparison.OrdinalIgnoreCase) || fileID.Equals("\"null\"", StringComparison.OrdinalIgnoreCase))
               // {
                    // Subir nuevo archivo CSV
                    _logger.LogInformation("📤 FileID is null/empty, uploading new CSV file to Azure AI Agents...");
                    
                    csvStream.Position = 0;
                    PersistentAgentFileInfo fileInfo = await _aiAgentsClient.Files.UploadFileAsync(
                        csvStream, 
                        PersistentAgentFilePurpose.Agents, 
                        $"facturas_{vendorName}_{twinId}.csv"
                    );

                    actualFileID = fileInfo.Id;
                    _logger.LogInformation("✅ New CSV uploaded to Azure AI Agents. File ID: {FileId}", actualFileID);
               // }
               // else
               // {
                    // Usar FileID existente
                //    actualFileID = fileID;
                    _logger.LogInformation("🔄 Using existing FileID: {FileId} (skipping upload to save time)", actualFileID);
               // }

                // 5. Configurar modelo
                var modelDeploymentName = _configuration["Values:AzureOpenAI:DeploymentName"] ?? 
                                        _configuration["AzureOpenAI:DeploymentName"] ?? 
                                        "gpt4mini";

                // 6. Crear instrucciones especializadas para facturas
                var dynamicInstructions = CreateInvoiceAnalysisInstructions(vendorName, twinId);

                // 7. Crear el agente Azure AI con el FileID correcto
                PersistentAgent definition = await _aiAgentsClient.Administration.CreateAgentAsync(
                    modelDeploymentName,
                    name: $"Invoices Analyzer - {vendorName}",
                    instructions: dynamicInstructions,
                    tools: [new CodeInterpreterToolDefinition()],
                    toolResources: new()
                    {
                        CodeInterpreter = new()
                        {
                            FileIds = { actualFileID }
                        }
                    });

                AzureAIAgent agent = new(definition, _aiAgentsClient);
                AzureAIAgentThread thread = new(_aiAgentsClient);

                _logger.LogInformation("✅ Azure AI Agent created successfully. Agent ID: {AgentId}, Using FileID: {FileId}", 
                    agent.Id, actualFileID);

                try
                {
                    // 8. Ejecutar análisis con timeout
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    var result = await InvokeInvoiceAgentAsync(question, cts.Token, agent, thread);
                    
                    // 9. Extraer respuesta final del JSON
                    var finalResponse = ExtractFinalResponseFromJson(result);
                    
                    // 10. Crear respuesta elegante
                 //   var elegantResponse = await CreateInvoiceAnalysisResponse(question, vendorName, finalResponse);

                    _logger.LogInformation("✅ Invoice analysis completed successfully!");

                    return new CsvAnalysis
                    {
                        Question = question,
                        AIResponse = finalResponse,
                        FileID = actualFileID // Retornar el FileID actual (nuevo o existente)
                    };
                }
                finally
                {
                    // Cleanup resources
                    try
                    {
                        // Note: En producción, considerar mantener algunos recursos para reutilización
                        _logger.LogInformation("🧹 Cleaning up Azure AI resources...");
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx, "⚠️ Error during cleanup: {Message}", cleanupEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in AI invoice analysis");
                return new CsvAnalysis
                {
                    Question = question,
                    AIResponse = $"❌ Error durante el análisis de facturas: {ex.Message}",
                    FileID = ""
                };
            }
        }

        /// <summary>
        /// Crea las instrucciones especializadas para análisis de facturas
        /// </summary>
        private string CreateInvoiceAnalysisInstructions(string vendorName, string twinId)
        {
            return @$"Eres un analista financiero especializado en análisis de facturas con capacidades de interpretación de código.

CONTEXTO DE DATOS:

IMPORTANTE:
Has los calculos como se te pide usando el CSV file
En la busqueda de Description usa contains y mayusculas o miniusculas para encontrar
Description elimina espacios y hazlo bien
 
RESPUESTA FINAL:
Da la respuesat final RespuestaFinal en formato HTML elegante y profesional con oclores
corporativos y emojis financieros (💰, 📊, 📈, 🏢, 📋).
Después de completar el análisis, proporciona tu respuesta en formato JSON:
Esta es tu respuesta asi la quiero no quiero mas ni menos.
No incluyas descuentos ni inventes datos que no se te dan en el CSV
No incluyas la pregunta ni ningun dato mas solo el json EJMPLO:
En tu codigo asegurate de no incluir registros que son descuentos.

IMPORTANTE FORMATO HTML: 
- NO uses caracteres de escape como \n, \t, \r en el HTML
- El HTML debe ser limpio y compacto en una sola línea
- Usa espacios normales en lugar de \n para separar elementos
- NO incluyas saltos de línea literales en el JSON

{{
   ""RespuestaFinalHTML"": ""<div style='font-family: Arial, sans-serif; color: #004080; background-color: #f0f8ff; padding: 20px; border-radius: 10px;'><h2>Análisis Financiero</h2><p>Contenido aquí sin caracteres de escape</p></div>""
}}

Tu respuesta aqui:


"
;
        }

        /// <summary>
        /// Ejecuta el agente con timeout y manejo de errores
        /// </summary>
        private async Task<string> InvokeInvoiceAgentAsync(string question, CancellationToken cancellationToken, 
            AzureAIAgent agent, AzureAIAgentThread thread)
        {
            var responseBuilder = new StringBuilder();

            try
            {
                _logger.LogInformation("🤖 Starting invoice analysis with AI Agent...");

                var message = new ChatMessageContent(AuthorRole.User, question);
                responseBuilder.AppendLine($"❓ Pregunta: {question}");
                responseBuilder.AppendLine("📋 Análisis:");

                var responseCount = 0;
                var maxResponses = 20;

                await foreach (ChatMessageContent response in agent.InvokeAsync(message, thread))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    responseCount++;
                    if (responseCount > maxResponses)
                    {
                        _logger.LogWarning("🔄 Maximum response count reached");
                        break;
                    }

                    if (!string.IsNullOrEmpty(response.Content))
                    {
                        var role = response.Role == AuthorRole.Assistant ? "🤖 AI Agent" :
                                   response.Role == AuthorRole.Tool ? "🔧 Code Interpreter" : "📝 System";
                        
                        responseBuilder.AppendLine($"{role}: {response.Content}");
                    }

                    // Check for completion - buscar "RespuestaFinalHTML" o "RespuestaFinal"
                    if (response.Content?.Contains("\"RespuestaFinalHTML\"", StringComparison.OrdinalIgnoreCase) == true ||
                        response.Content?.Contains("\"RespuestaFinal\"", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _logger.LogInformation("🎯 Final response detected");
                        break;
                    }
                }

                return responseBuilder.ToString();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "❌ Error in invoice agent execution");
                responseBuilder.AppendLine($"❌ Error durante el análisis: {ex.Message}");
                return responseBuilder.ToString();
            }
        }

        /// <summary>
        /// Extrae la respuesta final del JSON del agente
        /// </summary>
        private string ExtractFinalResponseFromJson(string agentResponse)
        {
            try
            {
                _logger.LogInformation("🔍 Starting extraction of RespuestaFinalHTML from agent response...");
                
                // 🎯 ESTRATEGIA 1: Búsqueda directa con Regex de "RespuestaFinalHTML"
                var htmlMatch = System.Text.RegularExpressions.Regex.Match(
                    agentResponse, 
                    @"""RespuestaFinalHTML""\s*:\s*""([^""\\]*(\\.[^""\\]*)*)""", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

                if (htmlMatch.Success)
                {
                    var extractedHtml = htmlMatch.Groups[1].Value;
                    _logger.LogInformation("✅ Found RespuestaFinalHTML with direct regex search, length: {Length}", extractedHtml.Length);
                    var cleanedHtml = CleanHTMLFromJson(extractedHtml);
                    return cleanedHtml;
                }

                // 🎯 ESTRATEGIA 2: JObject parsing con búsqueda case-insensitive
                try
                {
                    // Buscar el JSON en el response
                    var jsonStartIndex = agentResponse.IndexOf('{');
                    var jsonEndIndex = agentResponse.LastIndexOf('}');
                    
                    if (jsonStartIndex >= 0 && jsonEndIndex > jsonStartIndex)
                    {
                        var jsonString = agentResponse.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex + 1);
                        _logger.LogInformation("📄 Extracted JSON substring, length: {Length}", jsonString.Length);
                        
                        var jsonObject = JObject.Parse(jsonString);
                        
                        // Buscar "RespuestaFinalHTML" (case-insensitive)
                        foreach (var property in jsonObject.Properties())
                        {
                            if (string.Equals(property.Name, "RespuestaFinalHTML", StringComparison.OrdinalIgnoreCase))
                            {
                                var htmlValue = property.Value?.ToString();
                                if (!string.IsNullOrEmpty(htmlValue))
                                {
                                    _logger.LogInformation("✅ Found RespuestaFinalHTML via JObject parsing, length: {Length}", htmlValue.Length);
                                    var cleanedHtml = CleanHTMLFromJson(htmlValue);
                                    return cleanedHtml;
                                }
                            }
                        }
                        
                        // Fallback a "RespuestaFinal" si existe
                        foreach (var property in jsonObject.Properties())
                        {
                            if (string.Equals(property.Name, "RespuestaFinal", StringComparison.OrdinalIgnoreCase))
                            {
                                var fallbackValue = property.Value?.ToString();
                                if (!string.IsNullOrEmpty(fallbackValue))
                                {
                                    _logger.LogInformation("⚠️ Using RespuestaFinal as fallback, length: {Length}", fallbackValue.Length);
                                    var cleanedHtml = CleanHTMLFromJson(fallbackValue);
                                    return cleanedHtml;
                                }
                            }
                        }
                    }
                }
                catch (Exception jsonEx)
                {
                    _logger.LogWarning(jsonEx, "⚠️ JObject parsing failed, trying alternative extraction");
                }

                // 🎯 ESTRATEGIA 3: Búsqueda simple de string con Contains
                if (agentResponse.Contains("RespuestaFinalHTML", StringComparison.OrdinalIgnoreCase))
                {
                    var startPattern = "\"RespuestaFinalHTML\":";
                    var startIndex = agentResponse.IndexOf(startPattern, StringComparison.OrdinalIgnoreCase);
                    
                    if (startIndex >= 0)
                    {
                        startIndex += startPattern.Length;
                        
                        // Buscar el inicio de la cadena (después de los espacios y comillas)
                        while (startIndex < agentResponse.Length && (agentResponse[startIndex] == ' ' || agentResponse[startIndex] == ':' || agentResponse[startIndex] == '"'))
                        {
                            startIndex++;
                        }
                        
                        if (startIndex < agentResponse.Length && agentResponse[startIndex - 1] == '"')
                        {
                            // Encontrar el final de la cadena HTML
                            var endIndex = startIndex;
                            var escapeNext = false;
                            
                            while (endIndex < agentResponse.Length)
                            {
                                if (escapeNext)
                                {
                                    escapeNext = false;
                                }
                                else if (agentResponse[endIndex] == '\\')
                                {
                                    escapeNext = true;
                                }
                                else if (agentResponse[endIndex] == '"')
                                {
                                    break;
                                }
                                endIndex++;
                            }
                            
                            if (endIndex > startIndex)
                            {
                                var extractedHtml = agentResponse.Substring(startIndex, endIndex - startIndex);
                                _logger.LogInformation("✅ Found RespuestaFinalHTML with string search, length: {Length}", extractedHtml.Length);
                                var cleanedHtml = CleanHTMLFromJson(extractedHtml);
                                return cleanedHtml;
                            }
                        }
                    }
                }

                _logger.LogWarning("⚠️ Could not find RespuestaFinalHTML in agent response");
                _logger.LogInformation("📋 Agent response sample: {Sample}", agentResponse.Substring(0, Math.Min(300, agentResponse.Length)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting RespuestaFinalHTML from response");
            }

            _logger.LogInformation("🔄 Returning original agent response as fallback");
            return agentResponse;
        }

        /// <summary>
        /// Limpia HTML que viene del JSON con caracteres de escape complejos
        /// </summary>
        /// <param name="htmlContent">HTML con posibles caracteres de escape</param>
        /// <returns>HTML limpio y bien formateado</returns>
        private string CleanHTMLFromJson(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return string.Empty;

            try
            {
                _logger.LogInformation("🧹 Cleaning HTML content from JSON");

                var cleanedHtml = htmlContent;

                // 🔧 PASO 1: Unescape caracteres JSON básicos
                cleanedHtml = cleanedHtml
                    .Replace("\\\"", "\"")     // Comillas escapadas
                    .Replace("\\/", "/")       // Slash escapado
                    .Replace("\\\\", "\\");    // Backslash escapado

                // 🔧 PASO 2: Convertir secuencias de escape literales
                cleanedHtml = cleanedHtml
                    .Replace("\\n", "\n")      // Nuevas líneas
                    .Replace("\\r", "\r")      // Retorno de carro
                    .Replace("\\t", "\t");     // Tabs

                // 🔧 PASO 3: Limpiar espacios múltiples y saltos de línea excesivos
                cleanedHtml = System.Text.RegularExpressions.Regex.Replace(cleanedHtml, @"\s+", " ");
                cleanedHtml = System.Text.RegularExpressions.Regex.Replace(cleanedHtml, @">\\s+<", "><");

                // 🔧 PASO 4: Asegurar que el HTML esté bien formateado
                cleanedHtml = cleanedHtml.Trim();

                // 🔧 PASO 5: Validar que sea HTML válido básico
                if (!cleanedHtml.StartsWith("<") || !cleanedHtml.EndsWith(">"))
                {
                    _logger.LogWarning("⚠️ HTML doesn't appear to be well-formed, wrapping in div");
                    cleanedHtml = $"<div style='font-family: Arial, sans-serif; padding: 20px;'>{cleanedHtml}</div>";
                }

                _logger.LogInformation("✅ HTML cleaned successfully, final length: {Length}", cleanedHtml.Length);
                return cleanedHtml;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error cleaning HTML content");
                
                // Fallback: retornar contenido básico sin caracteres problemáticos
                var fallbackHtml = htmlContent
                    .Replace("\\n", " ")
                    .Replace("\\\"", "\"")
                    .Replace("\\/", "/");

                return $"<div style='font-family: Arial, sans-serif; padding: 20px; background: #f9f9f9; border-radius: 8px;'><p>{fallbackHtml}</p></div>";
            }
        }

        /// <summary>
        /// Escapa valores para formato CSV, manejando comillas y comas
        /// </summary>
        /// <param name="value">Valor a escapar</param>
        /// <returns>Valor escapado para CSV</returns>
        private string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            // Si el valor contiene comillas, comas o saltos de línea, debe ir entre comillas
            if (value.Contains("\"") || value.Contains(",") || value.Contains("\n") || value.Contains("\r"))
            {
                // Escapar comillas duplicándolas y envolver en comillas
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }
    }
}
