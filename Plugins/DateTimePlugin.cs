using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace TwinFx.Plugins;

/// <summary>
/// DateTime plugin for providing current date and time information
/// </summary>
public sealed class DateTimePlugin
{
    [KernelFunction, Description("Retrieves the current date and time in UTC.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public string GetCurrentDateTimeInUtc()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    }

    [KernelFunction, Description("Retrieves the current date in UTC in YYYY-MM-DD format.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public string GetCurrentDateInUtc()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd");
    }

    [KernelFunction, Description("Retrieves the current time in UTC in HH:mm:ss format.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public string GetCurrentTimeInUtc()
    {
        return DateTime.UtcNow.ToString("HH:mm:ss");
    }

    [KernelFunction, Description("Converts a datetime string to a specific timezone.")]
    public string ConvertToTimezone(
        [Description("The datetime string in UTC")] string dateTimeUtc, 
        [Description("The target timezone ID (e.g., 'Pacific Standard Time')")] string timezoneId)
    {
        try
        {
            if (DateTime.TryParse(dateTimeUtc, out var utcDateTime))
            {
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                var convertedTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, timeZone);
                return convertedTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return "Invalid datetime format";
        }
        catch (Exception ex)
        {
            return $"Error converting timezone: {ex.Message}";
        }
    }

    [KernelFunction, Description("Gets the current timestamp as Unix epoch seconds.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public long GetCurrentUnixTimestamp()
    {
        return ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
    }

    [KernelFunction, Description("Formats a datetime string to a specific format.")]
    public string FormatDateTime(
        [Description("The datetime string to format")] string dateTime, 
        [Description("The format string (e.g., 'yyyy-MM-dd', 'HH:mm:ss')")] string format)
    {
        try
        {
            if (DateTime.TryParse(dateTime, out var parsedDateTime))
            {
                return parsedDateTime.ToString(format);
            }
            return "Invalid datetime format";
        }
        catch (Exception ex)
        {
            return $"Error formatting datetime: {ex.Message}";
        }
    }

    [KernelFunction, Description("Gets the current time in a specific timezone.")]
    public string GetCurrentTimeInTimezone(
        [Description("The timezone ID (e.g., 'Pacific Standard Time', 'Eastern Standard Time')")] string timezoneId)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            var currentTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
            return currentTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            return $"Error getting time for timezone '{timezoneId}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Gets a list of available timezone IDs.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public string GetAvailableTimezones()
    {
        var commonTimezones = new[]
        {
            "UTC",
            "Eastern Standard Time",
            "Central Standard Time", 
            "Mountain Standard Time",
            "Pacific Standard Time",
            "GMT Standard Time",
            "Central European Standard Time",
            "Tokyo Standard Time",
            "China Standard Time",
            "India Standard Time"
        };

        return string.Join(", ", commonTimezones);
    }

    [KernelFunction, Description("Calculates the difference between two dates.")]
    public string CalculateDateDifference(
        [Description("First date in YYYY-MM-DD or full datetime format")] string date1,
        [Description("Second date in YYYY-MM-DD or full datetime format")] string date2)
    {
        try
        {
            if (DateTime.TryParse(date1, out var firstDate) && DateTime.TryParse(date2, out var secondDate))
            {
                var difference = secondDate - firstDate;
                
                if (difference.TotalDays >= 1)
                {
                    return $"{Math.Abs(difference.TotalDays):F0} days";
                }
                else if (difference.TotalHours >= 1)
                {
                    return $"{Math.Abs(difference.TotalHours):F0} hours";
                }
                else
                {
                    return $"{Math.Abs(difference.TotalMinutes):F0} minutes";
                }
            }
            return "Invalid date format";
        }
        catch (Exception ex)
        {
            return $"Error calculating date difference: {ex.Message}";
        }
    }

    [KernelFunction, Description("Adds or subtracts time from a given date.")]
    public string AddTimeToDate(
        [Description("The base date in YYYY-MM-DD or full datetime format")] string baseDate,
        [Description("Amount to add (positive) or subtract (negative)")] int amount,
        [Description("Unit of time: 'days', 'hours', 'minutes', 'months', 'years'")] string unit)
    {
        try
        {
            if (DateTime.TryParse(baseDate, out var date))
            {
                var result = unit.ToLower() switch
                {
                    "days" => date.AddDays(amount),
                    "hours" => date.AddHours(amount),
                    "minutes" => date.AddMinutes(amount),
                    "months" => date.AddMonths(amount),
                    "years" => date.AddYears(amount),
                    _ => date
                };

                return result.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return "Invalid date format";
        }
        catch (Exception ex)
        {
            return $"Error adding time to date: {ex.Message}";
        }
    }
}