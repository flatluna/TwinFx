using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Formatters;
using Newtonsoft.Json;
using TwinFx.Services;

namespace TwinFx.Models;

/// <summary>
/// Entrada de diario de actividades con tipado fuerte
/// 📋 Todas las propiedades tienen tipos explícitos para máxima validación
/// </summary>
public class DiaryEntry
{
    // ===== IDENTIFICACIÓN Y CONTROL =====
    
    /// <summary>
    /// Identificador único de la entrada del diario
    /// </summary>
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// ID del Twin propietario (Partition Key)
    /// </summary>
    [JsonPropertyName("twinID")]
    [JsonProperty("twinID")]
    [Required(ErrorMessage = "Twin ID is required")]
    public string TwinId { get; set; } = string.Empty;
    
    /// <summary>
    /// Tipo de documento para Cosmos DB
    /// </summary>
    [JsonPropertyName("type")]
    [JsonProperty("type")]
    public string Type { get; set; } = "diary_entry";
    
    /// <summary>
    /// Versión del documento para control de cambios
    /// </summary>
    [JsonPropertyName("version")]
    [JsonProperty("version")]
    public int Version { get; set; } = 1;
    
    /// <summary>
    /// Indica si la entrada está eliminada (soft delete)
    /// </summary>
    [JsonPropertyName("eliminado")]
    [JsonProperty("eliminado")]
    public bool Eliminado { get; set; } = false;

    // ===== INFORMACIÓN BÁSICA =====
    
