using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using TwinFx.Models;
using TwinFx.Agents;

namespace TwinFx.Services;

/// <summary>
/// Service class for managing Books in Cosmos DB
/// Container: TwinBooks, PartitionKey: TwinID
/// ========================================================================
/// 
/// Proporciona operaciones CRUD completas para libros con:
/// - Gestión de libros con Book class
/// - Sin mapeo - usa JsonSerializer directamente
/// - ID único y PartitionKey con TwinID
/// - Timestamps automáticos
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class BooksCosmosDbService
{
    private readonly ILogger<BooksCosmosDbService> _logger;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly Container _booksContainer;

    public BooksCosmosDbService(ILogger<BooksCosmosDbService> logger, IOptions<CosmosDbSettings> cosmosOptions)
    {
        _logger = logger;
        var cosmosSettings = cosmosOptions.Value;

        _logger.LogInformation("?? Initializing Books Cosmos DB Service");
        _logger.LogInformation($"   ?? Endpoint: {cosmosSettings.Endpoint}");
        _logger.LogInformation($"   ?? Database: {cosmosSettings.DatabaseName}");
        _logger.LogInformation($"   ?? Container: TwinBooks");

        if (string.IsNullOrEmpty(cosmosSettings.Key))
        {
            _logger.LogError("? COSMOS_KEY is required but not found in configuration");
            throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration");
        }

        if (string.IsNullOrEmpty(cosmosSettings.Endpoint))
        {
            _logger.LogError("? COSMOS_ENDPOINT is required but not found in configuration");
            throw new InvalidOperationException("COSMOS_ENDPOINT is required but not found in configuration");
        }

        try
        {
            _client = new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key);
            _database = _client.GetDatabase(cosmosSettings.DatabaseName);
            _booksContainer = _database.GetContainer("TwinBooks");
            
            _logger.LogInformation("? Books Cosmos DB Service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to initialize Books Cosmos DB client");
            throw;
        }
    }

    /// <summary>
    /// Crear un nuevo libro completo con BookMain
    /// </summary>
    public async Task<bool> CreateBookMainAsync(BookMain bookMain, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Creating BookMain for Twin ID: {TwinId}", twinId);

            // Crear el documento para Cosmos DB
            var bookMainDocument = new BookMainDocument
            {
                id = bookMain.id ?? Guid.NewGuid().ToString(),
                TwinID = twinId,
                BookMainData = bookMain,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _booksContainer.CreateItemAsync(bookMainDocument, new PartitionKey(twinId));

            _logger.LogInformation("? BookMain created successfully with ID: {BookId} for Twin: {TwinId}", 
                bookMainDocument.id, twinId);

            // ?? Indexar automáticamente en books-literature-index usando BooksSearchIndex
            try
            {
                _logger.LogDebug("?? Indexing BookMain in books-literature-index for BookID: {BookId}", bookMainDocument.id);

                // Crear instancia del BooksSearchIndex para indexar análisis de libros
                var booksSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<BooksSearchIndex>();

                // Obtener configuración desde el entorno
                var config = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();

                var booksSearchIndex = new BooksSearchIndex(booksSearchLogger, config);

                if (booksSearchIndex.IsAvailable)
                {
                    // Calcular tiempo de procesamiento basado en cuánto tardó en guardarse
                    var processingTimeMs = (DateTime.UtcNow - bookMainDocument.CreatedAt).TotalMilliseconds;

                    // Indexar el libro en Azure AI Search
                    var indexResult = await booksSearchIndex.IndexBookAnalysisFromBookMainAsync(
                        bookMain, twinId, processingTimeMs);

                    if (indexResult.Success)
                    {
                        _logger.LogInformation("? BookMain indexed successfully in books-literature-index: DocumentId={DocumentId}", 
                            indexResult.DocumentId);
                    }
                    else
                    {
                        _logger.LogWarning("?? Failed to index BookMain in books-literature-index: {Error}", 
                            indexResult.Error);
                    }
                }
                else
                {
                    _logger.LogDebug("?? BooksSearchIndex not available for indexing");
                }
            }
            catch (Exception indexEx)
            {
                _logger.LogWarning(indexEx, "?? Error indexing BookMain in books-literature-index: {BookId}. Cosmos DB operation was successful.", 
                    bookMainDocument.id);
                // No fallar la operación principal de Cosmos DB, solo logear el warning del índice
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to create BookMain for Twin: {TwinId}", twinId);
            return false;
        }
    }

    /// <summary>
    /// Obtener todos los libros BookMain de un twin
    /// </summary>
    public async Task<List<BookMainDocument>> GetBookMainsByTwinIdAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("?? Getting all BookMain for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.BookMainData != null ORDER BY c.CreatedAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = _booksContainer.GetItemQueryIterator<BookMainDocument>(query);
            var books = new List<BookMainDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                books.AddRange(response);
            }

            _logger.LogInformation("? Retrieved {Count} BookMain for Twin ID: {TwinId}", books.Count, twinId);
            return books;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting BookMain for Twin: {TwinId}", twinId);
            return new List<BookMainDocument>();
        }
    }

    /// <summary>
    /// Obtener BookMain específico por ID
    /// </summary>
    public async Task<BookMainDocument?> GetBookMainByIdAsync(string bookId, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Getting BookMain by ID: {BookId} for Twin: {TwinId}", bookId, twinId);

            var response = await _booksContainer.ReadItemAsync<BookMainDocument>(bookId, new PartitionKey(twinId));
            
            _logger.LogInformation("? BookMain retrieved successfully: {BookId}", bookId);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("?? BookMain not found: {BookId} for Twin: {TwinId}", bookId, twinId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting BookMain by ID: {BookId} for Twin: {TwinId}", bookId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Actualizar BookMain existente
    /// </summary>
    public async Task<bool> UpdateBookMainAsync(string bookId, BookMain bookMain, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Updating BookMain: {BookId} for Twin: {TwinId}", bookId, twinId);

            // Obtener el documento existente
            var existingDoc = await GetBookMainByIdAsync(bookId, twinId);
            if (existingDoc == null)
            {
                _logger.LogWarning("?? BookMain not found for update: {BookId}", bookId);
                return false;
            }

            // Actualizar los datos manteniendo el createdAt original
            existingDoc.BookMainData = bookMain;
            existingDoc.BookMainData.updatedAt = DateTime.UtcNow;
            existingDoc.UpdatedAt = DateTime.UtcNow;

            await _booksContainer.UpsertItemAsync(existingDoc, new PartitionKey(twinId));

            _logger.LogInformation("? BookMain updated successfully: {BookId}", bookId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to update BookMain: {BookId} for Twin: {TwinId}", bookId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Eliminar BookMain
    /// </summary>
    public async Task<bool> DeleteBookMainAsync(string bookId, string twinId)
    {
        try
        {
            _logger.LogInformation("??? Deleting BookMain: {BookId} for Twin: {TwinId}", bookId, twinId);

            await _booksContainer.DeleteItemAsync<BookMainDocument>(bookId, new PartitionKey(twinId));

            _logger.LogInformation("? BookMain deleted successfully: {BookId}", bookId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("?? BookMain not found for deletion: {BookId}", bookId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error deleting BookMain: {BookId} for Twin: {TwinId}", bookId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Crear un nuevo libro
    /// </summary>
    public async Task<bool> CreateBookAsync(Book book, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Creating book for Twin ID: {TwinId}", twinId);

            // Crear el documento para Cosmos DB
            var bookDocument = new BookDocument
            {
                id = Guid.NewGuid().ToString(),
                TwinID = twinId,
                BookData = book,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _booksContainer.CreateItemAsync(bookDocument, new PartitionKey(twinId));

            _logger.LogInformation("? Book created successfully with ID: {BookId} for Twin: {TwinId}", 
                bookDocument.id, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to create book for Twin: {TwinId}", twinId);
            return false;
        }
    }

    /// <summary>
    /// Obtener todos los libros de un twin
    /// </summary>
    public async Task<List<BookDocument>> GetBooksByTwinIdAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("?? Getting all books for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.CreatedAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = _booksContainer.GetItemQueryIterator<BookDocument>(query);
            var books = new List<BookDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                books.AddRange(response);
            }

            _logger.LogInformation("? Retrieved {Count} books for Twin ID: {TwinId}", books.Count, twinId);
            return books;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting books for Twin: {TwinId}", twinId);
            return new List<BookDocument>();
        }
    }

    /// <summary>
    /// Obtener libro específico por ID
    /// </summary>
    public async Task<BookDocument?> GetBookByIdAsync(string bookId, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Getting book by ID: {BookId} for Twin: {TwinId}", bookId, twinId);

            var response = await _booksContainer.ReadItemAsync<BookDocument>(bookId, new PartitionKey(twinId));
            
            _logger.LogInformation("? Book retrieved successfully: {BookId}", bookId);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("?? Book not found: {BookId} for Twin: {TwinId}", bookId, twinId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting book by ID: {BookId} for Twin: {TwinId}", bookId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Actualizar libro existente
    /// </summary>
    public async Task<bool> UpdateBookAsync(string bookId, Book book, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Updating book: {BookId} for Twin: {TwinId}", bookId, twinId);

            // Obtener el documento existente
            var existingDoc = await GetBookByIdAsync(bookId, twinId);
            if (existingDoc == null)
            {
                _logger.LogWarning("?? Book not found for update: {BookId}", bookId);
                return false;
            }

            // Actualizar los datos
            existingDoc.BookData = book;
            existingDoc.UpdatedAt = DateTime.UtcNow;

            await _booksContainer.UpsertItemAsync(existingDoc, new PartitionKey(twinId));

            _logger.LogInformation("? Book updated successfully: {BookId}", bookId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to update book: {BookId} for Twin: {TwinId}", bookId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Eliminar libro
    /// </summary>
    public async Task<bool> DeleteBookAsync(string bookId, string twinId)
    {
        try
        {
            _logger.LogInformation("??? Deleting book: {BookId} for Twin: {TwinId}", bookId, twinId);

            await _booksContainer.DeleteItemAsync<BookDocument>(bookId, new PartitionKey(twinId));

            _logger.LogInformation("? Book deleted successfully: {BookId}", bookId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("?? Book not found for deletion: {BookId}", bookId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error deleting book: {BookId} for Twin: {TwinId}", bookId, twinId);
            return false;
        }
    }
}

/// <summary>
/// Documento de libro para Cosmos DB con metadatos
/// </summary>
public class BookDocument
{
    public string id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public Book BookData { get; set; } = new Book();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Documento de BookMain para Cosmos DB con metadatos
/// </summary>
public class BookMainDocument
{
    public string id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public BookMain BookMainData { get; set; } = new BookMain();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}