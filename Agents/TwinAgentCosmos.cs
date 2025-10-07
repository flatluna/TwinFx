using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Models;
using TwinFx.Services;

namespace TwinFx.Agents;

/// <summary>
/// Agent specialized for Cosmos DB operations related to Twin travel documents
/// ========================================================================
/// 
/// Provides specialized queries and operations for travel documents:
/// - Get documents by activity ID
/// - Get documents by travel context
/// - Advanced filtering and search
/// - Statistical analysis of travel documents
/// 
/// This agent focuses on data retrieval and analysis from Cosmos DB
/// while leveraging the existing CosmosDbTwinProfileService infrastructure.
/// 
/// Author: TwinFx Project
/// Date: January 17, 2025
/// </summary>
public class TwinAgentCosmos : IDisposable
{
    private readonly ILogger<TwinAgentCosmos> _logger;
    private readonly IConfiguration _configuration;
    private readonly CosmosDbService _cosmosService;

    public TwinAgentCosmos(ILogger<TwinAgentCosmos> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        try
        {
            // Initialize Cosmos DB service with proper logger casting
            var cosmosLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbService>();
            _cosmosService = _configuration.CreateCosmosService(cosmosLogger);
            _logger.LogInformation("? TwinAgentCosmos initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to initialize TwinAgentCosmos");
            throw;
        }
    }

    /// <summary>
    /// Get all travel documents for a specific activity
    /// </summary>
    /// <param name="twinId">Twin ID (required)</param>
    /// <param name="activityId">Activity ID (required)</param>
    /// <returns>Response with travel documents for the activity</returns>
    public async Task<GetTravelDocumentsByActivityResponse> GetTravelDocumentsByActivityAsync(string twinId, string activityId)
    {
        _logger.LogInformation("?? Getting travel documents for Activity: {ActivityId}, Twin: {TwinId}", activityId, twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetTravelDocumentsByActivityResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID is required",
                    ProcessedAt = DateTime.UtcNow
                };
            }

            if (string.IsNullOrEmpty(activityId))
            {
                return new GetTravelDocumentsByActivityResponse
                {
                    Success = false,
                    ErrorMessage = "Activity ID is required",
                    ProcessedAt = DateTime.UtcNow
                };
            }

            // Get all travel documents for the twin
            var allDocuments = await _cosmosService.GetTravelDocumentsAsync(twinId);
            
