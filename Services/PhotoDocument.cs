using System.Text.Json;

namespace TwinFx.Services;

public class PhotoDocument
{
    public string Id { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public string PhotoId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DateTaken { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string PeopleInPhoto { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }

    public string Country { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ProcessedAt { get; set; }

    public static PhotoDocument FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (data.TryGetValue(key, out var value) && value != null)
            {
                try
                {
                    if (value is T directValue)
                        return directValue;

                    if (value is JsonElement jsonElement)
                    {
                        var type = typeof(T);
                        if (type == typeof(string))
                            return (T)(object)(jsonElement.GetString() ?? string.Empty);
                        if (type == typeof(long))
                            return (T)(object)jsonElement.GetInt64();
                        if (type == typeof(DateTime))
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
            return defaultValue;
        }

        List<string> GetStringList(string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return new List<string>();

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                return element.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();

            if (value is IEnumerable<object> enumerable)
                return enumerable.Select(item => item?.ToString() ?? string.Empty).ToList();

            return new List<string>();
        }

        return new PhotoDocument
        {
            Id = GetValue("id", GetValue("photoId", "")),
            TwinId = GetValue("TwinID", GetValue("twinId", "")),
            PhotoId = GetValue<string>("photoId"),
            Description = GetValue<string>("description"),
            DateTaken = GetValue<string>("dateTaken"),
            Location = GetValue<string>("location"),
            PeopleInPhoto = GetValue<string>("peopleInPhoto"),
            Category = GetValue<string>("category"),
            Tags = GetStringList("tags"),
            FilePath = GetValue<string>("filePath"),
            FileName = GetValue<string>("fileName"),
            FileSize = GetValue<long>("fileSize"),
            MimeType = GetValue<string>("mimeType"),
            UploadDate = GetValue("uploadDate", DateTime.MinValue),
            CreatedAt = GetValue("createdAt", DateTime.MinValue),
            ProcessedAt = GetValue("processedAt", DateTime.MinValue)
        };
    }
}
