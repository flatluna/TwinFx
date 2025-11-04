using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TwinFx.Models;

// ========================================================================================
// JOB OPPORTUNITY DATA MODELS
// ========================================================================================

/// <summary>
/// Job opportunity data model for tracking job applications
/// </summary>
public class JobOpportunityData
{
    /// <summary>
    /// Unique identifier for the job opportunity
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Company name (required, minimum 2 characters)
    /// </summary>
    [JsonPropertyName("empresa")]
    [Required(ErrorMessage = "Company name is required")]
    [MinLength(2, ErrorMessage = "Company name must be at least 2 characters")]
    public string Empresa { get; set; } = string.Empty;

    /// <summary>
    /// Company website URL (optional)
    /// </summary>
    [JsonPropertyName("urlCompany")]
    [Url(ErrorMessage = "Company URL must be a valid URL")]
    public string? URLCompany { get; set; }

    /// <summary>
    /// Job position/title (required, minimum 2 characters)
    /// </summary>
    [JsonPropertyName("puesto")]
    [Required(ErrorMessage = "Job position is required")]
    [MinLength(2, ErrorMessage = "Job position must be at least 2 characters")]
    public string Puesto { get; set; } = string.Empty;

    /// <summary>
    /// General job description (optional)
    /// </summary>
    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; set; }

    /// <summary>
    /// Job responsibilities and duties (optional)
    /// </summary>
    [JsonPropertyName("responsabilidades")]
    public string? Responsabilidades { get; set; }

    /// <summary>
    /// Required skills and qualifications (optional)
    /// </summary>
    [JsonPropertyName("habilidadesRequeridas")]
    public string? HabilidadesRequeridas { get; set; }

    /// <summary>
    /// Salary range as free text (optional)
    /// </summary>
    [JsonPropertyName("salario")]
    public string? Salario { get; set; }

    /// <summary>
    /// Employee benefits description (optional)
    /// </summary>
    [JsonPropertyName("beneficios")]
    public string? Beneficios { get; set; }

    /// <summary>
    /// Location (city/country) (optional)
    /// </summary>
    [JsonPropertyName("ubicacion")]
    public string? Ubicacion { get; set; }

    /// <summary>
    /// Date when application was submitted
    /// </summary>
    [JsonPropertyName("fechaAplicacion")]
    public DateTime? FechaAplicacion { get; set; }

    /// <summary>
    /// Application status: 'aplicado', 'entrevista', 'esperando', 'rechazado', 'aceptado'
    /// </summary>
    [JsonPropertyName("estado")]
    public JobApplicationStatus Estado { get; set; } = JobApplicationStatus.Aplicado;

    /// <summary>
    /// Recruiter/contact person name (optional)
    /// </summary>
    [JsonPropertyName("contactoNombre")]
    public string? ContactoNombre { get; set; }

    /// <summary>
    /// Contact email (optional, must be valid email if provided)
    /// </summary>
    [JsonPropertyName("contactoEmail")]
    [EmailAddress(ErrorMessage = "Contact email must be a valid email address")]
    public string? ContactoEmail { get; set; }

    /// <summary>
    /// Contact phone number (optional)
    /// </summary>
    [JsonPropertyName("contactoTelefono")]
    public string? ContactoTelefono { get; set; }

    /// <summary>
    /// Additional notes (optional)
    /// </summary>
    [JsonPropertyName("notas")]
    public string? Notas { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    [JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    [JsonPropertyName("fechaActualizacion")]
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User ID for multi-tenant support (required)
    /// </summary>
    [JsonPropertyName("usuarioId")]
    [Required(ErrorMessage = "User ID is required")]
    public string UsuarioId { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID for integration with TwinFx ecosystem
    /// </summary>
    [JsonPropertyName("TwinID")]
    public string? TwinID { get; set; }

    /// <summary>
    /// Document type for Cosmos DB partitioning
    /// </summary>
    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = "jobOpportunity";

    /// <summary>
    /// Convert Dictionary from Cosmos DB to JobOpportunityData
    /// </summary>
    public static JobOpportunityData FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            try
            {
                if (value is T directValue)
                    return directValue;

                if (value is System.Text.Json.JsonElement jsonElement)
                {
                    var type = typeof(T);
                    if (type == typeof(string))
                        return (T)(object)(jsonElement.GetString() ?? string.Empty);
                    if (type == typeof(DateTime))
                    {
                        if (DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                            return (T)(object)dateTime;
                        return defaultValue;
                    }
                    if (type == typeof(DateTime?))
                    {
                        if (DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                            return (T)(object)dateTime;
                        return defaultValue;
                    }
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        JobApplicationStatus GetEstado(string key)
        {
            var estadoStr = GetValue<string>(key);
            if (Enum.TryParse<JobApplicationStatus>(estadoStr, true, out var estado))
                return estado;
            return JobApplicationStatus.Aplicado;
        }

        return new JobOpportunityData
        {
            Id = GetValue("id", ""),
            Empresa = GetValue<string>("empresa"),
            URLCompany = GetValue<string?>("urlCompany"),
            Puesto = GetValue<string>("puesto"),
            Descripcion = GetValue<string?>("descripcion"),
            Responsabilidades = GetValue<string?>("responsabilidades"),
            HabilidadesRequeridas = GetValue<string?>("habilidadesRequeridas"),
            Salario = GetValue<string?>("salario"),
            Beneficios = GetValue<string?>("beneficios"),
            Ubicacion = GetValue<string?>("ubicacion"),
            FechaAplicacion = GetValue<DateTime?>("fechaAplicacion"),
            Estado = GetEstado("estado"),
            ContactoNombre = GetValue<string?>("contactoNombre"),
            ContactoEmail = GetValue<string?>("contactoEmail"),
            ContactoTelefono = GetValue<string?>("contactoTelefono"),
            Notas = GetValue<string?>("notas"),
            FechaCreacion = GetValue("fechaCreacion", DateTime.UtcNow),
            FechaActualizacion = GetValue("fechaActualizacion", DateTime.UtcNow),
            UsuarioId = GetValue<string>("usuarioId"),
            TwinID = GetValue<string?>("TwinID"),
            DocumentType = GetValue("documentType", "jobOpportunity")
        };
    }

    /// <summary>
    /// Convert JobOpportunityData to Dictionary for Cosmos DB
    /// </summary>
    public Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["empresa"] = Empresa,
            ["urlCompany"] = URLCompany,
            ["puesto"] = Puesto,
            ["descripcion"] = Descripcion,
            ["responsabilidades"] = Responsabilidades,
            ["habilidadesRequeridas"] = HabilidadesRequeridas,
            ["salario"] = Salario,
            ["beneficios"] = Beneficios,
            ["ubicacion"] = Ubicacion,
            ["fechaAplicacion"] = FechaAplicacion?.ToString("O"),
            ["estado"] = Estado.ToString().ToLowerInvariant(),
            ["contactoNombre"] = ContactoNombre,
            ["contactoEmail"] = ContactoEmail,
            ["contactoTelefono"] = ContactoTelefono,
            ["notas"] = Notas,
            ["fechaCreacion"] = FechaCreacion.ToString("O"),
            ["fechaActualizacion"] = FechaActualizacion.ToString("O"),
            ["usuarioId"] = UsuarioId,
            ["TwinID"] = TwinID,
            ["documentType"] = DocumentType
        };
    }
}

/// <summary>
/// Job application status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobApplicationStatus
{
    [JsonPropertyName("aplicado")]
    Aplicado,

    [JsonPropertyName("entrevista")]
    Entrevista,

    [JsonPropertyName("esperando")]
    Esperando,

    [JsonPropertyName("rechazado")]
    Rechazado,

    [JsonPropertyName("aceptado")]
    Aceptado
}

// ========================================================================================
// API REQUEST/RESPONSE MODELS
// ========================================================================================

/// <summary>
/// Request model for creating a new job opportunity
/// </summary>
public class CreateJobOpportunityRequest
{
    /// <summary>
    /// Company name (required)
    /// </summary>
    [Required(ErrorMessage = "Company name is required")]
    [MinLength(2, ErrorMessage = "Company name must be at least 2 characters")]
    public string Empresa { get; set; } = string.Empty;

    /// <summary>
    /// Company website URL (optional)
    /// </summary>
    [Url(ErrorMessage = "Company URL must be a valid URL")]
    public string? URLCompany { get; set; }

    /// <summary>
    /// Job position (required)
    /// </summary>
    [Required(ErrorMessage = "Job position is required")]
    [MinLength(2, ErrorMessage = "Job position must be at least 2 characters")]
    public string Puesto { get; set; } = string.Empty;

    /// <summary>
    /// General job description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Job responsibilities and duties (optional)
    /// </summary>
    public string? Responsabilidades { get; set; }

    /// <summary>
    /// Required skills and qualifications (optional)
    /// </summary>
    public string? HabilidadesRequeridas { get; set; }

    /// <summary>
    /// Salary range (optional)
    /// </summary>
    public string? Salario { get; set; }

    /// <summary>
    /// Employee benefits (optional)
    /// </summary>
    public string? Beneficios { get; set; }

    /// <summary>
    /// Location (optional)
    /// </summary>
    public string? Ubicacion { get; set; }

    /// <summary>
    /// Application date (optional)
    /// </summary>
    public DateTime? FechaAplicacion { get; set; }

    /// <summary>
    /// Application status (defaults to 'aplicado')
    /// </summary>
    public JobApplicationStatus Estado { get; set; } = JobApplicationStatus.Aplicado;

    /// <summary>
    /// Contact name (optional)
    /// </summary>
    public string? ContactoNombre { get; set; }

    /// <summary>
    /// Contact email (optional, validated if provided)
    /// </summary>
    [EmailAddress(ErrorMessage = "Contact email must be a valid email address")]
    public string? ContactoEmail { get; set; }

    /// <summary>
    /// Contact phone (optional)
    /// </summary>
    public string? ContactoTelefono { get; set; }

    /// <summary>
    /// Additional notes (optional)
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Request model for updating an existing job opportunity
/// </summary>
public class UpdateJobOpportunityRequest
{
    /// <summary>
    /// Company name (optional in update)
    /// </summary>
    [MinLength(2, ErrorMessage = "Company name must be at least 2 characters")]
    public string? Empresa { get; set; }

    /// <summary>
    /// Company website URL (optional)
    /// </summary>
    [Url(ErrorMessage = "Company URL must be a valid URL")]
    public string? URLCompany { get; set; }

    /// <summary>
    /// Job position (optional in update)
    /// </summary>
    [MinLength(2, ErrorMessage = "Job position must be at least 2 characters")]
    public string? Puesto { get; set; }

    /// <summary>
    /// General job description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Job responsibilities and duties (optional)
    /// </summary>
    public string? Responsabilidades { get; set; }

    /// <summary>
    /// Required skills and qualifications (optional)
    /// </summary>
    public string? HabilidadesRequeridas { get; set; }

    /// <summary>
    /// Salary range (optional)
    /// </summary>
    public string? Salario { get; set; }

    /// <summary>
    /// Employee benefits (optional)
    /// </summary>
    public string? Beneficios { get; set; }

    /// <summary>
    /// Location (optional)
    /// </summary>
    public string? Ubicacion { get; set; }

    /// <summary>
    /// Application date (optional)
    /// </summary>
    public DateTime? FechaAplicacion { get; set; }

    /// <summary>
    /// Application status (optional)
    /// </summary>
    public JobApplicationStatus? Estado { get; set; }

    /// <summary>
    /// Contact name (optional)
    /// </summary>
    public string? ContactoNombre { get; set; }

    /// <summary>
    /// Contact email (optional, validated if provided)
    /// </summary>
    [EmailAddress(ErrorMessage = "Contact email must be a valid email address")]
    public string? ContactoEmail { get; set; }

    /// <summary>
    /// Contact phone (optional)
    /// </summary>
    public string? ContactoTelefono { get; set; }

    /// <summary>
    /// Additional notes (optional)
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Response model for job opportunity operations
/// </summary>
public class JobOpportunityResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Job opportunity data (if applicable)
    /// </summary>
    public JobOpportunityData? Data { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response model for getting multiple job opportunities
/// </summary>
public class GetJobOpportunitiesResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// User ID used in the request
    /// </summary>
    public string UsuarioId { get; set; } = string.Empty;

    /// <summary>
    /// Total number of opportunities found
    /// </summary>
    public int TotalOpportunities { get; set; }

    /// <summary>
    /// Job opportunities data
    /// </summary>
    public List<JobOpportunityData> Opportunities { get; set; } = new();

    /// <summary>
    /// Statistics by status
    /// </summary>
    public JobOpportunityStats Stats { get; set; } = new();

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Job opportunity statistics
/// </summary>
public class JobOpportunityStats
{
    /// <summary>
    /// Number of applications submitted
    /// </summary>
    public int Aplicado { get; set; }

    /// <summary>
    /// Number in interview process
    /// </summary>
    public int Entrevista { get; set; }

    /// <summary>
    /// Number waiting for response
    /// </summary>
    public int Esperando { get; set; }

    /// <summary>
    /// Number of rejections
    /// </summary>
    public int Rechazado { get; set; }

    /// <summary>
    /// Number of accepted applications
    /// </summary>
    public int Aceptado { get; set; }

    /// <summary>
    /// Total count
    /// </summary>
    public int Total { get; set; }
}

/// <summary>
/// Query parameters for filtering job opportunities
/// </summary>
public class JobOpportunityQuery
{
    /// <summary>
    /// Filter by application status
    /// </summary>
    public JobApplicationStatus? Estado { get; set; }

    /// <summary>
    /// Filter by company name (partial match)
    /// </summary>
    public string? Empresa { get; set; }

    /// <summary>
    /// Filter by job position (partial match)
    /// </summary>
    public string? Puesto { get; set; }

    /// <summary>
    /// Filter by location (partial match)
    /// </summary>
    public string? Ubicacion { get; set; }

    /// <summary>
    /// Filter by application date from
    /// </summary>
    public DateTime? FechaDesde { get; set; }

    /// <summary>
    /// Filter by application date to
    /// </summary>
    public DateTime? FechaHasta { get; set; }

    /// <summary>
    /// Page number for pagination (1-based)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size for pagination (default 20, max 100)
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Sort field (empresa, puesto, fechaAplicacion, fechaCreacion)
    /// </summary>
    public string? SortBy { get; set; } = "fechaCreacion";

    /// <summary>
    /// Sort direction (asc, desc)
    /// </summary>
    public string? SortDirection { get; set; } = "desc";
}