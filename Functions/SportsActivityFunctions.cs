using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TwinFx.Services;
using System.Text;
using TwinAgentsLibrary.Models;

namespace TwinFx.Functions
{
    /// <summary>
    /// Azure Functions for Sports Activities CRUD operations
    /// Container: TwinSports, PartitionKey: TwinID
    /// </summary>
    public class SportsActivityFunctions
    {
        private readonly ILogger<SportsActivityFunctions> _logger;
        private readonly SportsActivityService _sportsService;

        public SportsActivityFunctions(ILogger<SportsActivityFunctions> logger, SportsActivityService sportsService)
        {
            _logger = logger;
            _sportsService = sportsService;
        }

        /// <summary>
        /// Create a new sports activity
        /// POST /api/sports-activities
        /// </summary>
        [Function("CreateSportsActivity")]
        public async Task<IActionResult> CreateSportsActivity(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sports-activities")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("????? Creating new sports activity");

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var activityData = JsonConvert.DeserializeObject<SportsActivityData>(requestBody);

                if (activityData == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid activity data provided" });
                }

                // Validate required fields
                if (string.IsNullOrEmpty(activityData.TwinID))
                {
                    return new BadRequestObjectResult(new { error = "TwinID is required" });
                }

                if (string.IsNullOrEmpty(activityData.TipoActividad))
                {
                    return new BadRequestObjectResult(new { error = "TipoActividad is required" });
                }

                if (string.IsNullOrEmpty(activityData.Fecha))
                {
                    return new BadRequestObjectResult(new { error = "Fecha is required" });
                }

                // Set system fields (ID will be generated in the service if needed)
                activityData.FechaCreacion = DateTime.UtcNow.ToString("O");
                activityData.FechaActualizacion = DateTime.UtcNow.ToString("O");

                var success = await _sportsService.CreateSportsActivityAsync(activityData);

                if (success)
                {
                    return new OkObjectResult(new { 
                        message = "Sports activity created successfully", 
                        id = activityData.id,
                        activityData = activityData
                    });
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error creating sports activity");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Get all sports activities for a twin
        /// GET /api/sports-activities/{twinId}
        /// </summary>
        [Function("GetSportsActivitiesByTwinId")]
        public async Task<IActionResult> GetSportsActivitiesByTwinId(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "sports-activities/{twinId}")] HttpRequest req,
            string twinId)
        {
            try
            {
                _logger.LogInformation("????? Getting sports activities for Twin: {TwinId}", twinId);

                var activities = await _sportsService.GetSportsActivitiesByTwinIdAsync(twinId);

                return new OkObjectResult(new { 
                    twinId = twinId,
                    count = activities.Count,
                    activities = activities
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting sports activities for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Get a specific sports activity by ID
        /// GET /api/sports-activities/{twinId}/{activityId}
        /// </summary>
        [Function("GetSportsActivityById")]
        public async Task<IActionResult> GetSportsActivityById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "sports-activities/{twinId}/{activityId}")] HttpRequest req,
            string twinId,
            string activityId)
        {
            try
            {
                _logger.LogInformation("????? Getting sports activity: {ActivityId} for Twin: {TwinId}", activityId, twinId);

                var activity = await _sportsService.GetSportsActivityByIdAsync(activityId, twinId);

                if (activity != null)
                {
                    return new OkObjectResult(activity);
                }
                else
                {
                    return new NotFoundObjectResult(new { error = "Sports activity not found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting sports activity: {ActivityId} for Twin: {TwinId}", activityId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Update an existing sports activity
        /// PUT /api/sports-activities/{twinId}/{activityId}
        /// </summary>
        [Function("UpdateSportsActivity")]
        public async Task<IActionResult> UpdateSportsActivity(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "sports-activities/{twinId}/{activityId}")] HttpRequest req,
            string twinId,
            string activityId)
        {
            try
            {
                _logger.LogInformation("????? Updating sports activity: {ActivityId} for Twin: {TwinId}", activityId, twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var activityData = JsonConvert.DeserializeObject<   SportsActivityData>(requestBody);

                if (activityData == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid activity data provided" });
                }

                // Ensure IDs match route parameters
                activityData.id = activityId;
                activityData.TwinID = twinId;

                var success = await _sportsService.UpdateSportsActivityAsync(activityData);

                if (success)
                {
                    return new OkObjectResult(new { 
                        message = "Sports activity updated successfully",
                        activityData = activityData
                    });
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error updating sports activity: {ActivityId} for Twin: {TwinId}", activityId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Delete a sports activity
        /// DELETE /api/sports-activities/{twinId}/{activityId}
        /// </summary>
        [Function("DeleteSportsActivity")]
        public async Task<IActionResult> DeleteSportsActivity(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "sports-activities/{twinId}/{activityId}")] HttpRequest req,
            string twinId,
            string activityId)
        {
            try
            {
                _logger.LogInformation("????? Deleting sports activity: {ActivityId} for Twin: {TwinId}", activityId, twinId);

                var success = await _sportsService.DeleteSportsActivityAsync(activityId, twinId);

                if (success)
                {
                    return new OkObjectResult(new { message = "Sports activity deleted successfully" });
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error deleting sports activity: {ActivityId} for Twin: {TwinId}", activityId, twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Get sports activities within a date range
        /// GET /api/sports-activities/{twinId}/range?startDate=2024-01-01&endDate=2024-12-31
        /// </summary>
        [Function("GetSportsActivitiesByDateRange")]
        public async Task<IActionResult> GetSportsActivitiesByDateRange(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "sports-activities/{twinId}/range")] HttpRequest req,
            string twinId)
        {
            try
            {
                _logger.LogInformation("????? Getting sports activities by date range for Twin: {TwinId}", twinId);

                var startDateStr = req.Query["startDate"].FirstOrDefault();
                var endDateStr = req.Query["endDate"].FirstOrDefault();

                if (string.IsNullOrEmpty(startDateStr) || string.IsNullOrEmpty(endDateStr))
                {
                    return new BadRequestObjectResult(new { error = "startDate and endDate query parameters are required" });
                }

                if (!DateTime.TryParse(startDateStr, out var startDate) || !DateTime.TryParse(endDateStr, out var endDate))
                {
                    return new BadRequestObjectResult(new { error = "Invalid date format. Use YYYY-MM-DD format" });
                }

                var activities = await _sportsService.GetSportsActivitiesByDateRangeAsync(twinId, startDate, endDate);

                return new OkObjectResult(new { 
                    twinId = twinId,
                    startDate = startDate.ToString("yyyy-MM-dd"),
                    endDate = endDate.ToString("yyyy-MM-dd"),
                    count = activities.Count,
                    activities = activities
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting sports activities by date range for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Get comprehensive statistics for sports activities
        /// GET /api/sports-activities/{twinId}/stats?startDate=2024-01-01&endDate=2024-12-31
        /// </summary>
        [Function("GetSportsActivityStats")]
        public async Task<IActionResult> GetSportsActivityStats(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "sports-activities/{twinId}/stats")] HttpRequest req,
            string twinId)
        {
            try
            {
                _logger.LogInformation("?? Getting sports activity stats for Twin: {TwinId}", twinId);

                var startDateStr = req.Query["startDate"].FirstOrDefault();
                var endDateStr = req.Query["endDate"].FirstOrDefault();

                DateTime? startDate = null;
                DateTime? endDate = null;

                if (!string.IsNullOrEmpty(startDateStr) && DateTime.TryParse(startDateStr, out var parsedStartDate))
                {
                    startDate = parsedStartDate;
                }

                if (!string.IsNullOrEmpty(endDateStr) && DateTime.TryParse(endDateStr, out var parsedEndDate))
                {
                    endDate = parsedEndDate;
                }

                var stats = await _sportsService.GetSportsActivityStatsAsync(twinId, startDate, endDate);

                return new OkObjectResult(new { 
                    twinId = twinId,
                    startDate = startDate?.ToString("yyyy-MM-dd"),
                    endDate = endDate?.ToString("yyyy-MM-dd"),
                    stats = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting sports activity stats for Twin: {TwinId}", twinId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}