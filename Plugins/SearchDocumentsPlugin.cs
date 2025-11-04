using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using TwinFx.Services;

namespace TwinFx.Plugins;

/// <summary>
/// Search Documents plugin for Azure AI Search integration
/// Provides vector search capabilities for document retrieval and management
/// </summary>
public sealed class SearchDocumentsPlugin
{
    private readonly ILogger<SearchDocumentsPlugin> _logger;
    private readonly IConfiguration _configuration;
    private readonly AzureSearchService _searchService;
    private const string IndexName = "semistructured_documents_index";

    public SearchDocumentsPlugin(ILogger<SearchDocumentsPlugin>? logger = null, IConfiguration? configuration = null)
    {
        _logger = logger ?? LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SearchDocumentsPlugin>();
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        _searchService = new AzureSearchService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AzureSearchService>(),
            _configuration);
    }

    /// <summary>
    /// Search documents in Azure AI Search using vector similarity
    /// </summary>
    [KernelFunction, Description("Search documents in Azure AI Search using semantic vector search")]
    public async Task<string> SearchDocuments(
        [Description("Search query text to find relevant documents")] string query,
        [Description("Maximum number of results to return (default: 5)")] int top = 5)
    {
        try
        {
            _logger.LogInformation("?? Searching documents with query: {Query}, top: {Top}", query, top);

            // Perform vector search using the Azure Search Service
            var results = await _searchService.VectorSearchAsync(IndexName, query, top);

            if (results.Success && results.Results.Any())
            {
                var documents = results.Results;
                var formattedText = FormatDocumentsForDisplay(documents);

                _logger.LogInformation("? Found {Count} documents for query: {Query}", documents.Count, query);

                return formattedText;
            }
            else
            {
                var errorMessage = $"? No se encontraron documentos para: '{query}'\n\nError: {results.Error ?? "Búsqueda sin resultados"}";
                _logger.LogWarning("?? No documents found for query: {Query}. Error: {Error}", query, results.Error);
                
                return errorMessage;
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"? Error al buscar documentos: {ex.Message}";
            _logger.LogError(ex, "? Error searching documents with query: {Query}", query);
            
            return errorMessage;
        }
    }

    /// <summary>
    /// Get comprehensive search information including metadata
    /// </summary>
    [KernelFunction, Description("Get comprehensive search results with metadata and file information")]
    public async Task<string> GetSearchResultsWithMetadata(
        [Description("Search query text to find relevant documents")] string query,
        [Description("Maximum number of results to return (default: 5)")] int top = 5)
    {
        try
        {
            _logger.LogInformation("?? Getting search results with metadata for query: {Query}", query);

            var results = await _searchService.VectorSearchAsync(IndexName, query, top);

            if (results.Success && results.Results.Any())
            {
                var documents = results.Results;
                var fileNames = documents.Select(doc => doc.GetValueOrDefault("fileName")?.ToString() ?? "N/A").ToList();

                var response = $"?? **Búsqueda Completada:**\n\n";
                response += $"?? **Consulta:** {query}\n";
                response += $"?? **Resultados encontrados:** {documents.Count}\n";
                response += $"?? **Archivos:** {string.Join(", ", fileNames)}\n\n";
                response += FormatDocumentsForDisplay(documents);
                response += $"\n? **Estado:** Búsqueda exitosa\n";
                response += $"?? **Índice:** {IndexName}\n";

                return response;
            }
            else
            {
                return $"? **Búsqueda sin resultados**\n\n" +
                       $"?? **Consulta:** {query}\n" +
                       $"?? **Resultados:** 0\n" +
                       $"?? **Error:** {results.Error ?? "No se encontraron documentos relevantes"}\n" +
                       $"?? **Índice:** {IndexName}";
            }
        }
        catch (Exception ex)
        {
            return $"? **Error en búsqueda**\n\n" +
                   $"?? **Consulta:** {query}\n" +
                   $"?? **Error:** {ex.Message}\n" +
                   $"?? **Índice:** {IndexName}";
        }
    }

    /// <summary>
    /// List available document types in the search index
    /// </summary>
    [KernelFunction, Description("List available document types and statistics from the search index")]
    public async Task<string> GetDocumentStatistics()
    {
        try
        {
            _logger.LogInformation("?? Getting document statistics from index: {IndexName}", IndexName);

            // Check if the index exists
            var indexExists = await _searchService.IndexExistsAsync(IndexName);
            
            if (!indexExists)
            {
                return $"? **Índice no encontrado**\n\n" +
                       $"?? **Índice:** {IndexName}\n" +
                       $"?? **Estado:** El índice no existe o no está disponible\n" +
                       $"?? **Sugerencia:** Verificar configuración de Azure Search";
            }

            // For now, return basic index information
            // This could be enhanced to query the index for actual statistics
            return $"?? **Estadísticas del Índice de Documentos**\n\n" +
                   $"?? **Índice:** {IndexName}\n" +
                   $"? **Estado:** Disponible\n" +
                   $"?? **Capacidades:** Búsqueda vectorial semántica\n" +
                   $"?? **Tipos de campo:** texto plano, metadatos, vectores\n" +
                   $"??? **Servicio:** Azure AI Search\n\n" +
                   $"?? **Uso:** Utiliza la función SearchDocuments para buscar contenido específico";
        }
        catch (Exception ex)
        {
            return $"? **Error obteniendo estadísticas**\n\n" +
                   $"?? **Índice:** {IndexName}\n" +
                   $"?? **Error:** {ex.Message}";
        }
    }

    /// <summary>
    /// Check search service availability
    /// </summary>
    [KernelFunction, Description("Check if the Azure Search service is available and configured")]
    public async Task<string> CheckSearchServiceStatus()
    {
        try
        {
            _logger.LogInformation("?? Checking Azure Search service status");

            if (!_searchService.IsAvailable)
            {
                return $"? **Servicio no disponible**\n\n" +
                       $"?? **Estado:** Azure Search Service no configurado\n" +
                       $"?? **Solución:** Verificar configuración en local.settings.json\n" +
                       $"?? **Requerido:** AZURE_SEARCH_ENDPOINT, AZURE_SEARCH_API_KEY";
            }

            // Try to list indexes to verify connectivity
            var indexResult = await _searchService.ListIndexesAsync();
            
            if (indexResult.Success)
            {
                var indexExists = indexResult.Indexes.Contains(IndexName);
                
                return $"? **Servicio funcionando correctamente**\n\n" +
                       $"?? **Estado:** Azure Search Service conectado\n" +
                       $"?? **Índices totales:** {indexResult.Count}\n" +
                       $"?? **Índice objetivo:** {IndexName}\n" +
                       $"?? **Disponible:** {(indexExists ? "Sí" : "No")}\n" +
                       $"?? **Conectividad:** Exitosa";
            }
            else
            {
                return $"?? **Servicio con problemas**\n\n" +
                       $"?? **Estado:** Conectado pero con errores\n" +
                       $"? **Error:** {indexResult.Error}\n" +
                       $"?? **Sugerencia:** Verificar permisos y configuración";
            }
        }
        catch (Exception ex)
        {
            return $"? **Error verificando servicio**\n\n" +
                   $"?? **Error:** {ex.Message}\n" +
                   $"?? **Solución:** Verificar configuración y conectividad";
        }
    }

    /// <summary>
    /// Format documents for user-friendly display
    /// </summary>
    private static string FormatDocumentsForDisplay(List<Dictionary<string, object>> documents)
    {
        if (!documents.Any())
        {
            return "No se encontraron documentos relevantes.";
        }

        var formatted = $"?? **Documentos encontrados ({documents.Count}):**\n\n";

        for (int i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];
            var docNumber = i + 1;

            formatted += $"**Documento {docNumber}:**\n";
            formatted += $"?? **Archivo:** `{doc.GetValueOrDefault("fileName")?.ToString() ?? "N/A"}`\n";
            formatted += $"?? **Tipo:** {doc.GetValueOrDefault("documentType")?.ToString() ?? "N/A"}\n";
            formatted += $"?? **ID Twin:** {doc.GetValueOrDefault("twinId")?.ToString() ?? "N/A"}\n";
            formatted += $"?? **Procesado:** {doc.GetValueOrDefault("processedAt")?.ToString() ?? "N/A"}\n";
            
            // Add search score if available
            if (doc.ContainsKey("@search.score"))
            {
                var score = doc["@search.score"];
                formatted += $"?? **Relevancia:** {score:F3}\n";
            }

            // Add content preview
            var content = doc.GetValueOrDefault("reporteTextoPlano")?.ToString();
            if (!string.IsNullOrEmpty(content))
            {
                var preview = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                formatted += $"?? **Contenido:** {preview}\n";
            }

            formatted += "\n";
        }

        return formatted;
    }
}