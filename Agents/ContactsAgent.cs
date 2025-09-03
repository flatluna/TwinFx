using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TwinFx.Agents;

/// <summary>
/// Contact metadata for storage and transfer
/// </summary>
public class ContactMetadata
{
    public string Id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string Relacion { get; set; } = string.Empty;
    public string Apodo { get; set; } = string.Empty;
    public string TelefonoMovil { get; set; } = string.Empty;
    public string TelefonoTrabajo { get; set; } = string.Empty;
    public string TelefonoCasa { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Cumpleanos { get; set; } = string.Empty;
    public string Notas { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "contact";
}

public class ContactsAgent
{
    private readonly ILogger<ContactsAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly CosmosDbTwinProfileService _cosmosService;
    private Kernel? _kernel;

    public ContactsAgent(ILogger<ContactsAgent> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _cosmosService = new CosmosDbTwinProfileService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
            _configuration);
        
        _logger.LogInformation("?? ContactsAgent initialized");
    }

    /// <summary>
    /// Process contact-related questions and searches
    /// </summary>
    public async Task<string> ProcessContactQuestionAsync(string question, string twinId, bool requiresAnalysis = false, bool requiresFiltering = false)
    {
        try
        {
            _logger.LogInformation("?? Processing contact question: {Question} for Twin ID: {TwinId}, RequiresAnalysis: {RequiresAnalysis}, RequiresFiltering: {RequiresFiltering}", 
                question, twinId, requiresAnalysis, requiresFiltering);

            List<ContactMetadata> contactDocuments;

            if (requiresFiltering)
            {
                // Apply filters based on the question
                _logger.LogInformation("?? Applying filters based on question");
                var searchParams = await ExtractContactSearchParametersAsync(question, twinId);
                _logger.LogInformation("?? Search parameters extracted: {SearchParams}", JsonSerializer.Serialize(searchParams));
                contactDocuments = await GetFilteredContactsAsync(twinId, searchParams);
                _logger.LogInformation("?? Filtered contacts returned: {Count} contacts", contactDocuments.Count);
            }
            else
            {
                // Get all contacts
                _logger.LogInformation("?? Getting all contacts (no filtering applied)");
                var allContactsResult = await GetContactsAsync(twinId);
                contactDocuments = allContactsResult.Success ? allContactsResult.Contacts : new List<ContactMetadata>();
                _logger.LogInformation("?? All contacts returned: {Count} contacts", contactDocuments.Count);
            }
            
            if (contactDocuments.Count == 0)
            {
                return $"?? No se encontraron contactos para el Twin ID: {twinId}" + 
                       (requiresFiltering ? " con los filtros especificados" : "");
            }

            // ? ACTUALIZADO: Log más detallado de datos reales para debugging
            _logger.LogInformation("?? DEBUG: Processing {Count} contacts for question: '{Question}'", contactDocuments.Count, question);
            foreach (var contact in contactDocuments)
            {
                _logger.LogInformation("?? Contact Found: {Name} {LastName} - Email: {Email} - Phone: {Phone} - Relation: {Relation} - Apodo: {Apodo}",
                    contact.Nombre, contact.Apellido, contact.Email, contact.TelefonoMovil, contact.Relacion, contact.Apodo);
            }

            string rawResult;
            
            if (requiresAnalysis)
            {
                // Complex analysis with AI
                _logger.LogInformation("?? Using AI for complex contact analysis");
                rawResult = await GenerateAIContactAnalysisAsync(contactDocuments, question);
            }
            else
            {
                // Simple display of contact information
                _logger.LogInformation("?? Using simple contact display without complex analysis");
                rawResult = await GenerateDirectContactResponseAsync(contactDocuments, question);
            }

            // Skip AI enhancement to preserve data integrity
            var enhancedResult = rawResult;

            var finalResult = $"""
?? **Análisis de Contactos**

{enhancedResult}

?? **Resumen:**
   • Twin ID: {twinId}
   • Total de contactos: {contactDocuments.Count}
   • Relaciones: {GetRelationsFromContacts(contactDocuments)}
   • Empresas: {GetCompaniesFromContacts(contactDocuments)}
   • Con teléfono móvil: {contactDocuments.Count(c => !string.IsNullOrEmpty(c.TelefonoMovil))}
   • Con email: {contactDocuments.Count(c => !string.IsNullOrEmpty(c.Email))}
   • Filtros aplicados: {(requiresFiltering ? "Sí" : "No")}
   • Análisis avanzado: {(requiresAnalysis ? "Sí" : "No")}
""";

            _logger.LogInformation("? Contact question processed successfully");
            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error processing contact question");
            return $"? Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Get contacts for a twin with optional filtering
    /// </summary>
    public async Task<ContactsResult> GetContactsAsync(string twinId, string? relacion = null, string? search = null)
    {
        try
        {
            _logger.LogInformation("?? Getting contacts for Twin ID: {TwinId}, Relacion: {Relacion}, Search: {Search}", 
                twinId, relacion, search);

            // Get contacts from Cosmos DB
            var contactDocuments = await _cosmosService.GetContactsByTwinIdAsync(twinId);

            // Apply filters if specified
            if (!string.IsNullOrEmpty(relacion))
            {
                contactDocuments = contactDocuments.Where(c => 
                    c.Relacion.Equals(relacion, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLowerInvariant();
                contactDocuments = contactDocuments.Where(c => 
                    c.Nombre.ToLowerInvariant().Contains(searchLower) ||
                    c.Apellido.ToLowerInvariant().Contains(searchLower) ||
                    c.Empresa.ToLowerInvariant().Contains(searchLower) ||
                    c.Email.ToLowerInvariant().Contains(searchLower) ||
                    c.Apodo.ToLowerInvariant().Contains(searchLower) ||
                    c.Relacion.ToLowerInvariant().Contains(searchLower)).ToList();
            }

            // Convert to ContactMetadata
            var contacts = contactDocuments.Select(doc => new ContactMetadata
            {
                Id = doc.Id,
                TwinID = doc.TwinID,
                Nombre = doc.Nombre,
                Apellido = doc.Apellido,
                Relacion = doc.Relacion,
                Apodo = doc.Apodo,
                TelefonoMovil = doc.TelefonoMovil,
                TelefonoTrabajo = doc.TelefonoTrabajo,
                TelefonoCasa = doc.TelefonoCasa,
                Email = doc.Email,
                Direccion = doc.Direccion,
                Empresa = doc.Empresa,
                Cargo = doc.Cargo,
                Cumpleanos = doc.Cumpleanos,
                Notas = doc.Notas,
                CreatedDate = doc.CreatedDate,
                Type = doc.Type
            }).ToList();

            _logger.LogInformation("? Retrieved {Count} contacts for Twin ID: {TwinId}", contacts.Count, twinId);
            
            return new ContactsResult 
            { 
                Success = true, 
                Contacts = contacts 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting contacts for Twin ID: {TwinId}", twinId);
            return new ContactsResult 
            { 
                Success = false, 
                ErrorMessage = ex.Message, 
                Contacts = new List<ContactMetadata>() 
            };
        }
    }

    /// <summary>
    /// Get filtered contacts based on search parameters
    /// </summary>
    private async Task<List<ContactMetadata>> GetFilteredContactsAsync(string twinId, ContactSearchParameters searchParams)
    {
        try
        {
            _logger.LogInformation("?? Starting contact filtering process");
            _logger.LogInformation("?? Search Parameters: {SearchParams}", JsonSerializer.Serialize(searchParams));
            
            // Fallback to getting all contacts first and filtering in memory
            var allContactsResult = await GetContactsAsync(twinId);
            if (!allContactsResult.Success)
                return new List<ContactMetadata>();

            var filteredContacts = allContactsResult.Contacts.AsEnumerable();

            // Apply relation filter
            if (!string.IsNullOrEmpty(searchParams.Relacion))
            {
                _logger.LogInformation("??? Applying relation filter: {Relacion}", searchParams.Relacion);
                filteredContacts = filteredContacts.Where(c => 
                    c.Relacion.Equals(searchParams.Relacion, StringComparison.OrdinalIgnoreCase));
            }

            // Apply text search filter
            if (!string.IsNullOrEmpty(searchParams.SearchText))
            {
                var searchLower = searchParams.SearchText.ToLowerInvariant();
                _logger.LogInformation("?? Applying text search filter: {SearchText}", searchLower);
                
                filteredContacts = filteredContacts.Where(c => 
                    c.Nombre.ToLowerInvariant().Contains(searchLower) ||
                    c.Apellido.ToLowerInvariant().Contains(searchLower) ||
                    c.Empresa.ToLowerInvariant().Contains(searchLower) ||
                    c.Email.ToLowerInvariant().Contains(searchLower) ||
                    c.Apodo.ToLowerInvariant().Contains(searchLower));
                
                // ? NUEVO: Logging de cada contacto que se evalúa
                var contactList = filteredContacts.ToList();
                _logger.LogInformation("?? After text filtering, found {Count} contacts", contactList.Count);
                foreach (var contact in contactList)
                {
                    _logger.LogInformation("? Filtered Contact: {Name} {LastName} matches '{SearchText}'", 
                        contact.Nombre, contact.Apellido, searchLower);
                }
            }

            // Apply company filter
            if (!string.IsNullOrEmpty(searchParams.Empresa))
            {
                var empresaLower = searchParams.Empresa.ToLowerInvariant();
                _logger.LogInformation("?? Applying company filter: {Empresa}", empresaLower);
                filteredContacts = filteredContacts.Where(c => 
                    c.Empresa.ToLowerInvariant().Contains(empresaLower));
            }

            var contacts = filteredContacts.ToList();

            // ? NUEVO: Validación de coherencia final
            if (!string.IsNullOrEmpty(searchParams.SearchText))
            {
                var searchLower = searchParams.SearchText.ToLowerInvariant();
                var unexpectedContacts = contacts.Where(c => 
                    !c.Nombre.ToLowerInvariant().Contains(searchLower) &&
                    !c.Apellido.ToLowerInvariant().Contains(searchLower) &&
                    !c.Apodo.ToLowerInvariant().Contains(searchLower)).ToList();
                
                if (unexpectedContacts.Any())
                {
                    _logger.LogWarning("?? FILTERING INCONSISTENCY: Found {Count} contacts that don't match search text '{SearchText}'", 
                        unexpectedContacts.Count, searchLower);
                    
                    foreach (var unexpected in unexpectedContacts)
                    {
                        _logger.LogWarning("? Unexpected Contact: {Name} {LastName} doesn't match '{SearchText}'", 
                            unexpected.Nombre, unexpected.Apellido, searchLower);
                    }
                }
            }

            return contacts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error filtering contacts");
            return new List<ContactMetadata>();
        }
    }

    /// <summary>
    /// Extract search parameters from natural language question using AI
    /// </summary>
    private async Task<ContactSearchParameters> ExtractContactSearchParametersAsync(string question, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Extracting contact search parameters from question: {Question}", question);

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            var extractionPrompt = $$"""
Analiza la siguiente pregunta sobre contactos y extrae los parámetros de búsqueda de manera inteligente:

PREGUNTA: {{question}}

FORMATO DE RESPUESTA (JSON):
{
  "relacion": "relación encontrada o null",
  "searchText": "palabras clave para búsqueda amplia o null", 
  "empresa": "empresa mencionada o null",
  "telefono": "criterio de teléfono o null",
  "email": "criterio de email o null",
  "ubicacion": "ubicación mencionada o null"
}

Responde ÚNICAMENTE con el JSON válido, sin explicaciones adicionales.
""";
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(extractionPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 400 },
                    { "temperature", 0.1 }
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var jsonResponse = response.Content?.Trim() ?? "{}";
            
            // Clean any markdown formatting
            if (jsonResponse.StartsWith("```json"))
                jsonResponse = jsonResponse.Substring(7).Trim();
            if (jsonResponse.StartsWith("```"))
                jsonResponse = jsonResponse.Substring(3).Trim();
            if (jsonResponse.EndsWith("```"))
                jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3).Trim();

            // Parse the JSON response
            var searchParams = JsonSerializer.Deserialize<ContactSearchParameters>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ContactSearchParameters();

            _logger.LogInformation("? Extracted contact search parameters: {Parameters}", JsonSerializer.Serialize(searchParams));
            return searchParams;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error extracting contact search parameters");
            return new ContactSearchParameters(); // Return empty parameters
        }
    }

    /// <summary>
    /// Generate AI-powered contact analysis
    /// </summary>
    private async Task<string> GenerateAIContactAnalysisAsync(List<ContactMetadata> contacts, string question)
    {
        try
        {
            _logger.LogInformation("?? Generating AI contact analysis for {Count} contacts", contacts.Count);
            return GenerateBasicContactSummary(contacts, question);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error generating AI contact analysis");
            return GenerateBasicContactSummary(contacts, question);
        }
    }

    /// <summary>
    /// Generate direct contact response without complex AI analysis
    /// </summary>
    private async Task<string> GenerateDirectContactResponseAsync(List<ContactMetadata> contacts, string question)
    {
        try
        {
            _logger.LogInformation("?? Generating direct contact response for {Count} contacts", contacts.Count);
            
            var htmlResponse = await GenerateContactHTMLDirectly(contacts, question);
            
            _logger.LogInformation("? Direct contact HTML response generated successfully");
            
            return htmlResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error generating direct contact response");
            return $"? Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Extract specific name from question for validation
    /// </summary>
    private string ExtractSpecificNameFromQuestion(string question)
    {
        var questionLower = question.ToLowerInvariant();
        
        // Lista de nombres comunes para detectar
        var commonNames = new[] { "jorge", "maria", "maría", "pedro", "juan", "luis", "ana", "carlos", "miguel", "sofia", "andrea", "pablo", "david", "diego", "fernando" };
        
        foreach (var name in commonNames)
        {
            if (questionLower.Contains(name))
            {
                return name;
            }
        }
        
        return string.Empty;
    }

    /// <summary>
    /// Generate HTML response using OpenAI instead of manual HTML construction
    /// </summary>
    private async Task<string> GenerateContactHTMLDirectly(List<ContactMetadata> contacts, string question)
    {
        try
        {
            _logger.LogInformation("?? Generating contact response using OpenAI for {Count} contacts", contacts.Count);

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            // Create JSON representation of contacts for AI analysis
            var contactsJson = JsonSerializer.Serialize(contacts.Select(c => new
            {
                Id = c.Id,
                Nombre = c.Nombre,
                Apellido = c.Apellido,
                Relacion = c.Relacion,
                Apodo = c.Apodo,
                TelefonoMovil = c.TelefonoMovil,
                TelefonoTrabajo = c.TelefonoTrabajo,
                TelefonoCasa = c.TelefonoCasa,
                Email = c.Email,
                Direccion = c.Direccion,
                Empresa = c.Empresa,
                Cargo = c.Cargo,
                Cumpleanos = c.Cumpleanos,
                Notas = c.Notas
            }), new JsonSerializerOptions { WriteIndented = true });

            var contactResponsePrompt = $@"Eres un asistente experto en presentar información de contactos. El usuario ha hecho esta pregunta sobre sus contactos:

PREGUNTA: {question}

DATOS DE CONTACTOS (JSON):
{contactsJson}

INSTRUCCIONES CRÍTICAS:
1. Responde la pregunta específica del usuario usando SOLO los datos proporcionados
2. Si el usuario busca un contacto específico que NO existe en los datos, informa claramente que no se encontró
3. Presenta los datos de manera clara y organizada en formato HTML profesional
4. Utiliza colores, emojis y formato HTML elegante para mejorar la presentación
5. Incluye tablas detalladas cuando sea apropiado
6. Mantén un tono personal y amigable
7. NO inventes datos que no estén en el JSON proporcionado
8. Si no hay contactos en los datos, informa que no se encontraron contactos

FORMATO DE RESPUESTA:
- Usa HTML con estilos CSS inline para colores y formato
- Crea tablas cuando sea apropiado para mostrar información de contactos
- Usa listas para organizar información
- Incluye emojis relevantes (??, ??, ??, ??, etc.)
- Destaca información importante con colores y negritas

CAMPOS DISPONIBLES REALES:
- Nombre y Apellido (nombres completos)
- Relacion (tipo de relación: familia, trabajo, etc.)
- Apodo (nombres informales)
- TelefonoMovil, TelefonoTrabajo, TelefonoCasa
- Email
- Direccion
- Empresa y Cargo
- Cumpleanos
- Notas

Responde directamente con el HTML completo y elegante mostrando la información de contactos relevante para la pregunta.";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(contactResponsePrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 3000,
                    ["temperature"] = 0.2
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "No se pudo generar respuesta de contactos.";
            
            _logger.LogInformation("? OpenAI contact response generated successfully");
            return aiResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error generating OpenAI contact response");
            
            // Fallback to basic summary if AI fails
            return GenerateBasicContactSummary(contacts, question);
        }
    }

    /// <summary>
    /// Initialize Semantic Kernel for AI operations
    /// </summary>
    private async Task InitializeKernelAsync()
    {
        if (_kernel != null)
            return; // Already initialized

        try
        {
            // Create kernel builder
            IKernelBuilder builder = Kernel.CreateBuilder();

            // Get Azure OpenAI configuration
            var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ?? 
                          _configuration.GetValue<string>("AzureOpenAI:Endpoint") ?? 
                          throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

            var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ?? 
                        _configuration.GetValue<string>("AzureOpenAI:ApiKey") ?? 
                        throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

            var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ?? 
                                _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ?? 
                                "gpt4mini";

            // Add Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Build the kernel
            _kernel = builder.Build();

            _logger.LogInformation("? Semantic Kernel initialized for ContactsAgent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to initialize Semantic Kernel for ContactsAgent");
            throw;
        }

        await Task.CompletedTask;
    }

    private string GetRelationsFromContacts(List<ContactMetadata> contacts)
    {
        return string.Join(", ", contacts.Select(c => c.Relacion).Where(r => !string.IsNullOrEmpty(r)).Distinct());
    }

    private string GetCompaniesFromContacts(List<ContactMetadata> contacts)
    {
        return string.Join(", ", contacts.Select(c => c.Empresa).Where(e => !string.IsNullOrEmpty(e)).Distinct().Take(5));
    }

    private string GenerateBasicContactSummary(List<ContactMetadata> contacts, string question)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"?? **Resumen Básico de Contactos** para la pregunta: *{question}*");
        sb.AppendLine();

        foreach (var contact in contacts)
        {
            sb.AppendLine($"- ?? {contact.Nombre} {contact.Apellido}");
            sb.AppendLine($"  - Relación: {contact.Relacion}");
            sb.AppendLine($"  - Teléfono: {contact.TelefonoMovil}");
            sb.AppendLine($"  - Email: {contact.Email}");
            sb.AppendLine();
        }

        sb.AppendLine($"?? **Total de contactos:** {contacts.Count}");
        
        return sb.ToString();
    }
}

/// <summary>
/// Contact search parameters
/// </summary>
public class ContactSearchParameters
{
    [JsonPropertyName("relacion")]
    public string? Relacion { get; set; }

    [JsonPropertyName("searchText")]
    public string? SearchText { get; set; }

    [JsonPropertyName("empresa")]
    public string? Empresa { get; set; }

    [JsonPropertyName("telefono")]
    public string? Telefono { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("ubicacion")]
    public string? Ubicacion { get; set; }
}

/// <summary>
/// Result for getting contacts
/// </summary>
public class ContactsResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<ContactMetadata> Contacts { get; set; } = new();
}