    /// <summary>
    /// Título descriptivo de la actividad
    /// </summary>
    [JsonPropertyName("titulo")]
    [JsonProperty("titulo")]
    [Required(ErrorMessage = "Título is required")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Título must be between 2 and 200 characters")]
    public string Titulo { get; set; } = string.Empty;
    
    /// <summary>
    /// Descripción detallada de la actividad
    /// </summary>
    [JsonPropertyName("descripcion")]
    [JsonProperty("descripcion")]
    [StringLength(2000, ErrorMessage = "Descripción cannot exceed 2000 characters")]
    public string Descripcion { get; set; } = string.Empty;
    
    /// <summary>
    /// Fecha de la actividad
    /// </summary>
    [JsonPropertyName("fecha")]
    [JsonProperty("fecha")]
    [Required(ErrorMessage = "Fecha is required")]
    public DateTime Fecha { get; set; }
    
    /// <summary>
    /// Fecha de creación del registro
    /// </summary>
    [JsonPropertyName("fechaCreacion")]
    [JsonProperty("fechaCreacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Fecha de última modificación
    /// </summary>
    [JsonPropertyName("fechaModificacion")]
    [JsonProperty("fechaModificacion")]
    public DateTime FechaModificacion { get; set; } = DateTime.UtcNow;

    // ===== CATEGORIZACIÓN =====
    
    /// <summary>
    /// Tipo de actividad realizada
    /// </summary>
    [JsonPropertyName("tipoActividad")]
    [JsonProperty("tipoActividad")]
    [StringLength(100, ErrorMessage = "TipoActividad cannot exceed 100 characters")]
    public string TipoActividad { get; set; } = string.Empty;
    
    /// <summary>
    /// Etiqueta específica de la actividad
    /// </summary>
    [JsonPropertyName("labelActividad")]
    [JsonProperty("labelActividad")]
    [StringLength(100, ErrorMessage = "LabelActividad cannot exceed 100 characters")]
    public string LabelActividad { get; set; } = string.Empty;

    // ===== UBICACIÓN =====
    
    /// <summary>
    /// Ubicación donde se realizó la actividad
    /// </summary>
    [JsonPropertyName("ubicacion")]
    [JsonProperty("ubicacion")]
    [StringLength(300, ErrorMessage = "Ubicacion cannot exceed 300 characters")]
    public string Ubicacion { get; set; } = string.Empty;
    
    /// <summary>
    /// Coordenada de latitud
    /// </summary>
    [JsonPropertyName("latitud")]
    [JsonProperty("latitud")]
    [Range(-90.0, 90.0, ErrorMessage = "Latitud must be between -90 and 90")]
    public double? Latitud { get; set; }
    
    /// <summary>
    /// Coordenada de longitud
    /// </summary>
    [JsonPropertyName("longitud")]
    [JsonProperty("longitud")]
    [Range(-180.0, 180.0, ErrorMessage = "Longitud must be between -180 and 180")]
    public double? Longitud { get; set; }

    // ===== UBICACIÓN DETALLADA Y CONTACTO =====
    
    /// <summary>
    /// 🌍 País donde se realizó la actividad (solo lectura, extraído automáticamente)
    /// </summary>
    [JsonPropertyName("pais")]
    [JsonProperty("pais")]
    [StringLength(100, ErrorMessage = "Pais cannot exceed 100 characters")]
    public string Pais { get; set; } = string.Empty;
    
    /// <summary>
    /// 🏙️ Ciudad donde se realizó la actividad (solo lectura, extraído automáticamente)
    /// </summary>
    [JsonPropertyName("ciudad")]
    [JsonProperty("ciudad")]
    [StringLength(100, ErrorMessage = "Ciudad cannot exceed 100 characters")]
    public string Ciudad { get; set; } = string.Empty;
    
    /// <summary>
    /// 🏛️ Estado o Provincia donde se realizó la actividad (solo lectura, extraído automáticamente)
    /// </summary>
    [JsonPropertyName("estado")]
    [JsonProperty("estado")]
    [StringLength(100, ErrorMessage = "EstadoProvincia cannot exceed 100 characters")]
    public string Estado { get; set; } = string.Empty;
    
    /// <summary>
    /// 📮 Código Postal del lugar (solo lectura, extraído automáticamente)
    /// </summary>
    [JsonPropertyName("codigoPostal")]
    [JsonProperty("codigoPostal")]
    [StringLength(20, ErrorMessage = "CodigoPostal cannot exceed 20 characters")]
    public string CodigoPostal { get; set; } = string.Empty;
    
    /// <summary>
    /// 📍 Dirección específica del lugar (solo lectura, extraída automáticamente)
    /// </summary>
    [JsonPropertyName("direccionEspecifica")]
    [JsonProperty("direccionEspecifica")]
    [StringLength(300, ErrorMessage = "DireccionEspecifica cannot exceed 300 characters")]
    public string DireccionEspecifica { get; set; } = string.Empty;
    
    /// <summary>
    /// 📞 Teléfono del establecimiento o lugar (solo lectura, extraído automáticamente)
    /// </summary>
    [JsonPropertyName("telefono")]
    [JsonProperty("telefono")]
    [StringLength(20, ErrorMessage = "Telefono cannot exceed 20 characters")]
    public string Telefono { get; set; } = string.Empty;
    
    /// <summary>
    /// 🌐 Website del establecimiento o lugar (solo lectura, extraído automáticamente)
    /// </summary>
    [JsonPropertyName("website")]
    [JsonProperty("website")]
    [StringLength(200, ErrorMessage = "Website cannot exceed 200 characters")]
    public string Website { get; set; } = string.Empty;
    
    /// <summary>
    /// 🏘️ Distrito o Colonia del lugar (editable manualmente por el usuario)
    /// </summary>
    [JsonPropertyName("distritoColonia")]
    [JsonProperty("distritoColonia")]
    [StringLength(100, ErrorMessage = "DistritoColonia cannot exceed 100 characters")]
    public string DistritoColonia { get; set; } = string.Empty;

    // ===== ESTADO EMOCIONAL Y ENERGÍA =====
    
    /// <summary>
    /// Estado emocional durante la actividad
    /// </summary>
    [JsonPropertyName("estadoEmocional")]
    [JsonProperty("estadoEmocional")]
    [StringLength(100, ErrorMessage = "EstadoEmocional cannot exceed 100 characters")]
    public string EstadoEmocional { get; set; } = string.Empty;
    
    /// <summary>
    /// Nivel de energía durante la actividad (1-5)
    /// </summary>
    [JsonPropertyName("nivelEnergia")]
    [JsonProperty("nivelEnergia")]
    [Range(1, 5, ErrorMessage = "NivelEnergia must be between 1 and 5")]
    public int? NivelEnergia { get; set; }

    // ===== ACTIVIDADES COMERCIALES (COMPRAS) =====
    
    /// <summary>
    /// Gasto total de la actividad
    /// </summary>
    [JsonPropertyName("gastoTotal")]
    [JsonProperty("gastoTotal")]
    [Range(0, double.MaxValue, ErrorMessage = "GastoTotal must be greater than or equal to 0")]
    public decimal? GastoTotal { get; set; }
    
    /// <summary>
    /// Productos comprados durante la actividad
    /// </summary>
    [JsonPropertyName("productosComprados")]
    [JsonProperty("productosComprados")]
    [StringLength(1000, ErrorMessage = "ProductosComprados cannot exceed 1000 characters")]
    public string ProductosComprados { get; set; } = string.Empty;
    
    /// <summary>
    /// Tienda o lugar donde se realizó la compra
    /// </summary>
    [JsonPropertyName("tiendaLugar")]
    [JsonProperty("tiendaLugar")]
    [StringLength(200, ErrorMessage = "TiendaLugar cannot exceed 200 characters")]
    public string TiendaLugar { get; set; } = string.Empty;
    
    /// <summary>
    /// Método de pago utilizado
    /// </summary>
    [JsonPropertyName("metodoPago")]
    [JsonProperty("metodoPago")]
    [StringLength(50, ErrorMessage = "MetodoPago cannot exceed 50 characters")]
    public string MetodoPago { get; set; } = string.Empty;
    
    /// <summary>
    /// Categoría de la compra realizada
    /// </summary>
    [JsonPropertyName("categoriaCompra")]
    [JsonProperty("categoriaCompra")]
    [StringLength(100, ErrorMessage = "CategoriaCompra cannot exceed 100 characters")]
    public string CategoriaCompra { get; set; } = string.Empty;
    
    /// <summary>
    /// Nivel de satisfacción con la compra (1-5)
    /// </summary>
    [JsonPropertyName("satisfaccionCompra")]
    [JsonProperty("satisfaccionCompra")]
    [Range(1, 5, ErrorMessage = "SatisfaccionCompra must be between 1 and 5")]
    public int? SatisfaccionCompra { get; set; }

    // ===== ACTIVIDADES GASTRONÓMICAS =====
    
    /// <summary>
    /// Costo de la comida
    /// </summary>
    [JsonPropertyName("costo_comida")]
    [JsonProperty("costoComida")]
    [Range(0, double.MaxValue, ErrorMessage = "CostoComida must be greater than or equal to 0")]
    public decimal? CostoComida { get; set; }
    
    /// <summary>
    /// Restaurante o lugar donde se comió
    /// </summary>
    [JsonPropertyName("restaurante_lugar")]
    [JsonProperty("restauranteLugar")]
    [StringLength(200, ErrorMessage = "RestauranteLugar cannot exceed 200 characters")]
    public string RestauranteLugar { get; set; } = string.Empty;
    
    /// <summary>
    /// Tipo de cocina consumida
    /// </summary>
    [JsonPropertyName("tipo_cocina")]
    [JsonProperty("tipoCocina")]
    [StringLength(100, ErrorMessage = "TipoCocina cannot exceed 100 characters")]
    public string TipoCocina { get; set; } = string.Empty;
    
    /// <summary>
    /// Platos ordenados durante la comida
    /// </summary>
    [JsonPropertyName("platos_ordenados")]
    [JsonProperty("platosOrdenados")]
    [StringLength(500, ErrorMessage = "PlatosOrdenados cannot exceed 500 characters")]
    public string PlatosOrdenados { get; set; } = string.Empty;
    
    /// <summary>
    /// Calificación de la experiencia gastronómica (1-5)
    /// </summary>
    [JsonPropertyName("calificacion_comida")]
    [JsonProperty("calificacionComida")]
    [Range(1, 5, ErrorMessage = "CalificacionComida must be between 1 and 5")]
    public int? CalificacionComida { get; set; }
    
    /// <summary>
    /// Ambiente del lugar donde se comió
    /// </summary>
    [JsonPropertyName("ambienteComida")]
    [JsonProperty("ambienteComida")]
    [StringLength(200, ErrorMessage = "AmbienteComida cannot exceed 200 characters")]
    public string AmbienteComida { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicador si recomendaría la comida/restaurante
    /// </summary>
    [JsonPropertyName("recomendariaComida")]
    [JsonProperty("recomendariaComida")]
    public bool? RecomendariaComida { get; set; }

    // ===== ACTIVIDADES DE VIAJE =====
    
    /// <summary>
    /// Costo del viaje
    /// </summary>
    [JsonPropertyName("costoViaje")]
    [JsonProperty("costoViaje")]
    [Range(0, double.MaxValue, ErrorMessage = "CostoViaje must be greater than or equal to 0")]
    public decimal? CostoViaje { get; set; }
    
    /// <summary>
    /// Destino del viaje
    /// </summary>
    [JsonPropertyName("destinoViaje")]
    [JsonProperty("destinoViaje")]
    [StringLength(200, ErrorMessage = "DestinoViaje cannot exceed 200 characters")]
    public string DestinoViaje { get; set; } = string.Empty;
    
    /// <summary>
    /// Medio de transporte utilizado
    /// </summary>
    [JsonPropertyName("transporteViaje")]
    [JsonProperty("transporteViaje")]
    [StringLength(100, ErrorMessage = "TransporteViaje cannot exceed 100 characters")]
    public string TransporteViaje { get; set; } = string.Empty;
    
    /// <summary>
    /// Propósito del viaje
    /// </summary>
    [JsonPropertyName("propositoViaje")]
    [JsonProperty("propositoViaje")]
    [StringLength(200, ErrorMessage = "PropositoViaje cannot exceed 200 characters")]
    public string PropositoViaje { get; set; } = string.Empty;
    
    /// <summary>
    /// Calificación de la experiencia de viaje (1-5)
    /// </summary>
    [JsonPropertyName("calificacionViaje")]
    [JsonProperty("calificacionViaje")]
    [Range(1, 5, ErrorMessage = "CalificacionViaje must be between 1 and 5")]
    public int? CalificacionViaje { get; set; }
    
    /// <summary>
    /// Duración del viaje en horas
    /// </summary>
    [JsonPropertyName("duracionViaje")]
    [JsonProperty("duracionViaje")]
    [Range(0, int.MaxValue, ErrorMessage = "DuracionViaje must be greater than or equal to 0")]
    public int? DuracionViaje { get; set; }

    // ===== ACTIVIDADES DE ENTRETENIMIENTO =====
    
    /// <summary>
    /// Costo del entretenimiento
    /// </summary>
    [JsonPropertyName("costoEntretenimiento")]
    [JsonProperty("costoEntretenimiento")]
    [Range(0, double.MaxValue, ErrorMessage = "CostoEntretenimiento must be greater than or equal to 0")]
    public decimal? CostoEntretenimiento { get; set; }
    
    /// <summary>
    /// Calificación de la actividad de entretenimiento (1-5)
    /// </summary>
    [JsonPropertyName("calificacionEntretenimiento")]
    [JsonProperty("calificacionEntretenimiento")]
    [Range(1, 5, ErrorMessage = "CalificacionEntretenimiento must be between 1 and 5")]
    public int? CalificacionEntretenimiento { get; set; }
    
    /// <summary>
    /// Tipo de entretenimiento realizado
    /// </summary>
    [JsonPropertyName("tipoEntretenimiento")]
    [JsonProperty("tipoEntretenimiento")]
    [StringLength(100, ErrorMessage = "TipoEntretenimiento cannot exceed 100 characters")]
    public string TipoEntretenimiento { get; set; } = string.Empty;
    
    /// <summary>
    /// Título o nombre específico del entretenimiento
    /// </summary>
    [JsonPropertyName("tituloNombre")]
    [JsonProperty("tituloNombre")]
    [StringLength(200, ErrorMessage = "TituloNombre cannot exceed 200 characters")]
    public string TituloNombre { get; set; } = string.Empty;
    
    /// <summary>
    /// Lugar donde se realizó el entretenimiento
    /// </summary>
    [JsonPropertyName("lugarEntretenimiento")]
    [JsonProperty("lugarEntretenimiento")]
    [StringLength(200, ErrorMessage = "LugarEntretenimiento cannot exceed 200 characters")]
    public string LugarEntretenimiento { get; set; } = string.Empty;

    // ===== ACTIVIDADES DE EJERCICIO =====
    
    /// <summary>
    /// Costo del ejercicio (gimnasio, clases, etc.)
    /// </summary>
    [JsonPropertyName("costo_ejercicio")]
    [JsonProperty("costoEjercicio")]
    [Range(0, double.MaxValue, ErrorMessage = "CostoEjercicio must be greater than or equal to 0")]
    public decimal? CostoEjercicio { get; set; }
    
    /// <summary>
    /// Nivel de energía después del ejercicio (1-5)
    /// </summary>
    [JsonPropertyName("energia_post")]
    [JsonProperty("energiaPostEjercicio")]
    [Range(1, 5, ErrorMessage = "EnergiaPostEjercicio must be between 1 and 5")]
    public int? EnergiaPostEjercicio { get; set; }
    
    /// <summary>
    /// Calorías quemadas durante el ejercicio
    /// </summary>
    [JsonPropertyName("calorias_quemadas")]
    [JsonProperty("caloriasQuemadas")]
    [Range(0, int.MaxValue, ErrorMessage = "CaloriasQuemadas must be greater than or equal to 0")]
    public int? CaloriasQuemadas { get; set; }
    
    /// <summary>
    /// Tipo de ejercicio realizado
    /// </summary>
    [JsonPropertyName("tipo_ejercicio")]
    [JsonProperty("tipoEjercicio")]
    [StringLength(100, ErrorMessage = "TipoEjercicio cannot exceed 100 characters")]
    public string TipoEjercicio { get; set; } = string.Empty;
    
    /// <summary>
    /// Duración del ejercicio en minutos
    /// </summary>
    [JsonPropertyName("duracion_ejercicio")]
    [JsonProperty("duracionEjercicio")]
    [Range(0, int.MaxValue, ErrorMessage = "DuracionEjercicio must be greater than or equal to 0")]
    public int? DuracionEjercicio { get; set; }
    
    /// <summary>
    /// Intensidad del ejercicio (1-5, siendo 5 muy intenso)
    /// </summary>
    [JsonPropertyName("intensidad")]
    [JsonProperty("intensidadEjercicio")]
    [Range(1, 5, ErrorMessage = "IntensidadEjercicio must be between 1 and 5")]
    public int? IntensidadEjercicio { get; set; }
    
    /// <summary>
    /// Lugar donde se realizó el ejercicio
    /// </summary>
    [JsonPropertyName("lugar_ejercicio")]
    [JsonProperty("lugarEjercicio")]
    [StringLength(200, ErrorMessage = "LugarEjercicio cannot exceed 200 characters")]
    public string LugarEjercicio { get; set; } = string.Empty;
    
    /// <summary>
    /// Rutina específica realizada
    /// </summary>
    [JsonPropertyName("rutina_especifica")]
    [JsonProperty("rutinaEspecifica")]
    [StringLength(500, ErrorMessage = "RutinaEspecifica cannot exceed 500 characters")]
    public string RutinaEspecifica { get; set; } = string.Empty;

    // ===== ACTIVIDADES DE ESTUDIO =====
    
    /// <summary>
    /// Costo del estudio (cursos, materiales, etc.)
    /// </summary>
    [JsonPropertyName("costoEstudio")]
    [JsonProperty("costoEstudio")]
    [Range(0, double.MaxValue, ErrorMessage = "CostoEstudio must be greater than or equal to 0")]
    public decimal? CostoEstudio { get; set; }
    
    /// <summary>
    /// Dificultad del material estudiado (1-5)
    /// </summary>
    [JsonPropertyName("dificultadEstudio")]
    [JsonProperty("dificultadEstudio")]
    [Range(1, 5, ErrorMessage = "DificultadEstudio must be between 1 and 5")]
    public int? DificultadEstudio { get; set; }
    
    /// <summary>
    /// Estado de ánimo después del estudio (1-5)
    /// </summary>
    [JsonPropertyName("estadoAnimoPost")]
    [JsonProperty("estadoAnimoPost")]
    [Range(1, 5, ErrorMessage = " EstadoAnimoPost must be between 1 and 5")]
    public int? EstadoAnimoPost { get; set; }
    
    /// <summary>
    /// Materia o tema estudiado
    /// </summary>
    [JsonPropertyName("materiaTema")]
    [JsonProperty("materiaTema")]
    [StringLength(200, ErrorMessage = "MateriaTema cannot exceed 200 characters")]
    public string MateriaTema { get; set; } = string.Empty;
    
    /// <summary>
    /// Material de estudio utilizado
    /// </summary>
    [JsonPropertyName("materialEstudio")]
    [JsonProperty("materialEstudio")]
    [StringLength(300, ErrorMessage = "MaterialEstudio cannot exceed 300 characters")]
    public string MaterialEstudio { get; set; } = string.Empty;
    
    /// <summary>
    /// Duración del estudio en minutos
    /// </summary>
    [JsonPropertyName("duracionEstudio")]
    [JsonProperty("duracionEstudio")]
    [Range(0, int.MaxValue, ErrorMessage = "DuracionEstudio must be greater than or equal to 0")]
    public int? DuracionEstudio { get; set; }
    
    /// <summary>
    /// Progreso percibido en el estudio (1-5)
    /// </summary>
    [JsonPropertyName("progresoEstudio")]
    [JsonProperty("progresoEstudio")]
    [Range(1, 5, ErrorMessage = "ProgresoEstudio must be between 1 and 5")]
    public int? ProgresoEstudio { get; set; }

    // ===== ACTIVIDADES DE TRABAJO =====
    
    /// <summary>
    /// Horas trabajadas durante la actividad
    /// </summary>
    [JsonPropertyName("horasTrabajadas")]
    [JsonProperty("horasTrabajadas")]
    [Range(0, 24, ErrorMessage = "HorasTrabajadas must be between 0 and 24")]
    public int? HorasTrabajadas { get; set; }
    
    /// <summary>
    /// Proyecto principal en el que se trabajó
    /// </summary>
    [JsonPropertyName("proyectoPrincipal")]
    [JsonProperty("proyectoPrincipal")]
    [StringLength(200, ErrorMessage = "ProyectoPrincipal cannot exceed 200 characters")]
    public string ProyectoPrincipal { get; set; } = string.Empty;
    
    /// <summary>
    /// Número de reuniones de trabajo asistidas
    /// </summary>
    [JsonPropertyName("reunionesTrabajo")]
    [JsonProperty("reunionesTrabajo")]
    [Range(0, int.MaxValue, ErrorMessage = "ReunionesTrabajo must be greater than or equal to 0")]
    public int? ReunionesTrabajo { get; set; }
    
    /// <summary>
    /// Logros principales del día laboral
    /// </summary>
    [JsonPropertyName("logrosHoy")]
    [JsonProperty("logrosHoy")]
    [StringLength(500, ErrorMessage = "LogrosHoy cannot exceed 500 characters")]
    public string LogrosHoy { get; set; } = string.Empty;
    
    /// <summary>
    /// Desafíos enfrentados en el trabajo
    /// </summary>
    [JsonPropertyName("desafiosTrabajo")]
    [JsonProperty("desafiosTrabajo")]
    [StringLength(500, ErrorMessage = "DesafiosTrabajo cannot exceed 500 characters")]
    public string DesafiosTrabajo { get; set; } = string.Empty;
    
    /// <summary>
    /// Estado de ánimo relacionado al trabajo (1-5)
    /// </summary>
    [JsonPropertyName("moodTrabajo")]
    [JsonProperty("moodTrabajo")]
    [Range(1, 5, ErrorMessage = "MoodTrabajo must be between 1 and 5")]
    public int? MoodTrabajo { get; set; }

    // ===== ACTIVIDADES DE SALUD =====
    
    /// <summary>
    /// Costo de actividades relacionadas con salud
    /// </summary>
    [JsonPropertyName("costoSalud")]
    [JsonProperty("costoSalud")]
    [Range(0, double.MaxValue, ErrorMessage = "CostoSalud must be greater than or equal to 0")]
    public decimal? CostoSalud { get; set; }
    
    /// <summary>
    /// Tipo de consulta médica o actividad de salud
    /// </summary>
    [JsonPropertyName("tipoConsulta")]
    [JsonProperty("tipoConsulta")]
    [StringLength(100, ErrorMessage = "TipoConsulta cannot exceed 100 characters")]
    public string TipoConsulta { get; set; } = string.Empty;
    
    /// <summary>
    /// Profesional o centro médico visitado
    /// </summary>
    [JsonPropertyName("profesionalCentro")]
    [JsonProperty("profesionalCentro")]
    [StringLength(200, ErrorMessage = "ProfesionalCentro cannot exceed 200 characters")]
    public string ProfesionalCentro { get; set; } = string.Empty;
    
    /// <summary>
    /// Motivo de la consulta médica
    /// </summary>
    [JsonPropertyName("motivoConsulta")]
    [JsonProperty("motivoConsulta")]
    [StringLength(300, ErrorMessage = "MotivoConsulta cannot exceed 300 characters")]
    public string MotivoConsulta { get; set; } = string.Empty;
    
    /// <summary>
    /// Tratamiento o medicamento recetado
    /// </summary>
    [JsonPropertyName("tratamientoRecetado")]
    [JsonProperty("tratamientoRecetado")]
    [StringLength(300, ErrorMessage = "TratamientoRecetado cannot exceed 300 characters")]
    public string TratamientoRecetado { get; set; } = string.Empty;
    
    /// <summary>
    /// Fecha de próxima cita médica
    /// </summary>
    [JsonPropertyName("proximaCita")]
    [JsonProperty("proximaCita")]
    public DateTime? ProximaCita { get; set; }

    // ===== ACTIVIDADES DE COMUNICACIÓN (LLAMADAS) =====
    
    /// <summary>
    /// Contacto con quien se realizó la llamada
    /// </summary>
    [JsonPropertyName("contactoLlamada")]
    [JsonProperty("contactoLlamada")]
    [StringLength(200, ErrorMessage = "ContactoLlamada cannot exceed 200 characters")]
    public string ContactoLlamada { get; set; } = string.Empty;
    
    /// <summary>
    /// Duración de la llamada en minutos
    /// </summary>
    [JsonPropertyName("duracionLlamada")]
    [JsonProperty("duracionLlamada")]
    [Range(0, int.MaxValue, ErrorMessage = "DuracionLlamada must be greater than or equal to 0")]
    public int? DuracionLlamada { get; set; }
    
    /// <summary>
    /// Motivo principal de la llamada
    /// </summary>
    [JsonPropertyName("motivoLlamada")]
    [JsonProperty("motivoLlamada")]
    [StringLength(300, ErrorMessage = "MotivoLlamada cannot exceed 300 characters")]
    public string MotivoLlamada { get; set; } = string.Empty;
    
    /// <summary>
    /// Temas principales de la conversación
    /// </summary>
    [JsonPropertyName("temasConversacion")]
    [JsonProperty("temasConversacion")]
    [StringLength(500, ErrorMessage = "TemasConversacion cannot exceed 500 characters")]
    public string TemasConversacion { get; set; } = string.Empty;
    
    /// <summary>
    /// Tipo de llamada (personal, trabajo, familia, etc.)
    /// </summary>
    [JsonPropertyName("tipoLlamada")]
    [JsonProperty("tipoLlamada")]
    [StringLength(100, ErrorMessage = "TipoLlamada cannot exceed 100 characters")]
    public string TipoLlamada { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicador si requiere seguimiento
    /// </summary>
    [JsonPropertyName("seguimientoLlamada")]
    [JsonProperty("seguimientoLlamada")]
    public bool? SeguimientoLlamada { get; set; }

    // ===== PARTICIPANTES Y PERSONAS PRESENTES =====
    
    /// <summary>
    /// Personas que participaron en la actividad
    /// </summary>
    [JsonPropertyName("participantes")]
    [JsonProperty("participantes")]
    [StringLength(500, ErrorMessage = "Participantes cannot exceed 500 characters")]
    public string Participantes { get; set; } = string.Empty;

    // ===== ARCHIVOS Y DOCUMENTOS =====
    
    /// <summary>
    /// Ruta del archivo adjunto (recibo, imagen, documento, etc.)
    /// </summary>
    [JsonPropertyName("pathFile")]
    [JsonProperty("pathFile")]
    public string PathFile { get; set; } = string.Empty;
    
    /// <summary>
    /// URL SAS para acceder al archivo (se genera dinámicamente)
    /// </summary>
    [JsonPropertyName("sasUrl")]
    [JsonProperty("sasUrl")]
    public string SasUrl { get; set; } = string.Empty;

    /// <summary>
    /// Análisis comprensivo de la entrada del diario desde Azure AI Search
    /// </summary>
    [JsonPropertyName("diaryIndex")]
    [JsonProperty("diaryIndex")]
    public DiaryAnalysisResponseItem? DiaryIndex { get; set; } = new DiaryAnalysisResponseItem();
}

/// <summary>
/// Request model para crear una nueva entrada de diario
/// </summary>
public class CreateDiaryEntryRequest
{
    [Required(ErrorMessage = "Título is required")]
    public string Titulo { get; set; } = string.Empty;
    
    public string Descripcion { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Fecha is required")]
    public DateTime Fecha { get; set; }
    
    public string TipoActividad { get; set; } = string.Empty;
    public string LabelActividad { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    
    // Nuevos campos de ubicación detallada y contacto
    public string Pais { get; set; } = string.Empty;
    public string Ciudad { get; set; } = string.Empty;
    public string EstadoProvincia { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string DireccionEspecifica { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string DistritoColonia { get; set; } = string.Empty;
    
    public string EstadoEmocional { get; set; } = string.Empty;
    public int? NivelEnergia { get; set; }
    
    // Campos específicos por actividad
    public decimal? GastoTotal { get; set; }
    public string ProductosComprados { get; set; } = string.Empty;
    public string TiendaLugar { get; set; } = string.Empty;
    public string MetodoPago { get; set; } = string.Empty;
    public string CategoriaCompra { get; set; } = string.Empty;
    public int? SatisfaccionCompra { get; set; }
    
    public decimal? CostoComida { get; set; }
    public string RestauranteLugar { get; set; } = string.Empty;
    public string TipoCocina { get; set; } = string.Empty;
    public string PlatosOrdenados { get; set; } = string.Empty;
    public int? CalificacionComida { get; set; }
    public string AmbienteComida { get; set; } = string.Empty;
    public bool? RecomendariaComida { get; set; }
    
    public decimal? CostoViaje { get; set; }
    public string DestinoViaje { get; set; } = string.Empty;
    public string TransporteViaje { get; set; } = string.Empty;
    public string PropositoViaje { get; set; } = string.Empty;
    public int? CalificacionViaje { get; set; }
    public int? DuracionViaje { get; set; }
    
    public decimal? CostoEntretenimiento { get; set; }
    public int? CalificacionEntretenimiento { get; set; }
    public string TipoEntretenimiento { get; set; } = string.Empty;
    public string TituloNombre { get; set; } = string.Empty;
    public string LugarEntretenimiento { get; set; } = string.Empty;
    
    public decimal? CostoEjercicio { get; set; }
    public int? EnergiaPostEjercicio { get; set; }
    public int? CaloriasQuemadas { get; set; }
    public string TipoEjercicio { get; set; } = string.Empty;
    public int? DuracionEjercicio { get; set; }
    public int? IntensidadEjercicio { get; set; }
    public string LugarEjercicio { get; set; } = string.Empty;
    public string RutinaEspecifica { get; set; } = string.Empty;
    
    public decimal? CostoEstudio { get; set; }
    public int? DificultadEstudio { get; set; }
    public int? EstadoAnimoPost { get; set; }
    public string MateriaTema { get; set; } = string.Empty;
    public string MaterialEstudio { get; set; } = string.Empty;
    public int? DuracionEstudio { get; set; }
    public int? ProgresoEstudio { get; set; }
    
    public int? HorasTrabajadas { get; set; }
    public string ProyectoPrincipal { get; set; } = string.Empty;
    public int? ReunionesTrabajo { get; set; }
    public string LogrosHoy { get; set; } = string.Empty;
    public string DesafiosTrabajo { get; set; } = string.Empty;
    public int? MoodTrabajo { get; set; }
    
    public decimal? CostoSalud { get; set; }
    public string TipoConsulta { get; set; } = string.Empty;
    public string ProfesionalCentro { get; set; } = string.Empty;
    public string MotivoConsulta { get; set; } = string.Empty;
    public string TratamientoRecetado { get; set; } = string.Empty;
    public DateTime? ProximaCita { get; set; }
    
    public string ContactoLlamada { get; set; } = string.Empty;
    public int? DuracionLlamada { get; set; }
    public string MotivoLlamada { get; set; } = string.Empty;
    public string TemasConversacion { get; set; } = string.Empty;
    public string TipoLlamada { get; set; } = string.Empty;
    public bool? SeguimientoLlamada { get; set; }
    
    // Personas presentes
    public string Participantes { get; set; } = string.Empty;
    
    // Archivo adjunto
    public string PathFile { get; set; } = string.Empty;
}

/// <summary>
/// Request model para actualizar una entrada de diario existente
/// </summary>
public class UpdateDiaryEntryRequest
{
    public string? Titulo { get; set; }
    public string? Descripcion { get; set; }
    public DateTime? Fecha { get; set; }
    public string? TipoActividad { get; set; }
    public string? LabelActividad { get; set; }
    public string? Ubicacion { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    
    // Nuevos campos de ubicación detallada y contacto (opcionales para updates)
    public string? Pais { get; set; }
    public string? Ciudad { get; set; }
    public string? Estado { get; set; }
    public string? CodigoPostal { get; set; }
    public string? DireccionEspecifica { get; set; }
    public string? Telefono { get; set; }
    public string? Website { get; set; }
    public string? DistritoColonia { get; set; }
    
    public string? EstadoEmocional { get; set; }
    public int? NivelEnergia { get; set; }
    
    // Campos específicos por actividad (todos opcionales para update)
    public decimal? GastoTotal { get; set; }
    public string? ProductosComprados { get; set; }
    public string? TiendaLugar { get; set; }
    public string? MetodoPago { get; set; }
    public string? CategoriaCompra { get; set; }
    public int? SatisfaccionCompra { get; set; }
    
    public decimal? CostoComida { get; set; }
    public string? RestauranteLugar { get; set; }
    public string? TipoCocina { get; set; }
    public string? PlatosOrdenados { get; set; }
    public int? CalificacionComida { get; set; }
    public string? AmbienteComida { get; set; }
    public bool? RecomendariaComida { get; set; }
    
    public decimal? CostoViaje { get; set; }
    public string? DestinoViaje { get; set; }
    public string? TransporteViaje { get; set; }
    public string? PropositoViaje { get; set; }
    public int? CalificacionViaje { get; set; }
    public int? DuracionViaje { get; set; }
    
    public decimal? CostoEntretenimiento { get; set; }
    public int? CalificacionEntretenimiento { get; set; }
    public string? TipoEntretenimiento { get; set; }
    public string? TituloNombre { get; set; }
    public string? LugarEntretenimiento { get; set; }
    
    public decimal? CostoEjercicio { get; set; }
    public int? EnergiaPostEjercicio { get; set; }
    public int? CaloriasQuemadas { get; set; }
    public string? TipoEjercicio { get; set; }
    public int? DuracionEjercicio { get; set; }
    public int? IntensidadEjercicio { get; set; }
    public string? LugarEjercicio { get; set; }
    public string? RutinaEspecifica { get; set; }
    
    public decimal? CostoEstudio { get; set; }
    public int? DificultadEstudio { get; set; }
    public int? EstadoAnimoPost { get; set; }
    public string? MateriaTema { get; set; }
    public string? MaterialEstudio { get; set; }
    public int? DuracionEstudio { get; set; }
    public int? ProgresoEstudio { get; set; }
    
    public int? HorasTrabajadas { get; set; }
    public string? ProyectoPrincipal { get; set; }
    public int? ReunionesTrabajo { get; set; }
    public string? LogrosHoy { get; set; }
    public string? DesafiosTrabajo { get; set; }
    public int? MoodTrabajo { get; set; }
    
    public decimal? CostoSalud { get; set; }
    public string? TipoConsulta { get; set; }
    public string? ProfesionalCentro { get; set; }
    public string? MotivoConsulta { get; set; }
    public string? TratamientoRecetado { get; set; }
    public DateTime? ProximaCita { get; set; }
    
    public string? ContactoLlamada { get; set; }
    public int? DuracionLlamada { get; set; }
    public string? MotivoLlamada { get; set; }
    public string? TemasConversacion { get; set; }
    public string? TipoLlamada { get; set; }
    public bool? SeguimientoLlamada { get; set; }
    
    // Personas presentes
    public string? Participantes { get; set; }
    
    // Archivo adjunto
    public string? PathFile { get; set; }
}

/// <summary>
/// Query parameters para filtrar entradas de diario
/// </summary>
public class DiaryEntryQuery
{
    /// <summary>
    /// Filtrar por tipo de actividad
    /// </summary>
    public string? TipoActividad { get; set; }
    
    /// <summary>
    /// Filtrar por rango de fechas (desde)
    /// </summary>
    public DateTime? FechaDesde { get; set; }
    
    /// <summary>
    /// Filtrar por rango de fechas (hasta)
    /// </summary>
    public DateTime? FechaHasta { get; set; }
    
    /// <summary>
    /// Filtrar por ubicación (búsqueda parcial)
    /// </summary>
    public string? Ubicacion { get; set; }
    
    /// <summary>
    /// Filtrar por estado emocional
    /// </summary>
    public string? EstadoEmocional { get; set; }
    
    /// <summary>
    /// Filtrar por nivel mínimo de energía
    /// </summary>
    public int? NivelEnergiaMin { get; set; }
    
    /// <summary>
    /// Filtrar por gasto máximo
    /// </summary>
    public decimal? GastoMaximo { get; set; }
    
    /// <summary>
    /// Búsqueda de texto libre en título y descripción
    /// </summary>
    public string? SearchTerm { get; set; }
    
    /// <summary>
    /// Número de página para paginación (base 1)
    /// </summary>
    public int Page { get; set; } = 1;
    
    /// <summary>
    /// Tamaño de página
    /// </summary>
    public int PageSize { get; set; } = 20;
    
    /// <summary>
    /// Campo por el cual ordenar
    /// </summary>
    public string SortBy { get; set; } = "fecha";
    
    /// <summary>
    /// Dirección del ordenamiento (asc/desc)
    /// </summary>
    public string SortDirection { get; set; } = "desc";
}

/// <summary>
/// Response model para operaciones con entradas de diario
/// </summary>
public class DiaryEntryResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public DiaryEntry? Entry { get; set; }
    public List<DiaryEntry>? Entries { get; set; }
    public DiaryStats? Stats { get; set; }
    public int TotalEntries { get; set; }
    public string TwinId { get; set; } = string.Empty;
    
    /// <summary>
    /// Lista de recibos subidos exitosamente durante la creación de la entrada
    /// </summary>
    public List<object>? UploadedReceipts { get; set; }
}

/// <summary>
/// Estadísticas del diario de actividades
/// </summary>
public class DiaryStats
{
    public int TotalEntries { get; set; }
    public Dictionary<string, int> ByActivityType { get; set; } = new();
    public Dictionary<string, int> ByEmotionalState { get; set; } = new();
    public decimal TotalSpent { get; set; }
    public double AverageEnergyLevel { get; set; }
    public int TotalCaloriesBurned { get; set; }
    public int TotalHoursWorked { get; set; }
    public Dictionary<string, decimal> SpendingByCategory { get; set; } = new();
    public List<string> TopLocations { get; set; } = new();
    public DateTime? MostRecentEntry { get; set; }
    public DateTime? OldestEntry { get; set; }
}

// ===== ANALYSIS AND COMPREHENSIVE RESULTS =====

/// <summary>
/// Resultado del análisis comprensivo de diario con recibo
/// Combina información extraída del recibo con datos del diario usando AI
/// </summary>
public class DiaryComprehensiveAnalysisResult
{
    /// <summary>
    /// Indica si el análisis fue exitoso
    /// </summary>
    public bool Success { get; set; } = false;
    
    /// <summary>
    /// Sumario ejecutivo del análisis en texto plano
    /// </summary>
    public string ExecutiveSummary { get; set; } = string.Empty;
    
    /// <summary>
    /// Reporte detallado en formato HTML con estilos y colores
    /// </summary>
    public string DetailedHtmlReport { get; set; } = string.Empty;
    
    /// <summary>
    /// Mensaje de error si el análisis falló
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Tiempo de procesamiento en milisegundos
    /// </summary>
    public double ProcessingTimeMs { get; set; }
    
    /// <summary>
    /// Timestamp del análisis
    /// </summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// ID de la entrada del diario analizada
    /// </summary>
    public string DiaryEntryId { get; set; } = string.Empty;
    
    /// <summary>
    /// Información extraída del recibo (referencia)
    /// </summary>
    public object? ReceiptData { get; set; }
    
    /// <summary>
    /// Metadatos adicionales del análisis
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Individual diary analysis response item with all relevant data
/// </summary>
public class DiaryAnalysisResponseItem
{
    [JsonPropertyName("success")]
    [JsonProperty("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("diaryEntryId")]
    [JsonProperty("diaryEntryId")]
    public string DiaryEntryId { get; set; } = string.Empty;
    
    [JsonPropertyName("twinId")]
    [JsonProperty("twinId")]
    public string TwinId { get; set; } = string.Empty;
    
    [JsonPropertyName("executiveSummary")]
    [JsonProperty("executiveSummary")]
    public string ExecutiveSummary { get; set; } = string.Empty;
    
    [JsonPropertyName("detailedHtmlReport")]
    [JsonProperty("detailedHtmlReport")]
    public string DetailedHtmlReport { get; set; } = string.Empty;
    
    [JsonPropertyName("processingTimeMs")]
    [JsonProperty("processingTimeMs")]
    public double ProcessingTimeMs { get; set; }
    
    [JsonPropertyName("analyzedAt")]
    [JsonProperty("analyzedAt")]
    public string AnalyzedAt { get; set; } = string.Empty;
    
    [JsonPropertyName("errorMessage")]
    [JsonProperty("errorMessage")]
    public string? ErrorMessage { get; set; }
    
    [JsonPropertyName("metadataKeys")]
    [JsonProperty("metadataKeys")]
    public string MetadataKeys { get; set; } = string.Empty;
    
    [JsonPropertyName("metadataValues")]
    [JsonProperty("metadataValues")]
    public string MetadataValues { get; set; } = string.Empty;
    
    [JsonPropertyName("contenidoCompleto")]
    [JsonProperty("contenidoCompleto")]
    public string ContenidoCompleto { get; set; } = string.Empty;
}