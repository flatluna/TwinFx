using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwinFx.Models
{
    /// <summary>
    /// Datos de alimento para el sistema nutricional
    /// Container: TwinAlimentos, PartitionKey: TwinID
    /// </summary>
    public class FoodData
    {
        public string Id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string NombreAlimento { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        
        // Información nutricional por 100g
        public double CaloriasPor100g { get; set; }
        public double? Proteinas { get; set; } // gramos
        public double? Carbohidratos { get; set; } // gramos
        public double? Grasas { get; set; } // gramos
        public double? Fibra { get; set; } // gramos
        
        // Calorías calculadas para la unidad común
        public double CaloriasUnidadComun => CantidadComun > 0 ? (CaloriasPor100g * CantidadComun / 100.0) : 0;
        
        // Unidad común de medida
        public string UnidadComun { get; set; } = "unidades";
        public double CantidadComun { get; set; } = 1;
        
        // Descripción opcional
        public string? Descripcion { get; set; }
        
        // Campos de sistema
        public string FechaCreacion { get; set; } = DateTime.UtcNow.ToString("O");
        public string FechaActualizacion { get; set; } = DateTime.UtcNow.ToString("O");
        public string Type { get; set; } = "food";

        public Dictionary<string, object?> ToDict()
        {
            return new Dictionary<string, object?>
            {
                ["id"] = Id,
                ["TwinID"] = TwinID,
                ["nombreAlimento"] = NombreAlimento,
                ["categoria"] = Categoria,
                ["caloriasPor100g"] = CaloriasPor100g,
                ["proteinas"] = Proteinas ?? 0.0, // ✅ NO enviamos null, enviamos 0
                ["carbohidratos"] = Carbohidratos ?? 0.0, // ✅ NO enviamos null, enviamos 0
                ["grasas"] = Grasas ?? 0.0, // ✅ NO enviamos null, enviamos 0
                ["fibra"] = Fibra ?? 0.0, // ✅ NO enviamos null, enviamos 0
                ["unidadComun"] = UnidadComun,
                ["cantidadComun"] = CantidadComun,
                ["descripcion"] = Descripcion,
                ["fechaCreacion"] = FechaCreacion,
                ["fechaActualizacion"] = FechaActualizacion,
                ["type"] = Type
            };
        }

        public static FoodData FromDict(Dictionary<string, object?> data)
        {
            // Helper para obtener valores con mejor logging
            T GetValue<T>(string key, T defaultValue = default!)
            {
                if (!data.TryGetValue(key, out var value) || value == null)
                {
                    Console.WriteLine($"🔍 FoodData.FromDict: Key '{key}' not found or null, using default: {defaultValue}");
                    return defaultValue;
                }

                try
                {
                    // Tipo directo
                    if (value is T directValue)
                    {
                        Console.WriteLine($"✅ FoodData.FromDict: Key '{key}' = {directValue} (direct type)");
                        return directValue;
                    }

                    // JsonElement (System.Text.Json)
                    if (value is JsonElement jsonElement)
                    {
                        var result = ParseJsonElement<T>(jsonElement, key, defaultValue);
                        Console.WriteLine($"✅ FoodData.FromDict: Key '{key}' = {result} (from JsonElement)");
                        return result;
                    }

                    // JToken (Newtonsoft.Json) - común en Cosmos DB responses
                    if (value is JToken jToken)
                    {
                        var result = ParseJToken<T>(jToken, key, defaultValue);
                        Console.WriteLine($"✅ FoodData.FromDict: Key '{key}' = {result} (from JToken)");
                        return result;
                    }

                    // Conversión directa
                    var converted = (T)Convert.ChangeType(value, typeof(T));
                    Console.WriteLine($"✅ FoodData.FromDict: Key '{key}' = {converted} (converted from {value.GetType().Name})");
                    return converted;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ FoodData.FromDict: Error parsing key '{key}' (value: {value}, type: {value?.GetType().Name}): {ex.Message}");
                    return defaultValue;
                }
            }

            // ✅ Para campos nutricionales nullable - FIXED VERSION
            double? GetNutritionalValue(string key)
            {
                // Primero intentamos obtener el valor como double?
                var nullableValue = GetValue<double?>(key, null);
                if (nullableValue.HasValue)
                {
                    Console.WriteLine($"🔍 GetNutritionalValue: '{key}' found with value: {nullableValue.Value}");
                    return nullableValue.Value;
                }

                // Si no funciona como nullable, intentemos como double directo
                var directValue = GetValue<double>(key, double.MinValue); // Usar MinValue como indicador de "no encontrado"
                if (directValue != double.MinValue)
                {
                    Console.WriteLine($"🔍 GetNutritionalValue: '{key}' found as direct double: {directValue}");
                    return directValue;
                }

                // Si tampoco funciona, intentemos parsing manual del valor raw
                if (data.TryGetValue(key, out var rawValue) && rawValue != null)
                {
                    Console.WriteLine($"🔍 GetNutritionalValue: '{key}' attempting manual parse of raw value: {rawValue} (type: {rawValue.GetType().Name})");
                    
                    // Intentar conversión directa si es un número
                    if (rawValue is int intValue)
                    {
                        Console.WriteLine($"✅ GetNutritionalValue: '{key}' converted from int: {intValue}");
                        return (double)intValue;
                    }
                    
                    if (rawValue is long longValue)
                    {
                        Console.WriteLine($"✅ GetNutritionalValue: '{key}' converted from long: {longValue}");
                        return (double)longValue;
                    }
                    
                    if (rawValue is decimal decimalValue)
                    {
                        Console.WriteLine($"✅ GetNutritionalValue: '{key}' converted from decimal: {decimalValue}");
                        return (double)decimalValue;
                    }

                    // Intentar parsing de string
                    if (rawValue is string stringValue && double.TryParse(stringValue, out var parsedValue))
                    {
                        Console.WriteLine($"✅ GetNutritionalValue: '{key}' parsed from string: {parsedValue}");
                        return parsedValue;
                    }

                    // Si es JToken, intentar conversión directa
                    if (rawValue is JToken jToken && jToken.Type != JTokenType.Null)
                    {
                        try
                        {
                            var jTokenValue = jToken.ToObject<double>();
                            Console.WriteLine($"✅ GetNutritionalValue: '{key}' converted from JToken: {jTokenValue}");
                            return jTokenValue;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ GetNutritionalValue: Error converting JToken for '{key}': {ex.Message}");
                        }
                    }
                }

                // Si llegamos aquí, no pudimos obtener el valor - devolver null (será convertido a 0 en el response)
                Console.WriteLine($"⚠️ GetNutritionalValue: '{key}' not found or couldn't parse, returning null");
                return null;
            }

            var result = new FoodData
            {
                Id = GetValue("id", ""),
                TwinID = GetValue<string>("TwinID"),
                NombreAlimento = GetValue<string>("nombreAlimento"),
                Categoria = GetValue<string>("categoria"),
                CaloriasPor100g = GetValue("caloriasPor100g", 0.0),
                Proteinas = GetNutritionalValue("proteinas"), // ✅ Ahora funciona correctamente
                Carbohidratos = GetNutritionalValue("carbohidratos"), // ✅ Ahora funciona correctamente
                Grasas = GetNutritionalValue("grasas"), // ✅ Ahora funciona correctamente
                Fibra = GetNutritionalValue("fibra"), // ✅ Ahora funciona correctamente
                UnidadComun = GetValue("unidadComun", "unidades"),
                CantidadComun = GetValue("cantidadComun", 1.0),
                Descripcion = GetValue<string?>("descripcion"),
                FechaCreacion = GetValue("fechaCreacion", DateTime.UtcNow.ToString("O")),
                FechaActualizacion = GetValue("fechaActualizacion", DateTime.UtcNow.ToString("O")),
                Type = GetValue("type", "food")
            };

            Console.WriteLine($"🎯 FoodData.FromDict completed for food: {result.NombreAlimento}");
            Console.WriteLine($"   - Proteinas: {result.Proteinas}");
            Console.WriteLine($"   - Carbohidratos: {result.Carbohidratos}");
            Console.WriteLine($"   - Grasas: {result.Grasas}");
            Console.WriteLine($"   - Fibra: {result.Fibra}");
            return result;
        }

        private static T ParseJsonElement<T>(JsonElement jsonElement, string key, T defaultValue)
        {
            var type = typeof(T);
            
            if (jsonElement.ValueKind == JsonValueKind.Null)
                return defaultValue;

            if (type == typeof(string))
                return (T)(object)(jsonElement.GetString() ?? string.Empty);
            
            if (type == typeof(double) || type == typeof(double?))
            {
                if (jsonElement.ValueKind == JsonValueKind.Number)
                    return (T)(object)jsonElement.GetDouble();
                if (jsonElement.ValueKind == JsonValueKind.String && double.TryParse(jsonElement.GetString(), out var parsed))
                    return (T)(object)parsed;
            }

            return defaultValue;
        }

        private static T ParseJToken<T>(JToken jToken, string key, T defaultValue)
        {
            var type = typeof(T);
            
            if (jToken.Type == JTokenType.Null || jToken.Type == JTokenType.Undefined)
                return defaultValue;

            try
            {
                if (type == typeof(string))
                    return (T)(object)(jToken.ToString() ?? string.Empty);
                
                if (type == typeof(double) || type == typeof(double?))
                {
                    var doubleValue = jToken.ToObject<double>();
                    return (T)(object)doubleValue;
                }

                return jToken.ToObject<T>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    /// <summary>
    /// Categorías predefinidas de alimentos
    /// </summary>
    public enum CategoriaAlimento
    {
        Frutas,
        Verduras,
        Cereales,
        Legumbres,
        Carnes,
        Pescados,
        Lacteos,
        Frutos_Secos,
        Aceites_Grasas,
        Dulces,
        Bebidas,
        Otros
    }

    /// <summary>
    /// Estadísticas nutricionales de alimentos
    /// </summary>
    public class FoodStats
    {
        public int TotalAlimentos { get; set; } = 0;
        public double PromedioCaloriasPor100g { get; set; } = 0.0;
        public double TotalProteinas { get; set; } = 0.0;
        public double TotalCarbohidratos { get; set; } = 0.0;
        public double TotalGrasas { get; set; } = 0.0;
        public double TotalFibra { get; set; } = 0.0;
        public Dictionary<string, int> AlimentosPorCategoria { get; set; } = new();
        public string? CategoriaConMasAlimentos { get; set; }
        public string? AlimentoConMasCalorias { get; set; }
        public double MaxCalorias { get; set; } = 0.0;
    }

    /// <summary>
    /// Consulta para filtrar alimentos
    /// </summary>
    public class FoodQuery
    {
        public string? Categoria { get; set; }
        public double? CaloriasMin { get; set; }
        public double? CaloriasMax { get; set; }
        public string? NombreContiene { get; set; }
        public string? OrderBy { get; set; } = "nombreAlimento"; // nombreAlimento, categoria, caloriasPor100g
        public string? OrderDirection { get; set; } = "ASC"; // ASC, DESC
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}