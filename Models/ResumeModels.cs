using System.Text.Json.Serialization;

namespace TwinFx.Models;

/// <summary>
/// Modelos tipados para datos de resume estructurados para el UI
/// Estos modelos garantizan que el JSON se serialice correctamente
/// </summary>

// ==================== MODELOS PRINCIPALES ====================

/// <summary>
/// Datos completos de resume estructurados para UI
/// </summary>
public class StructuredResumeData
{
    [JsonPropertyName("resume")]
    public StructuredResume Resume { get; set; } = new();
}

/// <summary>
/// Resume estructurado con todos los campos tipados
/// </summary>
public class StructuredResume
{
    [JsonPropertyName("executive_summary")]
    public string ExecutiveSummary { get; set; } = string.Empty;

    [JsonPropertyName("personal_information")]
    public StructuredPersonalInformation PersonalInformation { get; set; } = new();

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("skills")]
    public List<string> Skills { get; set; } = new();

    [JsonPropertyName("education")]
    public List<StructuredEducation> Education { get; set; } = new();

    [JsonPropertyName("work_experience")]
    public List<StructuredWorkExperience> WorkExperience { get; set; } = new();

    [JsonPropertyName("salaries")]
    public List<StructuredSalary> Salaries { get; set; } = new();

    [JsonPropertyName("benefits")]
    public List<StructuredBenefit> Benefits { get; set; } = new();

    [JsonPropertyName("certifications")]
    public List<StructuredCertification> Certifications { get; set; } = new();

    [JsonPropertyName("projects")]
    public List<StructuredProject> Projects { get; set; } = new();

    [JsonPropertyName("awards")]
    public List<StructuredAward> Awards { get; set; } = new();

    [JsonPropertyName("professional_associations")]
    public List<StructuredProfessionalAssociation> ProfessionalAssociations { get; set; } = new();
}

// ==================== CLASES AUXILIARES ====================

/// <summary>
/// Información personal estructurada
/// </summary>
public class StructuredPersonalInformation
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("linkedin")]
    public string LinkedIn { get; set; } = string.Empty;
}

/// <summary>
/// Educación estructurada
/// </summary>
public class StructuredEducation
{
    [JsonPropertyName("degree")]
    public string Degree { get; set; } = string.Empty;

    [JsonPropertyName("institution")]
    public string Institution { get; set; } = string.Empty;

    [JsonPropertyName("graduation_year")]
    public int GraduationYear { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
}

/// <summary>
/// Experiencia laboral estructurada
/// </summary>
public class StructuredWorkExperience
{
    [JsonPropertyName("job_title")]
    public string JobTitle { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;

    [JsonPropertyName("responsibilities")]
    public List<string> Responsibilities { get; set; } = new();
}

/// <summary>
/// Información salarial estructurada
/// </summary>
public class StructuredSalary
{
    [JsonPropertyName("job_title")]
    public string JobTitle { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("base_salary")]
    public decimal BaseSalary { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("year")]
    public int Year { get; set; }
}

/// <summary>
/// Beneficios estructurados
/// </summary>
public class StructuredBenefit
{
    [JsonPropertyName("job_title")]
    public string JobTitle { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("benefits")]
    public List<string> Benefits { get; set; } = new();
}

/// <summary>
/// Certificación estructurada
/// </summary>
public class StructuredCertification
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("issuing_organization")]
    public string IssuingOrganization { get; set; } = string.Empty;

    [JsonPropertyName("date_issued")]
    public string DateIssued { get; set; } = string.Empty;
}

/// <summary>
/// Proyecto estructurado
/// </summary>
public class StructuredProject
{
    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("technologies")]
    public List<string> Technologies { get; set; } = new();

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;
}

/// <summary>
/// Premio/reconocimiento estructurado
/// </summary>
public class StructuredAward
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int Year { get; set; }
}

/// <summary>
/// Asociación profesional estructurada
/// </summary>
public class StructuredProfessionalAssociation
{
    [JsonPropertyName("organization")]
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;
}