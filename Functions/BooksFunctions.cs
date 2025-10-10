using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Agents;
using TwinFx.Services;
using TwinFx.Models;
using System.Text.Json;
using System.Text.Json.Serialization; 

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions para consultas inteligentes sobre libros y CRUD operations
    /// Utiliza AI para proporcionar información detallada sobre libros
    /// Container: TwinBooks, PartitionKey: TwinID
    /// </summary>
    public class BooksFunctions
    {
        private readonly ILogger<BooksFunctions> _logger;
        private readonly BooksAgent _booksAgent;
        private readonly BooksCosmosDbService _booksService;
        private readonly IConfiguration _configuration;

        public BooksFunctions(ILogger<BooksFunctions> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Initialize Books Agent
            var booksAgentLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<BooksAgent>();
            _booksAgent = new BooksAgent(booksAgentLogger, configuration);

            // Initialize Books Cosmos DB Service
            var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
            {
                Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? "",
                Key = configuration["Values:COSMOS_KEY"] ?? "",
                DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            });

            var booksServiceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<BooksCosmosDbService>();
            _booksService = new BooksCosmosDbService(booksServiceLogger, cosmosOptions);
        }

        /// <summary>
        /// Helper method to add CORS headers to HTTP context
        /// </summary>
        private static void AddCorsHeaders(HttpRequest req)
        {
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent, X-Requested-With");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Max-Age", "86400");
            req.HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Credentials", "false");
        }

        // ===== OPTIONS HANDLERS FOR CORS =====

        /// <summary>
        /// Handle CORS preflight for /api/twins/searchbook/{twinId}/books
        /// </summary>
        [Function("BooksSearchBookOptions")]
        public IActionResult HandleBooksSearchBookOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/searchbook/{twinId}/books")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("📚 Handling OPTIONS request for /api/twins/searchbook/{TwinId}/books", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/books/search
        /// </summary>
        [Function("BooksSearchOptions")]
        public IActionResult HandleBooksSearchOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/books/search")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("📚 Handling OPTIONS request for /api/twins/{TwinId}/books/search", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/books
        /// </summary>
        [Function("BooksOptions")]
        public IActionResult HandleBooksOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/books")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("📚 Handling OPTIONS request for /api/twins/{TwinId}/books", twinId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Handle CORS preflight for /api/twins/{twinId}/books/{bookId}
        /// </summary>
        [Function("BooksIdOptions")]
        public IActionResult HandleBooksIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/books/{bookId}")] HttpRequest req,
            string twinId, string bookId)
        {
            _logger.LogInformation("📚 Handling OPTIONS request for /api/twins/{TwinId}/books/{BookId}", twinId, bookId);
            AddCorsHeaders(req);
            return new OkResult();
        }

        // ===== AI SEARCH ENDPOINT =====

        /// <summary>
        /// Búsqueda inteligente de información sobre libros
        /// GET /api/twins/searchbook/{twinId}/books?question={question}
        /// </summary>
        [Function("SearchBooks")]
        public async Task<IActionResult> SearchBooks(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/searchbook/{twinId}/books")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("📚 Starting intelligent book search for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                // Obtener la pregunta del query string
                var question = req.Query["question"].FirstOrDefault();
                
                if (string.IsNullOrEmpty(question))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Question parameter is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                _logger.LogInformation("📖 Processing book question: {Question}", question);

                // Procesar la consulta usando el Books Agent
                var result = await _booksAgent.ProcessBookQuestionAsync(question, twinId);

                if (!result.Success)
                {
                    var badRequestResponse = new BadRequestObjectResult(new
                    {
                        success = false,
                        error = result.Error,
                        twinId = twinId,
                        question = question,
                        processingTimeMs = result.ProcessingTimeMs
                    });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                _logger.LogInformation("✅ Book search completed successfully for Twin: {TwinId}, ProcessingTime: {Time}ms", 
                    twinId, result.ProcessingTimeMs);

                return new OkObjectResult(new
                {
                    success = true,
                    data = new
                    {
                        question = result.Question,
                        answer = result.Answer,
                        bookInformation = result.BookInformation,
                        disclaimer = result.Disclaimer
                    },
                    twinId = result.TwinId,
                    processingTimeMs = result.ProcessingTimeMs,
                    processedAt = result.ProcessedAt,
                    message = "Book information retrieved successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in book search for Twin ID: {TwinId}", twinId);
                var errorResponse = new ObjectResult(new { 
                    error = ex.Message,
                    twinId = twinId
                })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }

        // ===== CRUD OPERATIONS =====

        /// <summary>
        /// Crear un nuevo libro
        /// POST /api/twins/{twinId}/books
        /// </summary>
        [Function("CreateBook")]
        public async Task<IActionResult> CreateBook(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/books")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("📚 Creating new book for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                // Leer el cuerpo de la petición
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Request body is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                BookMain? bookMain;
                try
                {
                    // Configurar opciones JSON para caracteres UTF-8 sin escape y conversiones flexibles
                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                    };
                    
                    bookMain = JsonSerializer.Deserialize<BookMain>(requestBody, jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "⚠️ Invalid JSON in request body");
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Invalid JSON format in request body" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                if (bookMain == null)
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Book data is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                // Asegurar que los campos requeridos tengan valores
                if (string.IsNullOrEmpty(bookMain.id))
                {
                    bookMain.id = Guid.NewGuid().ToString();
                }

                bookMain.createdAt = DateTime.UtcNow;
                bookMain.updatedAt = DateTime.UtcNow;

                _logger.LogInformation("📚 Processing book creation: {Title}",
                    bookMain.titulo ?? "Unknown Title");

                // Crear el libro en Cosmos DB
                var createSuccess = await _booksService.CreateBookMainAsync(bookMain, twinId);

                if (!createSuccess)
                {
                    var errorResponse = new ObjectResult(new { error = "Failed to create book in database" })
                    {
                        StatusCode = 500
                    };
                    AddCorsHeaders(req);
                    return errorResponse;
                }

                _logger.LogInformation("✅ Book created successfully for Twin: {TwinId}", twinId);

                return new ObjectResult(new
                {
                    success = true,
                    data = bookMain,
                    message = $"Book '{bookMain.titulo ?? "Unknown"}' created successfully"
                })
                {
                    StatusCode = 201
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating book for Twin: {TwinId}", twinId);
                var errorResponse = new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }

        /// <summary>
        /// Obtener todos los libros de un Twin
        /// GET /api/twins/{twinId}/books
        /// </summary>
        [Function("GetBooksByTwin")]
        public async Task<IActionResult> GetBooksByTwin(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/books")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("📚 Getting books for Twin ID: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Twin ID parameter is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                var books = await _booksService.GetBookMainsByTwinIdAsync(twinId);

                _logger.LogInformation("✅ Retrieved {Count} books for Twin ID: {TwinId}", books.Count, twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    data = books,
                    count = books.Count,
                    message = $"Retrieved {books.Count} books for Twin {twinId}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting books for Twin ID: {TwinId}", twinId);
                var errorResponse = new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }

        /// <summary>
        /// Obtener libro específico por ID
        /// GET /api/twins/{twinId}/books/{bookId}
        /// </summary>
        [Function("GetBookById")]
        public async Task<IActionResult> GetBookById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/books/{bookId}")] HttpRequest req,
            string twinId, string bookId)
        {
            _logger.LogInformation("📚 Getting book by ID: {BookId} for Twin: {TwinId}", bookId, twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(bookId))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Twin ID and Book ID parameters are required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                var bookDocument = await _booksService.GetBookMainByIdAsync(bookId, twinId);

                if (bookDocument == null)
                {
                    var notFoundResponse = new NotFoundObjectResult(new { error = $"Book with ID {bookId} not found for Twin {twinId}" });
                    AddCorsHeaders(req);
                    return notFoundResponse;
                }

                _logger.LogInformation("✅ Retrieved book: {Title} for Twin: {TwinId}",
                    bookDocument.BookMainData.titulo ?? "Unknown", twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    data = bookDocument,
                    message = $"Book '{bookDocument.BookMainData.titulo ?? "Unknown"}' retrieved successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting book by ID: {BookId} for Twin: {TwinId}", bookId, twinId);
                var errorResponse = new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }

        /// <summary>
        /// Actualizar libro existente
        /// PUT /api/twins/{twinId}/books/{bookId}
        /// </summary>
        [Function("UpdateBook")]
        public async Task<IActionResult> UpdateBook(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/books/{bookId}")] HttpRequest req,
            string twinId, string bookId)
        {
            _logger.LogInformation("📝 Updating book: {BookId} for Twin: {TwinId}", bookId, twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(bookId))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Twin ID and Book ID parameters are required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                // Leer el cuerpo de la petición
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Request body is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                BookMain? bookMain;
                try
                {
                    // Configurar opciones JSON para caracteres UTF-8 sin escape y conversiones flexibles
                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                    };
                    
                    bookMain = JsonSerializer.Deserialize<BookMain>(requestBody, jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "⚠️ Invalid JSON in request body");
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Invalid JSON format in request body" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                if (bookMain == null)
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Book data is required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                var updateSuccess = await _booksService.UpdateBookMainAsync(bookId, bookMain, twinId);

                if (!updateSuccess)
                {
                    var errorResponse = new ObjectResult(new { error = "Failed to update book or book not found" })
                    {
                        StatusCode = 500
                    };
                    AddCorsHeaders(req);
                    return errorResponse;
                }

                _logger.LogInformation("✅ Book updated successfully: {Title} for Twin: {TwinId}",
                    bookMain.titulo ?? "Unknown", twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    data = bookMain,
                    message = $"Book '{bookMain.titulo ?? "Unknown"}' updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating book: {BookId} for Twin: {TwinId}", bookId, twinId);
                var errorResponse = new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }

        /// <summary>
        /// Eliminar libro
        /// DELETE /api/twins/{twinId}/books/{bookId}
        /// </summary>
        [Function("DeleteBook")]
        public async Task<IActionResult> DeleteBook(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/books/{bookId}")] HttpRequest req,
            string twinId, string bookId)
        {
            _logger.LogInformation("🗑️ Deleting book: {BookId} for Twin: {TwinId}", bookId, twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(bookId))
                {
                    var badRequestResponse = new BadRequestObjectResult(new { error = "Twin ID and Book ID parameters are required" });
                    AddCorsHeaders(req);
                    return badRequestResponse;
                }

                // Primero obtener el libro para logging
                var existingBook = await _booksService.GetBookMainByIdAsync(bookId, twinId);

                var deleteSuccess = await _booksService.DeleteBookMainAsync(bookId, twinId);

                if (!deleteSuccess)
                {
                    var errorResponse = new ObjectResult(new { error = "Failed to delete book or book not found" })
                    {
                        StatusCode = 500
                    };
                    AddCorsHeaders(req);
                    return errorResponse;
                }

                _logger.LogInformation("✅ Book deleted successfully: {BookId} for Twin: {TwinId}", bookId, twinId);

                return new OkObjectResult(new
                {
                    success = true,
                    message = existingBook != null
                        ? $"Book '{existingBook.BookMainData.titulo ?? "Unknown"}' deleted successfully"
                        : $"Book {bookId} deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting book: {BookId} for Twin: {TwinId}", bookId, twinId);
                var errorResponse = new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
                AddCorsHeaders(req);
                return errorResponse;
            }
        }
    }
}