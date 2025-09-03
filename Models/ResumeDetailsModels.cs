using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace TwinFx.Models;

/// <summary>
/// Clase limpia para estructurar datos de resume sin JsonElement ni ChildTokens
/// Optimizada para consumo directo por el UI
/// </summary>
public class ResumeDetails
{
    [JsonPropertyName("executive_summary")]
    public string ExecutiveSummary { get; set; } = string.Empty;

    [JsonPropertyName("personal_information")]
    public PersonalInformation PersonalInformation { get; set; } = new();

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("skills")]
    public List<string> Skills { get; set; } = new();

    [JsonPropertyName("education")]
    public List<EducationEntry> Education { get; set; } = new();

    [JsonPropertyName("work_experience")]
    public List<WorkExperienceEntry> WorkExperience { get; set; } = new();

    [JsonPropertyName("salaries")]
    public List<SalaryEntry> Salaries { get; set; } = new();

    [JsonPropertyName("benefits")]
    public List<BenefitEntry> Benefits { get; set; } = new();

    [JsonPropertyName("certifications")]
    public List<CertificationEntry> Certifications { get; set; } = new();

    [JsonPropertyName("projects")]
    public List<ProjectEntry> Projects { get; set; } = new();

    [JsonPropertyName("awards")]
    public List<AwardEntry> Awards { get; set; } = new();

    [JsonPropertyName("professional_associations")]
    public List<ProfessionalAssociationEntry> ProfessionalAssociations { get; set; } = new();
}

/// <summary>
/// Información personal del currículum
/// </summary>
public class PersonalInformation
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
/// Entrada de educación
/// </summary>
public class EducationEntry
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
/// Entrada de experiencia laboral
/// </summary>
public class WorkExperienceEntry
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
/// Entrada de salario
/// </summary>
public class SalaryEntry
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
/// Entrada de beneficios
/// </summary>
public class BenefitEntry
{
    [JsonPropertyName("job_title")]
    public string JobTitle { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("benefits")]
    public List<string> Benefits { get; set; } = new();
}

/// <summary>
/// Entrada de certificación
/// </summary>
public class CertificationEntry
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("issuing_organization")]
    public string IssuingOrganization { get; set; } = string.Empty;

    [JsonPropertyName("date_issued")]
    public string DateIssued { get; set; } = string.Empty;
}

/// <summary>
/// Entrada de proyecto
/// </summary>
public class ProjectEntry
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
/// Entrada de premio/reconocimiento
/// </summary>
public class AwardEntry
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int Year { get; set; }
}

/// <summary>
/// Entrada de asociación profesional
/// </summary>
public class ProfessionalAssociationEntry
{
    [JsonPropertyName("organization")]
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;
}

