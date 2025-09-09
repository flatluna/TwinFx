using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinFx.Models
{
    /// <summary>
    /// Data class for sports/exercise activity information.
    /// Matches the ActividadEjercicio interface from UI exactly.
    /// </summary>
    public class SportsActivityData
    {
        public string Id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string Fecha { get; set; } = string.Empty; // formato: YYYY-MM-DD
        public string TipoActividad { get; set; } = string.Empty;
        
        // Duración y métricas
        public int DuracionMinutos { get; set; }
        public IntensidadEjercicio Intensidad { get; set; } = IntensidadEjercicio.Moderada;
        public int? Calorias { get; set; }
        public int? Pasos { get; set; }
        public double? DistanciaKm { get; set; }
        
        // Frecuencia cardíaca
        public int? FrecuenciaCardiacaPromedio { get; set; }
        public int? FrecuenciaCardiacaMaxima { get; set; }
        
        // Ubicación y notas
        public string? Ubicacion { get; set; }
        public string? Notas { get; set; }
        
        // Ejercicios detallados
        public List<EjercicioDetalle> EjerciciosDetalle { get; set; } = new();
        
        // Campos de sistema
        public string FechaCreacion { get; set; } = DateTime.UtcNow.ToString("O");
        public string FechaActualizacion { get; set; } = DateTime.UtcNow.ToString("O");

        public Dictionary<string, object?> ToDict()
        {
            return new Dictionary<string, object?>
            {
                ["id"] = Id,
                ["TwinID"] = TwinID,
                ["fecha"] = Fecha,
                ["tipoActividad"] = TipoActividad,
                ["duracionMinutos"] = DuracionMinutos,
                ["intensidad"] = Intensidad.ToString().ToLowerInvariant(),
                ["calorias"] = Calorias,
                ["pasos"] = Pasos,
                ["distanciaKm"] = DistanciaKm,
                ["frecuenciaCardiacaPromedio"] = FrecuenciaCardiacaPromedio,
                ["frecuenciaCardiacaMaxima"] = FrecuenciaCardiacaMaxima,
                ["ubicacion"] = Ubicacion,
                ["notas"] = Notas,
                ["ejerciciosDetalle"] = EjerciciosDetalle.Select(e => e.ToDict()).ToList(),
                ["fechaCreacion"] = FechaCreacion,
                ["fechaActualizacion"] = FechaActualizacion
            };
        }

        public static SportsActivityData FromDict(Dictionary<string, object?> data)
        {
            T GetValue<T>(string key, T defaultValue = default!)
            {
                if (!data.TryGetValue(key, out var value) || value == null)
                    return defaultValue;

                try
                {
                    if (value is T directValue)
                        return directValue;

                    if (value is JsonElement jsonElement)
                    {
                        var type = typeof(T);
                        if (type == typeof(string))
                            return (T)(object)(jsonElement.GetString() ?? string.Empty);
                        if (type == typeof(int))
                            return (T)(object)jsonElement.GetInt32();
                        if (type == typeof(double))
                            return (T)(object)jsonElement.GetDouble();
                        if (type == typeof(int?))
                        {
                            if (jsonElement.ValueKind == JsonValueKind.Null)
                                return (T)(object?)null;
                            return (T)(object?)jsonElement.GetInt32();
                        }
                        if (type == typeof(double?))
                        {
                            if (jsonElement.ValueKind == JsonValueKind.Null)
                                return (T)(object?)null;
                            return (T)(object?)jsonElement.GetDouble();
                        }
                    }

                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }

            List<EjercicioDetalle> GetEjerciciosDetalle(string key)
            {
                if (!data.TryGetValue(key, out var value) || value == null)
                    return new List<EjercicioDetalle>();

                try
                {
                    if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
                    {
                        return jsonElement.EnumerateArray()
                            .Select(item => EjercicioDetalle.FromDict(ConvertObjectToDictionary(item)))
                            .ToList();
                    }
                    else if (value is IEnumerable<object> enumerable)
                    {
                        return enumerable
                            .Select(item => EjercicioDetalle.FromDict(ConvertObjectToDictionary(item)))
                            .ToList();
                    }
                }
                catch (Exception)
                {
                    // Return empty list on error
                }

                return new List<EjercicioDetalle>();
            }

            static Dictionary<string, object?> ConvertObjectToDictionary(object? obj)
            {
                if (obj == null)
                    return new Dictionary<string, object?>();

                if (obj is Dictionary<string, object?> dict)
                    return dict;

                if (obj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                {
                    var dictionary = new Dictionary<string, object?>();
                    foreach (var property in jsonElement.EnumerateObject())
                    {
                        dictionary[property.Name] = JsonElementToObject(property.Value);
                    }
                    return dictionary;
                }

                // Try to convert using reflection as fallback
                try
                {
                    var serialized = System.Text.Json.JsonSerializer.Serialize(obj);
                    var deserialized = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(serialized);
                    return deserialized ?? new Dictionary<string, object?>();
                }
                catch
                {
                    return new Dictionary<string, object?>();
                }
            }

            static object? JsonElementToObject(JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    JsonValueKind.Object => ConvertObjectToDictionary(element),
                    JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
                    _ => element.ToString()
                };
            }

            return new SportsActivityData
            {
                Id = GetValue("id", ""),
                TwinID = GetValue<string>("TwinID"),
                Fecha = GetValue<string>("fecha"),
                TipoActividad = GetValue<string>("tipoActividad"),
                DuracionMinutos = GetValue("duracionMinutos", 0),
                Intensidad = GetValue<string>("intensidad") switch
                {
                    "baja" => IntensidadEjercicio.Baja,
                    "moderada" => IntensidadEjercicio.Moderada,
                    "alta" => IntensidadEjercicio.Alta,
                    "muy_alta" => IntensidadEjercicio.MuyAlta,
                    _ => IntensidadEjercicio.Moderada
                },
                Calorias = GetValue<int?>("calorias"),
                Pasos = GetValue<int?>("pasos"),
                DistanciaKm = GetValue<double?>("distanciaKm"),
                FrecuenciaCardiacaPromedio = GetValue<int?>("frecuenciaCardiacaPromedio"),
                FrecuenciaCardiacaMaxima = GetValue<int?>("frecuenciaCardiacaMaxima"),
                Ubicacion = GetValue<string?>("ubicacion"),
                Notas = GetValue<string?>("notas"),
                EjerciciosDetalle = GetEjerciciosDetalle("ejerciciosDetalle"),
                FechaCreacion = GetValue("fechaCreacion", DateTime.UtcNow.ToString("O")),
                FechaActualizacion = GetValue("fechaActualizacion", DateTime.UtcNow.ToString("O"))
            };
        }
    }

    /// <summary>
    /// Enum for exercise intensity levels.
    /// </summary>
    public enum IntensidadEjercicio
    {
        Baja,
        Moderada,
        Alta,
        MuyAlta
    }

    /// <summary>
    /// Detailed exercise information within an activity.
    /// </summary>
    public class EjercicioDetalle
    {
        public string Nombre { get; set; } = string.Empty;
        public int? Series { get; set; }
        public int? Repeticiones { get; set; }
        public double? Peso { get; set; } // en kg
        public int? DuracionSegundos { get; set; }
        public string? Notas { get; set; }

        public Dictionary<string, object?> ToDict()
        {
            return new Dictionary<string, object?>
            {
                ["nombre"] = Nombre,
                ["series"] = Series,
                ["repeticiones"] = Repeticiones,
                ["peso"] = Peso,
                ["duracionSegundos"] = DuracionSegundos,
                ["notas"] = Notas
            };
        }

        public static EjercicioDetalle FromDict(Dictionary<string, object?> data)
        {
            T GetValue<T>(string key, T defaultValue = default!)
            {
                if (!data.TryGetValue(key, out var value) || value == null)
                    return defaultValue;

                try
                {
                    if (value is T directValue)
                        return directValue;

                    if (value is JsonElement jsonElement)
                    {
                        var type = typeof(T);
                        if (type == typeof(string))
                            return (T)(object)(jsonElement.GetString() ?? string.Empty);
                        if (type == typeof(int?))
                        {
                            if (jsonElement.ValueKind == JsonValueKind.Null)
                                return (T)(object?)null;
                            return (T)(object?)jsonElement.GetInt32();
                        }
                        if (type == typeof(double?))
                        {
                            if (jsonElement.ValueKind == JsonValueKind.Null)
                                return (T)(object?)null;
                            return (T)(object?)jsonElement.GetDouble();
                        }
                    }

                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }

            return new EjercicioDetalle
            {
                Nombre = GetValue<string>("nombre"),
                Series = GetValue<int?>("series"),
                Repeticiones = GetValue<int?>("repeticiones"),
                Peso = GetValue<double?>("peso"),
                DuracionSegundos = GetValue<int?>("duracionSegundos"),
                Notas = GetValue<string?>("notas")
            };
        }
    }

    /// <summary>
    /// Statistics for sports activities.
    /// </summary>
    public class SportsActivityStats
    {
        public int TotalActividades { get; set; } = 0;
        public int TotalMinutos { get; set; } = 0;
        public int TotalCalorias { get; set; } = 0;
        public int TotalPasos { get; set; } = 0;
        public double TotalDistanciaKm { get; set; } = 0.0;
        public double PromedioMinutosPorActividad { get; set; } = 0.0;
        public double PromedioCaloriasPorActividad { get; set; } = 0.0;
        public DateTime? UltimaActividad { get; set; }
        public Dictionary<string, int> ActividadesPorTipo { get; set; } = new();
    }
}