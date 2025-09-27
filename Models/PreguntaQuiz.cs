using Newtonsoft.Json;

namespace TwinFx.Models
{
    /// <summary>
    /// Clase para representar una pregunta del quiz generada por AI
    /// </summary>
    public class PreguntaQuiz
    {
        [JsonProperty("pregunta")]
        public string Pregunta { get; set; } = string.Empty;
        
        [JsonProperty("opciones")]
        public List<string> Opciones { get; set; } = new List<string>();
        
        [JsonProperty("respuestaCorrecta")]
        public string RespuestaCorrecta { get; set; } = string.Empty;
        
        [JsonProperty("explicacion")]
        public string Explicacion { get; set; } = string.Empty;
    }
}