/// <summary>
/// Utilidades para convertir datos JSON complejos a clases limpias
/// </summary>
public static class ResumeDataConverter
{
    /// <summary>
    /// Convierte datos complejos de resumeData a ResumeDetails limpio usando Newtonsoft.Json
    /// </summary>
    /// <param name="resumeDataValue">Datos de resume de Cosmos DB (puede ser JsonElement, JObject, etc.)</param>
    /// <returns>ResumeDetails estructurado y limpio</returns>
    public static ResumeDetails? ConvertToResumeDetails(object? resumeDataValue)
    {
        if (resumeDataValue == null)
            return null;

        try
        {
            // Serializar el objeto complejo a JSON string usando Newtonsoft.Json
            var jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(resumeDataValue);
            
            // Deserializar a un objeto dinámico para navegación fácil
            dynamic? resumeObject = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonString);
            
            if (resumeObject == null)
                return null;

            // Navegar al objeto "resume" si existe
            dynamic actualResumeData = resumeObject.resume ?? resumeObject;
            
            if (actualResumeData == null)
                return null;

            // Crear el objeto ResumeDetails limpio
            var resumeDetails = new ResumeDetails();

            // Executive Summary
            resumeDetails.ExecutiveSummary = actualResumeData.executive_summary?.ToString() ?? string.Empty;

            // Summary
            resumeDetails.Summary = actualResumeData.summary?.ToString() ?? string.Empty;

            // Personal Information
            if (actualResumeData.personal_information != null)
            {
                resumeDetails.PersonalInformation = new PersonalInformation
                {
                    FullName = actualResumeData.personal_information.full_name?.ToString() ?? string.Empty,
                    Address = actualResumeData.personal_information.address?.ToString() ?? string.Empty,
                    PhoneNumber = actualResumeData.personal_information.phone_number?.ToString() ?? string.Empty,
                    Email = actualResumeData.personal_information.email?.ToString() ?? string.Empty,
                    LinkedIn = actualResumeData.personal_information.linkedin?.ToString() ?? string.Empty
                };
            }

            // Skills
            if (actualResumeData.skills != null)
            {
                resumeDetails.Skills = new List<string>();
                foreach (var skill in actualResumeData.skills)
                {
                    if (skill != null && !string.IsNullOrEmpty(skill.ToString()))
                    {
                        resumeDetails.Skills.Add(skill.ToString());
                    }
                }
            }

            // Education
            if (actualResumeData.education != null)
            {
                resumeDetails.Education = new List<EducationEntry>();
                foreach (var edu in actualResumeData.education)
                {
                    if (edu != null)
                    {
                        resumeDetails.Education.Add(new EducationEntry
                        {
                            Degree = edu.degree?.ToString() ?? string.Empty,
                            Institution = edu.institution?.ToString() ?? string.Empty,
                            GraduationYear = TryParseInt(edu.graduation_year?.ToString()),
                            Location = edu.location?.ToString() ?? string.Empty
                        });
                    }
                }
            }

            // Work Experience
            if (actualResumeData.work_experience != null)
            {
                resumeDetails.WorkExperience = new List<WorkExperienceEntry>();
                foreach (var work in actualResumeData.work_experience)
                {
                    if (work != null)
                    {
                        var responsibilities = new List<string>();
                        if (work.responsibilities != null)
                        {
                            foreach (var resp in work.responsibilities)
                            {
                                if (resp != null && !string.IsNullOrEmpty(resp.ToString()))
                                {
                                    responsibilities.Add(resp.ToString());
                                }
                            }
                        }

                        resumeDetails.WorkExperience.Add(new WorkExperienceEntry
                        {
                            JobTitle = work.job_title?.ToString() ?? string.Empty,
                            Company = work.company?.ToString() ?? string.Empty,
                            Duration = work.duration?.ToString() ?? string.Empty,
                            Responsibilities = responsibilities
                        });
                    }
                }
            }

            // Certifications
            if (actualResumeData.certifications != null)
            {
                resumeDetails.Certifications = new List<CertificationEntry>();
                foreach (var cert in actualResumeData.certifications)
                {
                    if (cert != null)
                    {
                        resumeDetails.Certifications.Add(new CertificationEntry
                        {
                            Title = cert.title?.ToString() ?? string.Empty,
                            IssuingOrganization = cert.issuing_organization?.ToString() ?? string.Empty,
                            DateIssued = cert.date_issued?.ToString() ?? string.Empty
                        });
                    }
                }
            }

            // Projects
            if (actualResumeData.projects != null)
            {
                resumeDetails.Projects = new List<ProjectEntry>();
                foreach (var proj in actualResumeData.projects)
                {
                    if (proj != null)
                    {
                        var technologies = new List<string>();
                        if (proj.technologies != null)
                        {
                            foreach (var tech in proj.technologies)
                            {
                                if (tech != null && !string.IsNullOrEmpty(tech.ToString()))
                                {
                                    technologies.Add(tech.ToString());
                                }
                            }
                        }

                        resumeDetails.Projects.Add(new ProjectEntry
                        {
                            ProjectName = proj.project_name?.ToString() ?? string.Empty,
                            Description = proj.description?.ToString() ?? string.Empty,
                            Technologies = technologies,
                            Duration = proj.duration?.ToString() ?? string.Empty
                        });
                    }
                }
            }

            // Awards
            if (actualResumeData.awards != null)
            {
                resumeDetails.Awards = new List<AwardEntry>();
                foreach (var award in actualResumeData.awards)
                {
                    if (award != null)
                    {
                        resumeDetails.Awards.Add(new AwardEntry
                        {
                            Title = award.title?.ToString() ?? string.Empty,
                            Organization = award.organization?.ToString() ?? string.Empty,
                            Year = TryParseInt(award.year?.ToString())
                        });
                    }
                }
            }

            // Salaries
            if (actualResumeData.salaries != null)
            {
                resumeDetails.Salaries = new List<SalaryEntry>();
                foreach (var salary in actualResumeData.salaries)
                {
                    if (salary != null)
                    {
                        resumeDetails.Salaries.Add(new SalaryEntry
                        {
                            JobTitle = salary.job_title?.ToString() ?? string.Empty,
                            Company = salary.company?.ToString() ?? string.Empty,
                            BaseSalary = TryParseDecimal(salary.base_salary?.ToString()),
                            Currency = salary.currency?.ToString() ?? "USD",
                            Year = TryParseInt(salary.year?.ToString())
                        });
                    }
                }
            }

            // Benefits
            if (actualResumeData.benefits != null)
            {
                resumeDetails.Benefits = new List<BenefitEntry>();
                foreach (var benefit in actualResumeData.benefits)
                {
                    if (benefit != null)
                    {
                        var benefits = new List<string>();
                        if (benefit.benefits != null)
                        {
                            foreach (var ben in benefit.benefits)
                            {
                                if (ben != null && !string.IsNullOrEmpty(ben.ToString()))
                                {
                                    benefits.Add(ben.ToString());
                                }
                            }
                        }

                        resumeDetails.Benefits.Add(new BenefitEntry
                        {
                            JobTitle = benefit.job_title?.ToString() ?? string.Empty,
                            Company = benefit.company?.ToString() ?? string.Empty,
                            Benefits = benefits
                        });
                    }
                }
            }

            // Professional Associations
            if (actualResumeData.professional_associations != null)
            {
                resumeDetails.ProfessionalAssociations = new List<ProfessionalAssociationEntry>();
                foreach (var assoc in actualResumeData.professional_associations)
                {
                    if (assoc != null)
                    {
                        resumeDetails.ProfessionalAssociations.Add(new ProfessionalAssociationEntry
                        {
                            Organization = assoc.organization?.ToString() ?? string.Empty,
                            Role = assoc.role?.ToString() ?? string.Empty,
                            Duration = assoc.duration?.ToString() ?? string.Empty
                        });
                    }
                }
            }

            return resumeDetails;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Helper para convertir string a int de manera segura
    /// </summary>
    private static int TryParseInt(string? value)
    {
        if (int.TryParse(value, out var result))
            return result;
        return 0;
    }

    /// <summary>
    /// Helper para convertir string a decimal de manera segura
    /// </summary>
    private static decimal TryParseDecimal(string? value)
    {
        if (decimal.TryParse(value, out var result))
            return result;
        return 0m;
    }
}