            // Filter documents by activity ID
            var activityDocuments = allDocuments
                .Where(doc => !string.IsNullOrEmpty(doc.ActivityId) && doc.ActivityId.Equals(activityId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(doc => doc.CreatedAt)
                .ToList();

            _logger.LogInformation("? Found {Count} travel documents for Activity: {ActivityId}", 
                activityDocuments.Count, activityId);

            // Calculate statistics
            var stats = CalculateActivityDocumentStats(activityDocuments);

            return new GetTravelDocumentsByActivityResponse
            {
                Success = true,
                Message = $"Retrieved {activityDocuments.Count} travel documents for activity {activityId}",
                TwinId = twinId,
                ActivityId = activityId,
                Documents = activityDocuments,
                TotalDocuments = activityDocuments.Count,
                Statistics = stats,
                ProcessedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting travel documents for activity {ActivityId}", activityId);
            
            return new GetTravelDocumentsByActivityResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                TwinId = twinId,
                ActivityId = activityId,
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Get travel documents with advanced filtering by travel context
    /// </summary>
    /// <param name="twinId">Twin ID (required)</param>
    /// <param name="query">Query parameters for filtering</param>
    /// <returns>Response with filtered travel documents</returns>
    public async Task<GetTravelDocumentsByContextResponse> GetTravelDocumentsByContextAsync(string twinId, TravelDocumentQuery query)
    {
        _logger.LogInformation("?? Getting travel documents with context filters for Twin: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetTravelDocumentsByContextResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID is required",
                    ProcessedAt = DateTime.UtcNow
                };
            }

            // Get all travel documents for the twin
            var allDocuments = await _cosmosService.GetTravelDocumentsAsync(twinId);
            
            // Apply filters
            var filteredDocuments = ApplyTravelDocumentFilters(allDocuments, query);

            // Apply pagination
            var totalCount = filteredDocuments.Count();
            var pagedDocuments = filteredDocuments
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            _logger.LogInformation("? Found {Total} total documents, returning {Count} for page {Page}", 
                totalCount, pagedDocuments.Count, query.Page);

            // Calculate statistics
            var stats = CalculateAdvancedDocumentStats(filteredDocuments.ToList());

            return new GetTravelDocumentsByContextResponse
            {
                Success = true,
                Message = $"Retrieved {pagedDocuments.Count} travel documents (page {query.Page} of {Math.Ceiling((double)totalCount / query.PageSize)})",
                TwinId = twinId,
                Documents = pagedDocuments,
                TotalDocuments = totalCount,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / query.PageSize),
                Statistics = stats,
                AppliedFilters = GetAppliedFiltersDescription(query),
                ProcessedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting travel documents with context filters");
            
            return new GetTravelDocumentsByContextResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                TwinId = twinId,
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Get travel documents summary and analytics for a Twin
    /// </summary>
    /// <param name="twinId">Twin ID (required)</param>
    /// <returns>Response with comprehensive analytics</returns>
    public async Task<GetTravelDocumentsAnalyticsResponse> GetTravelDocumentsAnalyticsAsync(string twinId)
    {
        _logger.LogInformation("?? Getting travel documents analytics for Twin: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetTravelDocumentsAnalyticsResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID is required",
                    ProcessedAt = DateTime.UtcNow
                };
            }

            // Get all travel documents for the twin
            var allDocuments = await _cosmosService.GetTravelDocumentsAsync(twinId);
            
            _logger.LogInformation("? Analyzing {Count} travel documents for comprehensive analytics", allDocuments.Count);

            // Generate comprehensive analytics
            var analytics = GenerateComprehensiveAnalytics(allDocuments);

            return new GetTravelDocumentsAnalyticsResponse
            {
                Success = true,
                Message = $"Generated analytics for {allDocuments.Count} travel documents",
                TwinId = twinId,
                TotalDocuments = allDocuments.Count,
                Analytics = analytics,
                ProcessedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error generating travel documents analytics");
            
            return new GetTravelDocumentsAnalyticsResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                TwinId = twinId,
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Apply filters to travel documents based on query parameters
    /// </summary>
    private IEnumerable<TravelDocument> ApplyTravelDocumentFilters(List<TravelDocument> documents, TravelDocumentQuery query)
    {
        var filtered = documents.AsEnumerable();

        // Filter by travel ID
        if (!string.IsNullOrEmpty(query.TravelId))
        {
            filtered = filtered.Where(d => d.TravelId != null && d.TravelId.Equals(query.TravelId, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by itinerary ID
        if (!string.IsNullOrEmpty(query.ItineraryId))
        {
            filtered = filtered.Where(d => d.ItineraryId != null && d.ItineraryId.Equals(query.ItineraryId, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by activity ID
        if (!string.IsNullOrEmpty(query.ActivityId))
        {
            filtered = filtered.Where(d => d.ActivityId != null && d.ActivityId.Equals(query.ActivityId, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by document type
        if (query.DocumentType.HasValue)
        {
            filtered = filtered.Where(d => d.DocumentType == query.DocumentType.Value);
        }

        // Filter by establishment type
        if (query.EstablishmentType.HasValue)
        {
            filtered = filtered.Where(d => d.EstablishmentType == query.EstablishmentType.Value);
        }

        // Filter by date range
        if (query.FromDate.HasValue)
        {
            filtered = filtered.Where(d => d.DocumentDate >= query.FromDate.Value || d.CreatedAt >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            filtered = filtered.Where(d => d.DocumentDate <= query.ToDate.Value || d.CreatedAt <= query.ToDate.Value);
        }

        // Filter by amount range
        if (query.MinAmount.HasValue)
        {
            filtered = filtered.Where(d => d.TotalAmount >= query.MinAmount.Value);
        }

        if (query.MaxAmount.HasValue)
        {
            filtered = filtered.Where(d => d.TotalAmount <= query.MaxAmount.Value);
        }

        // Search term filter
        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            var searchTerm = query.SearchTerm.ToLowerInvariant();
            filtered = filtered.Where(d => 
                (d.Titulo != null && d.Titulo.ToLowerInvariant().Contains(searchTerm)) ||
                (d.VendorName != null && d.VendorName.ToLowerInvariant().Contains(searchTerm)) ||
                (d.Descripcion != null && d.Descripcion.ToLowerInvariant().Contains(searchTerm)) ||
                (d.AiSummary != null && d.AiSummary.ToLowerInvariant().Contains(searchTerm))
            );
        }

        // Apply sorting
        filtered = query.SortBy?.ToLowerInvariant() switch
        {
            "date" => query.SortDirection?.ToLowerInvariant() == "asc" 
                ? filtered.OrderBy(d => d.DocumentDate ?? d.CreatedAt)
                : filtered.OrderByDescending(d => d.DocumentDate ?? d.CreatedAt),
            "amount" => query.SortDirection?.ToLowerInvariant() == "asc"
                ? filtered.OrderBy(d => d.TotalAmount ?? 0)
                : filtered.OrderByDescending(d => d.TotalAmount ?? 0),
            "vendor" => query.SortDirection?.ToLowerInvariant() == "asc"
                ? filtered.OrderBy(d => d.VendorName ?? "")
                : filtered.OrderByDescending(d => d.VendorName ?? ""),
            _ => filtered.OrderByDescending(d => d.CreatedAt)
        };

        return filtered;
    }

    /// <summary>
    /// Calculate statistics for activity documents
    /// </summary>
    private ActivityDocumentStats CalculateActivityDocumentStats(List<TravelDocument> documents)
    {
        return new ActivityDocumentStats
        {
            TotalDocuments = documents.Count,
            TotalAmount = documents.Where(d => d.TotalAmount.HasValue).Sum(d => d.TotalAmount.Value),
            AverageAmount = documents.Where(d => d.TotalAmount.HasValue).DefaultIfEmpty().Average(d => d?.TotalAmount ?? 0),
            DocumentsByType = documents.GroupBy(d => d.DocumentType).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            DocumentsByEstablishment = documents.GroupBy(d => d.EstablishmentType).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            TopVendors = documents.Where(d => !string.IsNullOrEmpty(d.VendorName))
                                 .GroupBy(d => d.VendorName!)
                                 .OrderByDescending(g => g.Count())
                                 .Take(5)
                                 .ToDictionary(g => g.Key, g => g.Count()),
            MostRecentDocument = documents.OrderByDescending(d => d.CreatedAt).FirstOrDefault()?.CreatedAt,
            OldestDocument = documents.OrderBy(d => d.CreatedAt).FirstOrDefault()?.CreatedAt
        };
    }

    /// <summary>
    /// Calculate advanced statistics for document collection
    /// </summary>
    private AdvancedDocumentStats CalculateAdvancedDocumentStats(List<TravelDocument> documents)
    {
        var monthlySpending = documents
            .Where(d => d.DocumentDate.HasValue && d.TotalAmount.HasValue)
            .GroupBy(d => new { Year = d.DocumentDate!.Value.Year, Month = d.DocumentDate.Value.Month })
            .ToDictionary(
                g => $"{g.Key.Year}-{g.Key.Month:D2}",
                g => g.Sum(d => d.TotalAmount!.Value)
            );

        return new AdvancedDocumentStats
        {
            TotalDocuments = documents.Count,
            TotalAmount = documents.Where(d => d.TotalAmount.HasValue).Sum(d => d.TotalAmount.Value),
            AverageAmount = documents.Where(d => d.TotalAmount.HasValue).DefaultIfEmpty().Average(d => d?.TotalAmount ?? 0),
            DocumentsByType = documents.GroupBy(d => d.DocumentType).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            DocumentsByEstablishment = documents.GroupBy(d => d.EstablishmentType).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            MonthlySpending = monthlySpending,
            CurrencyBreakdown = documents.Where(d => !string.IsNullOrEmpty(d.Currency))
                                        .GroupBy(d => d.Currency!)
                                        .ToDictionary(g => g.Key, g => new CurrencyStats 
                                        { 
                                            Count = g.Count(), 
                                            TotalAmount = g.Where(d => d.TotalAmount.HasValue).Sum(d => d.TotalAmount.Value) 
                                        }),
            TopVendors = documents.Where(d => !string.IsNullOrEmpty(d.VendorName))
                                 .GroupBy(d => d.VendorName!)
                                 .OrderByDescending(g => g.Sum(d => d.TotalAmount ?? 0))
                                 .Take(10)
                                 .ToDictionary(g => g.Key, g => new VendorStats 
                                 { 
                                     Count = g.Count(), 
                                     TotalAmount = g.Sum(d => d.TotalAmount ?? 0) 
                                 }),
            DateRange = new DateRangeStats
            {
                EarliestDocument = documents.OrderBy(d => d.CreatedAt).FirstOrDefault()?.CreatedAt,
                LatestDocument = documents.OrderByDescending(d => d.CreatedAt).FirstOrDefault()?.CreatedAt,
                SpanInDays = documents.Any() ? 
                    (documents.Max(d => d.CreatedAt) - documents.Min(d => d.CreatedAt)).Days : 0
            }
        };
    }

    /// <summary>
    /// Generate comprehensive analytics for all travel documents
    /// </summary>
    private TravelDocumentAnalytics GenerateComprehensiveAnalytics(List<TravelDocument> documents)
    {
        var stats = CalculateAdvancedDocumentStats(documents);
        
        return new TravelDocumentAnalytics
        {
            Overview = new AnalyticsOverview
            {
                TotalDocuments = documents.Count,
                TotalSpent = stats.TotalAmount,
                AveragePerDocument = stats.AverageAmount,
                UniqueVendors = documents.Where(d => !string.IsNullOrEmpty(d.VendorName)).Select(d => d.VendorName).Distinct().Count(),
                DocumentsWithTravelContext = documents.Count(d => !string.IsNullOrEmpty(d.TravelId)),
                DocumentsWithItineraryContext = documents.Count(d => !string.IsNullOrEmpty(d.ItineraryId)),
                DocumentsWithActivityContext = documents.Count(d => !string.IsNullOrEmpty(d.ActivityId))
            },
            Breakdown = stats,
            Insights = GenerateDocumentInsights(documents)
        };
    }

    /// <summary>
    /// Generate insights from document analysis
    /// </summary>
    private List<string> GenerateDocumentInsights(List<TravelDocument> documents)
    {
        var insights = new List<string>();

        if (documents.Count == 0)
        {
            insights.Add("No travel documents found for analysis");
            return insights;
        }

        // Context analysis
        var documentsWithContext = documents.Count(d => !string.IsNullOrEmpty(d.ActivityId));
        var contextPercentage = (double)documentsWithContext / documents.Count * 100;
        insights.Add($"{contextPercentage:F1}% of documents are associated with specific activities");

        // Spending analysis
        var totalSpent = documents.Where(d => d.TotalAmount.HasValue).Sum(d => d.TotalAmount.Value);
        if (totalSpent > 0)
        {
            insights.Add($"Total documented spending: ${totalSpent:F2}");
            
            var avgSpending = totalSpent / documents.Count(d => d.TotalAmount.HasValue);
            insights.Add($"Average spending per document: ${avgSpending:F2}");
        }

        // Top establishment type
        var topEstablishment = documents.GroupBy(d => d.EstablishmentType)
                                       .OrderByDescending(g => g.Count())
                                       .FirstOrDefault();
        if (topEstablishment != null)
        {
            insights.Add($"Most common establishment type: {topEstablishment.Key} ({topEstablishment.Count()} documents)");
        }

        // Document frequency
        if (documents.Count > 1)
        {
            var timeSpan = documents.Max(d => d.CreatedAt) - documents.Min(d => d.CreatedAt);
            var docsPerDay = documents.Count / Math.Max(timeSpan.Days, 1);
            insights.Add($"Document upload frequency: {docsPerDay:F2} documents per day over {timeSpan.Days} days");
        }

        return insights;
    }

    /// <summary>
    /// Get description of applied filters
    /// </summary>
    private string GetAppliedFiltersDescription(TravelDocumentQuery query)
    {
        var filters = new List<string>();

        if (!string.IsNullOrEmpty(query.TravelId))
            filters.Add($"Travel: {query.TravelId}");
        
        if (!string.IsNullOrEmpty(query.ItineraryId))
            filters.Add($"Itinerary: {query.ItineraryId}");
        
        if (!string.IsNullOrEmpty(query.ActivityId))
            filters.Add($"Activity: {query.ActivityId}");
        
        if (query.DocumentType.HasValue)
            filters.Add($"Document Type: {query.DocumentType}");
        
        if (query.EstablishmentType.HasValue)
            filters.Add($"Establishment: {query.EstablishmentType}");
        
        if (query.FromDate.HasValue)
            filters.Add($"From: {query.FromDate:yyyy-MM-dd}");
        
        if (query.ToDate.HasValue)
            filters.Add($"To: {query.ToDate:yyyy-MM-dd}");
        
        if (query.MinAmount.HasValue)
            filters.Add($"Min Amount: ${query.MinAmount:F2}");
        
        if (query.MaxAmount.HasValue)
            filters.Add($"Max Amount: ${query.MaxAmount:F2}");
        
        if (!string.IsNullOrEmpty(query.SearchTerm))
            filters.Add($"Search: '{query.SearchTerm}'");

        return filters.Any() ? string.Join(", ", filters) : "No filters applied";
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        // CosmosDbTwinProfileService doesn't implement IDisposable, so no cleanup needed
        _logger.LogInformation("?? TwinAgentCosmos disposed");
    }
}

// ================================================================================================
// RESPONSE MODELS FOR TWIN AGENT COSMOS
// ================================================================================================

/// <summary>
/// Response for getting travel documents by activity
/// </summary>
public class GetTravelDocumentsByActivityResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public string ActivityId { get; set; } = string.Empty;
    public List<TravelDocument> Documents { get; set; } = new();
    public int TotalDocuments { get; set; }
    public ActivityDocumentStats? Statistics { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response for getting travel documents with context filtering
/// </summary>
public class GetTravelDocumentsByContextResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public List<TravelDocument> Documents { get; set; } = new();
    public int TotalDocuments { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public AdvancedDocumentStats? Statistics { get; set; }
    public string AppliedFilters { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response for travel documents analytics
/// </summary>
public class GetTravelDocumentsAnalyticsResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public TravelDocumentAnalytics? Analytics { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Query parameters for filtering travel documents
/// </summary>
public class TravelDocumentQuery
{
    public string? TravelId { get; set; }
    public string? ItineraryId { get; set; }
    public string? ActivityId { get; set; }
    public TravelDocumentType? DocumentType { get; set; }
    public EstablishmentType? EstablishmentType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "date";
    public string? SortDirection { get; set; } = "desc";
}

/// <summary>
/// Statistics for activity documents
/// </summary>
public class ActivityDocumentStats
{
    public int TotalDocuments { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AverageAmount { get; set; }
    public Dictionary<string, int> DocumentsByType { get; set; } = new();
    public Dictionary<string, int> DocumentsByEstablishment { get; set; } = new();
    public Dictionary<string, int> TopVendors { get; set; } = new();
    public DateTime? MostRecentDocument { get; set; }
    public DateTime? OldestDocument { get; set; }
}

/// <summary>
/// Advanced statistics for document collection
/// </summary>
public class AdvancedDocumentStats
{
    public int TotalDocuments { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AverageAmount { get; set; }
    public Dictionary<string, int> DocumentsByType { get; set; } = new();
    public Dictionary<string, int> DocumentsByEstablishment { get; set; } = new();
    public Dictionary<string, decimal> MonthlySpending { get; set; } = new();
    public Dictionary<string, CurrencyStats> CurrencyBreakdown { get; set; } = new();
    public Dictionary<string, VendorStats> TopVendors { get; set; } = new();
    public DateRangeStats DateRange { get; set; } = new();
}

/// <summary>
/// Currency statistics
/// </summary>
public class CurrencyStats
{
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// Vendor statistics
/// </summary>
public class VendorStats
{
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// Date range statistics
/// </summary>
public class DateRangeStats
{
    public DateTime? EarliestDocument { get; set; }
    public DateTime? LatestDocument { get; set; }
    public int SpanInDays { get; set; }
}

/// <summary>
/// Comprehensive travel document analytics
/// </summary>
public class TravelDocumentAnalytics
{
    public AnalyticsOverview Overview { get; set; } = new();
    public AdvancedDocumentStats Breakdown { get; set; } = new();
    public List<string> Insights { get; set; } = new();
}

/// <summary>
/// Analytics overview
/// </summary>
public class AnalyticsOverview
{
    public int TotalDocuments { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal AveragePerDocument { get; set; }
    public int UniqueVendors { get; set; }
    public int DocumentsWithTravelContext { get; set; }
    public int DocumentsWithItineraryContext { get; set; }
    public int DocumentsWithActivityContext { get; set; }
}