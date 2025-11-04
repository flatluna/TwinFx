using System.Text.Json.Serialization;

namespace TwinFx.Models;

/// <summary>
/// Education data model
/// </summary>
public class EducationData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("twinId")]
    public string TwinID { get; set; } = string.Empty;

    [JsonPropertyName("countryId")]
    public string CountryID { get; set; } = string.Empty;

    [JsonPropertyName("education_type")]
    public string EducationType { get; set; } = string.Empty;

    [JsonPropertyName("degree")]
    public string Degree { get; set; } = string.Empty;

    [JsonPropertyName("field_of_study")]
    public string FieldOfStudy { get; set; } = string.Empty;

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("end_date")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("in_progress")]
    public bool InProgress { get; set; }

    [JsonPropertyName("institution")]
    public string Institution { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("achievements")]
    public string Achievements { get; set; } = string.Empty;

    [JsonPropertyName("gpa")]
    public string Gpa { get; set; } = string.Empty;

    [JsonPropertyName("credits")]
    public string Credits { get; set; } = string.Empty;
}

/// <summary>
/// Family data model
/// </summary> 

/// <summary>
/// Contact data model
/// </summary>
public class ContactData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("twinId")]
    public string TwinID { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("apellido")]
    public string Apellido { get; set; } = string.Empty;

    [JsonPropertyName("relacion")]
    public string Relacion { get; set; } = string.Empty;

    [JsonPropertyName("apodo")]
    public string Apodo { get; set; } = string.Empty;

    [JsonPropertyName("telefonoMovil")]
    public string TelefonoMovil { get; set; } = string.Empty;

    [JsonPropertyName("telefonoTrabajo")]
    public string TelefonoTrabajo { get; set; } = string.Empty;

    [JsonPropertyName("telefonoCasa")]
    public string TelefonoCasa { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("direccion")]
    public string Direccion { get; set; } = string.Empty;

    [JsonPropertyName("empresa")]
    public string Empresa { get; set; } = string.Empty;

    [JsonPropertyName("cargo")]
    public string Cargo { get; set; } = string.Empty;

    [JsonPropertyName("cumpleanos")]
    public string Cumpleanos { get; set; } = string.Empty;

    [JsonPropertyName("notas")]
    public string Notas { get; set; } = string.Empty;

    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "contact";
}