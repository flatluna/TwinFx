using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TwinFx.Services
{
    /// <summary>
    /// Lightweight Azure Search service for chapter AI index (capitulos-ai-index)
    /// Only stores: id, TwinID, capituloId, Contenido
    /// Also supports a textoVector field for embeddings
    /// </summary>
    public class CapitulosAISearchIndex
    {
        private readonly ILogger<CapitulosAISearchIndex> _logger;
        private readonly IConfiguration _configuration;
        private readonly SearchIndexClient? _indexClient;
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private const string IndexName = "capitulos-ai-index";
        private const int EmbeddingDimensions = 1536;

        public CapitulosAISearchIndex(ILogger<CapitulosAISearchIndex> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _searchEndpoint = GetConfigurationValue("AZURE_SEARCH_ENDPOINT");
            _searchApiKey = GetConfigurationValue("AZURE_SEARCH_API_KEY");

            if (!string.IsNullOrEmpty(_searchEndpoint) && !string.IsNullOrEmpty(_searchApiKey))
            {
                try
                {
                    var credential = new AzureKeyCredential(_searchApiKey);
                    _indexClient = new SearchIndexClient(new Uri(_searchEndpoint), credential);
                    _logger.LogInformation("Capitulos AI SearchIndex client initialized");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initializing CapitulosAISearchIndex client");
                }
            }
            else
            {
                _logger.LogWarning("Azure Search configuration not found for CapitulosAISearchIndex");
            }
        }

        private string? GetConfigurationValue(string key, string? defaultValue = null)
        {
            var value = _configuration.GetValue<string>(key);
            if (string.IsNullOrEmpty(value)) value = _configuration.GetValue<string>($"Values:{key}");
            return !string.IsNullOrEmpty(value) ? value : defaultValue;
        }

        public bool IsAvailable => _indexClient != null;

        /// <summary>
        /// Create the simple capitulos-ai-index with only the four fields required by the user.
        /// Also adds a textoVector field for embeddings.
        /// </summary>
        public async Task<CapitulosIndexResult> CreateCapitulosAiIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new CapitulosIndexResult { Success = false, Error = "Azure Search service not available" };
                }

                _logger.LogInformation("Creating index {IndexName}", IndexName);

                var fields = new List<SearchField>
                {
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // TwinID for filtering
                    new SearchableField("TwinID")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // capituloId
                    new SearchableField("capituloId")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Contenido (searchable text)
                    new SearchableField("Contenido")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // textoVector for embeddings
                    new SearchField("textoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = EmbeddingDimensions
                    }
                };

                var index = new SearchIndex(IndexName, fields);
                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);

                _logger.LogInformation("Index {IndexName} created or updated", IndexName);

                return new CapitulosIndexResult
                {
                    Success = true,
                    IndexName = IndexName,
                    Message = "Index created or updated"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating capitulos-ai-index");
                return new CapitulosIndexResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Index a single capitulo document into capitulos-ai-index. Only the four fields are used plus optional textoVector.
        /// </summary>
        public async Task<CapitulosIndexResult> IndexCapituloAsync(CapitulosAiDocument doc)
        {
            try
            {
                if (!IsAvailable) return new CapitulosIndexResult { Success = false, Error = "Azure Search service not available" };

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                var document = new Dictionary<string, object>
                {
                    ["id"] = string.IsNullOrWhiteSpace(doc.Id) ? Guid.NewGuid().ToString() : doc.Id,
                    ["TwinID"] = doc.TwinID ?? string.Empty,
                    ["capituloId"] = doc.CapituloId ?? string.Empty,
                    ["Contenido"] = doc.Contenido ?? string.Empty
                };

                if (doc.TextoVector != null && doc.TextoVector.Length > 0)
                {
                    document["textoVector"] = doc.TextoVector;
                }

                var uploadResult = await searchClient.UploadDocumentsAsync(new[] { document });
                var first = uploadResult.Value.Results.FirstOrDefault();
                bool ok = first != null && first.Succeeded;

                if (ok)
                {
                    return new CapitulosIndexResult { Success = true, DocumentId = document["id"].ToString(), IndexName = IndexName };
                }

                var errorMessage = first?.ErrorMessage ?? "Unknown error";
                return new CapitulosIndexResult { Success = false, Error = errorMessage };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing capitulo document");
                return new CapitulosIndexResult { Success = false, Error = ex.Message };
            }
        }
    }

    public class CapitulosAiDocument
    {
        public string Id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string CapituloId { get; set; } = string.Empty;
        public string Contenido { get; set; } = string.Empty;
        public float[]? TextoVector { get; set; }
    }

    public class CapitulosIndexResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? IndexName { get; set; }
        public string? DocumentId { get; set; }
    }
}
