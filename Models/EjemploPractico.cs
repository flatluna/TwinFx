using Newtonsoft.Json;

namespace TwinFx.Models
{
    /// <summary>
    /// Clase para representar un ejemplo práctico generado por AI
    /// </summary>
    public class EjemploPractico
    {
        [JsonProperty("titulo")]
        public string Titulo { get; set; } = string.Empty;
        
        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;
        
        [JsonProperty("aplicacion")]
        public string Aplicacion { get; set; } = string.Empty;
    }
}