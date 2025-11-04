using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinFx.Services;
using TwinFx.Models;

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions for managing unstructured documents
    /// Provides endpoints for InvoiceDocument operations using UnStructuredDocumentsCosmosDB
    /// </summary>
    public class UnstructuredDocumentsFunctions
    {
        private readonly ILogger<UnstructuredDocumentsFunctions> _logger;
        private readonly IConfiguration _configuration;

        public UnstructuredDocumentsFunctions(ILogger<UnstructuredDocumentsFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // ========================================
        // OPTIONS HANDLERS FOR CORS
        // ========================================

        [Function("GetInvoicesMetadataOptions")]
        public async Task<HttpResponseData> HandleGetInvoicesMetadataOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "invoices-metadata/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📄 OPTIONS preflight request for invoices-metadata/{TwinId}", twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetInvoiceByIdOptions")]
        public async Task<HttpResponseData> HandleGetInvoiceByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "invoice/{twinId}/{documentId}")] HttpRequestData req,
            string twinId,
            string documentId)
        {
            _logger.LogInformation("📄 OPTIONS preflight request for invoice/{TwinId}/{DocumentId}", twinId, documentId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetInvoicesByVendorOptions")]
        public async Task<HttpResponseData> HandleGetInvoicesByVendorOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "invoices-vendor/{twinId}/{vendorName}")] HttpRequestData req,
            string twinId,
            string vendorName)
        {
            _logger.LogInformation("📄 OPTIONS preflight request for invoices-vendor/{TwinId}/{VendorName}", twinId, vendorName);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        // ========================================
        // INVOICE ENDPOINTS
        // ========================================

        /// <summary>
        /// Get specific InvoiceDocument by TwinID and DocumentID
        /// GET /api/invoice/{twinId}/{documentId}
        /// </summary>
        [Function("GetInvoiceById")]
        public async Task<HttpResponseData> GetInvoiceById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "invoice/{twinId}/{documentId}")] HttpRequestData req,
            string twinId,
            string documentId)
        {
            _logger.LogInformation("📄 GetInvoiceById function triggered for TwinId: {TwinId}, DocumentId: {DocumentId}", 
                twinId, documentId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(documentId))
                {
                    _logger.LogError("❌ Document ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Document ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🔍 Retrieving InvoiceDocument for TwinId: {TwinId}, DocumentId: {DocumentId}", 
                    twinId, documentId);

                // Create UnStructuredDocumentsCosmosDB service instance
                var cosmosService = CreateUnStructuredDocumentsCosmosService();

                // Get the specific InvoiceDocument using the query method
                var invoiceDocument = await cosmosService.GetInvoiceDocumentByIdWithQueryAsync(documentId, twinId);

                // Calculate processing time
                var processingTime = DateTime.UtcNow - startTime;

                if (invoiceDocument == null)
                {
                    _logger.LogWarning("⚠️ InvoiceDocument not found for TwinId: {TwinId}, DocumentId: {DocumentId}", 
                        twinId, documentId);

                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = $"Invoice document with ID '{documentId}' not found for twin '{twinId}'",
                        TwinId = twinId,
                        DocumentId = documentId,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                        RetrievedAt = DateTime.UtcNow
                    }));
                    return notFoundResponse;
                }

                // Create response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    DocumentId = documentId,
                    Invoice = invoiceDocument,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    RetrievedAt = DateTime.UtcNow,
                    Message = "Invoice document retrieved successfully",
                    Note = "This response contains the complete InvoiceDocument with all data including LineItems and AI analysis."
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Successfully retrieved InvoiceDocument for TwinId: {TwinId}, DocumentId: {DocumentId} in {Time:F2}s", 
                    twinId, documentId, processingTime.TotalSeconds);
                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error retrieving InvoiceDocument for TwinId: {TwinId}, DocumentId: {DocumentId}", 
                    twinId, documentId);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    DocumentId = documentId,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    RetrievedAt = DateTime.UtcNow
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Get invoice metadata for a specific TwinID (lightweight query)
        /// GET /api/invoices-metadata/{twinId}
        /// </summary>
        [Function("GetInvoicesMetadata")]
        public async Task<HttpResponseData> GetInvoicesMetadata(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "invoices-metadata/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📄 GetInvoicesMetadata function triggered for TwinId: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🔍 Retrieving invoice metadata for TwinId: {TwinId}", twinId);

                // Create UnStructuredDocumentsCosmosDB service instance
                var cosmosService = CreateUnStructuredDocumentsCosmosService();

                // Get invoice metadata for the specified TwinId (lightweight query)
                var invoicesMetadata = await cosmosService.GetInvoiceDocumentsMetadataByTwinIdAsync(twinId);

                // Calculate summary statistics
                var totalAmount = invoicesMetadata.Sum(i => i.InvoiceTotal);
                var avgAmount = invoicesMetadata.Any() ? invoicesMetadata.Average(i => i.InvoiceTotal) : 0;
                var totalTax = invoicesMetadata.Sum(i => i.TotalTax);
                var totalLineItems = invoicesMetadata.Sum(i => i.LineItemsCount);

                // Group by vendor for vendor summary
                var vendorSummary = invoicesMetadata
                    .Where(i => !string.IsNullOrEmpty(i.VendorName))
                    .GroupBy(i => i.VendorName)
                    .Select(g => new
                    {
                        VendorName = g.Key,
                        InvoiceCount = g.Count(),
                        TotalAmount = g.Sum(i => i.InvoiceTotal),
                        LatestInvoiceDate = g.Max(i => i.InvoiceDate)
                    })
                    .OrderByDescending(v => v.TotalAmount)
                    .Take(5)
                    .ToList();

                // Calculate processing time
                var processingTime = DateTime.UtcNow - startTime;

                // Create response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    TotalInvoices = invoicesMetadata.Count,
                    Invoices = invoicesMetadata,
                    Summary = new
                    {
                        TotalAmount = totalAmount,
                        AverageAmount = Math.Round(avgAmount, 2),
                        TotalTax = totalTax,
                        TotalLineItems = totalLineItems,
                        DateRange = GetDateRangeFromMetadata(invoicesMetadata),
                        TopVendors = vendorSummary
                    },
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    RetrievedAt = DateTime.UtcNow,
                    Message = $"Retrieved {invoicesMetadata.Count} invoice metadata records successfully",
                    Note = "This response contains only metadata (no invoice data or AI analysis) for faster access. Use full invoice endpoints for complete data."
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Successfully retrieved {Count} invoice metadata for TwinId: {TwinId} in {Time:F2}s", 
                    invoicesMetadata.Count, twinId, processingTime.TotalSeconds);
                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error retrieving invoice metadata for TwinId: {TwinId}", twinId);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    RetrievedAt = DateTime.UtcNow
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Get all invoices for a specific vendor by TwinID and VendorName
        /// GET /api/invoices-vendor/{twinId}/{vendorName}
        /// </summary>
        [Function("GetInvoicesByVendor")]
        public async Task<HttpResponseData> GetInvoicesByVendor(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "invoices-vendor/{twinId}/{vendorName}")] HttpRequestData req,
            string twinId,
            string vendorName)
        {
            _logger.LogInformation("📄 GetInvoicesByVendor function triggered for TwinId: {TwinId}, VendorName: {VendorName}", 
                twinId, vendorName);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(vendorName))
                {
                    _logger.LogError("❌ Vendor name parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        ErrorMessage = "Vendor name parameter is required"
                    }));
                    return badResponse;
                }

                // Decode URL-encoded vendor name
                var decodedVendorName = Uri.UnescapeDataString(vendorName);
                _logger.LogInformation("🔍 Retrieving invoices for TwinId: {TwinId}, VendorName: {VendorName}", 
                    twinId, decodedVendorName);

                // Create UnStructuredDocumentsCosmosDB service instance
                var cosmosService = CreateUnStructuredDocumentsCosmosService();

                // Get invoices for the specified vendor
                var invoiceDocuments = await cosmosService.GetInvoiceDocumentsByVendorNameAsync(twinId, decodedVendorName);

                // Calculate summary statistics
                var totalAmount = invoiceDocuments.Sum(i => i.InvoiceTotal);
                var avgAmount = invoiceDocuments.Any() ? invoiceDocuments.Average(i => i.InvoiceTotal) : 0;
                var totalTax = invoiceDocuments.Sum(i => i.TotalTax);
                var totalLineItems = invoiceDocuments.Sum(i => i.LineItemsCount);

                // Calculate processing time
                var processingTime = DateTime.UtcNow - startTime;

                // Create response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new
                {
                    Success = true,
                    TwinId = twinId,
                    VendorName = decodedVendorName,
                    TotalInvoices = invoiceDocuments.Count,
                    Invoices = invoiceDocuments,
                    Summary = new
                    {
                        TotalAmount = totalAmount,
                        AverageAmount = Math.Round(avgAmount, 2),
                        TotalTax = totalTax,
                        TotalLineItems = totalLineItems,
                        DateRange = GetVendorDateRange(invoiceDocuments),
                        LatestInvoice = invoiceDocuments.Any() ? invoiceDocuments.OrderByDescending(i => i.InvoiceDate).First().InvoiceDate : (DateTime?)null,
                        EarliestInvoice = invoiceDocuments.Any() ? invoiceDocuments.OrderBy(i => i.InvoiceDate).First().InvoiceDate : (DateTime?)null
                    },
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    RetrievedAt = DateTime.UtcNow,
                    Message = $"Retrieved {invoiceDocuments.Count} invoices for vendor '{decodedVendorName}' successfully",
                    Note = "This response contains complete InvoiceDocument objects with all data including LineItems and FileURL for direct access."
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Successfully retrieved {Count} invoices for vendor '{VendorName}' in {Time:F2}s", 
                    invoiceDocuments.Count, decodedVendorName, processingTime.TotalSeconds);
                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error retrieving invoices for vendor '{VendorName}'", vendorName);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    VendorName = vendorName,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    RetrievedAt = DateTime.UtcNow
                }));

                return errorResponse;
            }
        }

        // ========================================
        // HELPER METHODS
        // ========================================

        /// <summary>
        /// Create UnStructuredDocumentsCosmosDB service instance
        /// </summary>
        private UnStructuredDocumentsCosmosDB CreateUnStructuredDocumentsCosmosService()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<UnStructuredDocumentsCosmosDB>();
            return new UnStructuredDocumentsCosmosDB(cosmosLogger, _configuration);
        }

        /// <summary>
        /// Get date range string from invoice metadata
        /// </summary>
        private string GetDateRangeFromMetadata(List<InvoiceMetadata> invoices)
        {
            if (!invoices.Any())
                return "No hay facturas";

            var validDates = invoices.Where(i => i.InvoiceDate != DateTime.MinValue).ToList();
            if (!validDates.Any())
                return "Sin fechas válidas";

            var minDate = validDates.Min(i => i.InvoiceDate);
            var maxDate = validDates.Max(i => i.InvoiceDate);

            return $"{minDate:yyyy-MM-dd} a {maxDate:yyyy-MM-dd}";
        }

        /// <summary>
        /// Get date range string from invoice documents for vendor analysis
        /// </summary>
        private string GetVendorDateRange(List<TwinFx.Models.InvoiceDocument> invoices)
        {
            if (!invoices.Any())
                return "No hay facturas";

            var validDates = invoices.Where(i => i.InvoiceDate != DateTime.MinValue).ToList();
            if (!validDates.Any())
                return "Sin fechas válidas";

            var minDate = validDates.Min(i => i.InvoiceDate);
            var maxDate = validDates.Max(i => i.InvoiceDate);

            return $"{minDate:yyyy-MM-dd} a {maxDate:yyyy-MM-dd}";
        }

        /// <summary>
        /// Add CORS headers to response
        /// </summary>
        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            // Get origin from request headers
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

            // Allow specific origins for development
            var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173", "http://127.0.0.1:3000" };

            if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
            }
            else
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
            }

            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent");
            response.Headers.Add("Access-Control-Max-Age", "3600");
        }
    }
}
