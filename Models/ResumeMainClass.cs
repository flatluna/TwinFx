using Newtonsoft.Json;
using System.Collections.Generic;

namespace TwinFx.Models
{
    public class ResumeMainClass
    {
        [JsonProperty("resume")]
        public ResumeInner Resume { get; set; } = new ResumeInner();
    }

    public class ResumeInner
    {
        [JsonProperty("executive_summary")]
        public string ExecutiveSummary { get; set; } = string.Empty;

        [JsonProperty("personal_information")]
        public PersonalInfo PersonalInformation { get; set; } = new PersonalInfo();

        [JsonProperty("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonProperty("skills")]
        public List<string> Skills { get; set; } = new List<string>();

        [JsonProperty("education")]
        public List<EducationItem> Education { get; set; } = new List<EducationItem>();

        [JsonProperty("work_experience")]
        public List<WorkItem> WorkExperience { get; set; } = new List<WorkItem>();

        [JsonProperty("salaries")]
        public List<object> Salaries { get; set; } = new List<object>();

        [JsonProperty("benefits")]
        public List<object> Benefits { get; set; } = new List<object>();

        [JsonProperty("certifications")]
        public List<CertificationItem> Certifications { get; set; } = new List<CertificationItem>();

        [JsonProperty("projects")]
        public List<object> Projects { get; set; } = new List<object>();

        [JsonProperty("awards")]
        public List<AwardItem> Awards { get; set; } = new List<AwardItem>();

        [JsonProperty("professional_associations")]
        public List<object> ProfessionalAssociations { get; set; } = new List<object>();
    }

    public class PersonalInfo
    {
        [JsonProperty("full_name")]
        public string FullName { get; set; } = string.Empty;
        [JsonProperty("address")]
        public string Address { get; set; } = string.Empty;
        [JsonProperty("phone_number")]
        public string PhoneNumber { get; set; } = string.Empty;
        [JsonProperty("email")]
        public string Email { get; set; } = string.Empty;
        [JsonProperty("linkedin")]
        public string LinkedIn { get; set; } = string.Empty;
    }

    public class EducationItem
    {
        [JsonProperty("degree")]
        public string Degree { get; set; } = string.Empty;
        [JsonProperty("institution")]
        public string Institution { get; set; } = string.Empty;
        [JsonProperty("graduation_year")]
        public int GraduationYear { get; set; }
        [JsonProperty("location")]
        public string Location { get; set; } = string.Empty;
    }

    public class WorkItem
    {
        [JsonProperty("job_title")]
        public string JobTitle { get; set; } = string.Empty;
        [JsonProperty("company")]
        public string Company { get; set; } = string.Empty;
        [JsonProperty("duration")]
        public string Duration { get; set; } = string.Empty;
        [JsonProperty("responsibilities")]
        public List<string> Responsibilities { get; set; } = new List<string>();
    }

    public class CertificationItem
    {
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;
        [JsonProperty("issuing_organization")]
        public string IssuingOrganization { get; set; } = string.Empty;
        [JsonProperty("date_issued")]
        public string DateIssued { get; set; } = string.Empty;
    }

    public class AwardItem
    {
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;
        [JsonProperty("organization")]
        public string Organization { get; set; } = string.Empty;
        [JsonProperty("year")]
        public int Year { get; set; }
    }
}
