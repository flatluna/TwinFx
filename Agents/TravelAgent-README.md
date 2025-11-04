# TravelAgent - Gestión de Viajes TwinFx

?? **Agente especializado para la gestión completa de viajes y experiencias de viaje**

## ?? Descripción General

El TravelAgent es un agente inteligente que forma parte del ecosistema TwinFx para la gestión completa de viajes y experiencias de viaje. Permite crear, actualizar, consultar y eliminar registros de viajes con capacidades avanzadas de filtrado y análisis estadístico.

## ? Características Principales

### ?? **Gestión Completa de Viajes**
- **Creación de viajes**: Registra nuevos viajes con información detallada
- **Actualización**: Modifica cualquier aspecto de un viaje existente
- **Eliminación**: Borra registros de viajes
- **Consulta avanzada**: Filtros múltiples y búsqueda de texto libre

### ?? **Análisis y Estadísticas**
- **Estadísticas por estado**: Planeando, confirmado, en progreso, completado, cancelado
- **Análisis por tipo**: Vacaciones, negocios, familiar, aventura, cultural, otro
- **Presupuesto total**: Suma de todos los presupuestos de viajes
- **Países más visitados**: Ranking de destinos frecuentes

### ?? **Filtrado Avanzado**
- **Por estado del viaje**: Filtra por estado específico
- **Por tipo de viaje**: Filtra por categoría de viaje
- **Por destino**: País y ciudad de destino
- **Por fechas**: Rango de fechas de viaje
- **Por calificación**: Viajes con calificación mínima
- **Por presupuesto**: Viajes hasta cierto presupuesto máximo
- **Búsqueda de texto**: En título, descripción, notas y actividades

### ??? **Estructura de Datos**
- **Contenedor Cosmos DB**: `TwinTravel`
- **Partition Key**: `TwinID`
- **Documento Type**: `travel`

## ?? API Endpoints

### **POST** `/api/twins/{twinId}/travels`
Crear un nuevo viaje

**Request Body:**
```json
{
  "titulo": "Viaje a París",
  "descripcion": "Viaje romántico a la ciudad de la luz",
  "paisDestino": "Francia",
  "ciudadDestino": "París",
  "fechaInicio": "2025-06-15T00:00:00Z",
  "fechaFin": "2025-06-22T00:00:00Z",
  "presupuesto": 2500.00,
  "moneda": "EUR",
  "tipoViaje": "vacaciones",
  "estado": "planeando",
  "transporte": "Avión",
  "alojamiento": "Hotel 4 estrellas",
  "compañeros": "Pareja",
  "actividades": "Torre Eiffel, Louvre, Crucero por el Sena",
  "notas": "Reservar con anticipación"
}
```

**Response:**
```json
{
  "success": true,
  "travel": {
    "id": "guid-generated",
    "twinID": "twin123",
    "titulo": "Viaje a París",
    "descripcion": "Viaje romántico a la ciudad de la luz",
    "paisDestino": "Francia",
    "ciudadDestino": "París",
    "fechaInicio": "2025-06-15T00:00:00Z",
    "fechaFin": "2025-06-22T00:00:00Z",
    "duracionDias": 8,
    "presupuesto": 2500.00,
    "moneda": "EUR",
    "tipoViaje": "vacaciones",
    "estado": "planeando",
    "transporte": "Avión",
    "alojamiento": "Hotel 4 estrellas",
    "compañeros": "Pareja",
    "actividades": "Torre Eiffel, Louvre, Crucero por el Sena",
    "notas": "Reservar con anticipación",
    "fechaCreacion": "2025-01-15T10:00:00Z",
    "fechaActualizacion": "2025-01-15T10:00:00Z"
  },
  "message": "Travel 'Viaje a París' created successfully"
}
```

### **GET** `/api/twins/{twinId}/travels`
Obtener todos los viajes de un Twin con filtros opcionales

**Query Parameters:**
- `estado`: `planeando`, `confirmado`, `en_progreso`, `completado`, `cancelado`
- `tipoViaje`: `vacaciones`, `negocios`, `familiar`, `aventura`, `cultural`, `otro`
- `paisDestino`: Filtro por país (búsqueda parcial)
- `ciudadDestino`: Filtro por ciudad (búsqueda parcial)
- `fechaDesde`: Fecha mínima de inicio (ISO 8601)
- `fechaHasta`: Fecha máxima de inicio (ISO 8601)
- `calificacionMin`: Calificación mínima (1-5)
- `presupuestoMax`: Presupuesto máximo
- `searchTerm`: Búsqueda en título, descripción, notas, actividades
- `page`: Número de página (default: 1)
- `pageSize`: Tamaño de página (default: 20, max: 100)
- `sortBy`: Campo de ordenamiento (`fechaInicio`, `fechaCreacion`, `titulo`, `presupuesto`, `calificacion`)
- `sortDirection`: Dirección (`asc`, `desc`)

**Ejemplo:**
```
GET /api/twins/twin123/travels?estado=completado&tipoViaje=vacaciones&page=1&pageSize=10&sortBy=fechaInicio&sortDirection=desc
```

**Response:**
```json
{
  "success": true,
  "travels": [
    {
      "id": "travel1",
      "titulo": "Viaje a París",
      "descripcion": "Viaje romántico...",
      "paisDestino": "Francia",
      "estado": "completado",
      "calificacion": 5
    }
  ],
  "stats": {
    "total": 15,
    "planeando": 3,
    "confirmado": 2,
    "enProgreso": 1,
    "completado": 8,
    "cancelado": 1,
    "byType": {
      "vacaciones": 10,
      "negocios": 3,
      "familiar": 2
    },
    "totalBudget": 15000.00,
    "topCountries": {
      "Francia": 3,
      "España": 2,
      "Italia": 2
    }
  },
  "twinId": "twin123",
  "totalTravels": 15,
  "message": "Found 15 travel records for Twin twin123"
}
```

### **GET** `/api/twins/{twinId}/travels/{travelId}`
Obtener un viaje específico por ID

**Response:**
```json
{
  "success": true,
  "travel": {
    "id": "travel1",
    "titulo": "Viaje a París",
    "descripcion": "Viaje romántico a la ciudad de la luz",
    "paisDestino": "Francia",
    "ciudadDestino": "París",
    "fechaInicio": "2025-06-15T00:00:00Z",
    "fechaFin": "2025-06-22T00:00:00Z",
    "duracionDias": 8,
    "presupuesto": 2500.00,
    "moneda": "EUR",
    "tipoViaje": "vacaciones",
    "estado": "completado",
    "calificacion": 5,
    "highlights": "Torre Eiffel al atardecer fue increíble"
  },
  "message": "Travel record retrieved successfully",
  "travelId": "travel1",
  "twinId": "twin123"
}
```

### **PUT** `/api/twins/{twinId}/travels/{travelId}`
Actualizar un viaje existente

**Request Body** (todos los campos son opcionales):
```json
{
  "estado": "completado",
  "calificacion": 5,
  "highlights": "Torre Eiffel al atardecer fue increíble",
  "notas": "Excelente viaje, definitivamente regresaré"
}
```

### **DELETE** `/api/twins/{twinId}/travels/{travelId}`
Eliminar un viaje

**Response:**
```json
{
  "success": true,
  "travel": {
    "id": "travel1",
    "titulo": "Viaje a París"
  },
  "message": "Travel record 'Viaje a París' deleted successfully",
  "travelId": "travel1",
  "twinId": "twin123"
}
```

## ?? Modelos de Datos

### **TravelData** - Modelo principal
```csharp
public class TravelData
{
    public string Id { get; set; }                    // GUID único
    public string Titulo { get; set; }                // Título del viaje (requerido)
    public string Descripcion { get; set; }           // Descripción (requerido)
    public string? PaisDestino { get; set; }          // País de destino
    public string? CiudadDestino { get; set; }        // Ciudad de destino
    public DateTime? FechaInicio { get; set; }        // Fecha de inicio
    public DateTime? FechaFin { get; set; }           // Fecha de fin
    public int? DuracionDias { get; set; }            // Duración calculada
    public decimal? Presupuesto { get; set; }         // Presupuesto
    public string? Moneda { get; set; }               // Moneda (default: USD)
    public TravelType TipoViaje { get; set; }         // Tipo de viaje
    public TravelStatus Estado { get; set; }          // Estado del viaje
    public string? Transporte { get; set; }           // Medio de transporte
    public string? Alojamiento { get; set; }          // Información de alojamiento
    public string? Compañeros { get; set; }           // Compañeros de viaje
    public string? Actividades { get; set; }          // Actividades planificadas/realizadas
    public string? Notas { get; set; }                // Notas adicionales
    public int? Calificacion { get; set; }            // Calificación 1-5 estrellas
    public string? Highlights { get; set; }           // Momentos destacados
    public DateTime FechaCreacion { get; set; }       // Timestamp de creación
    public DateTime FechaActualizacion { get; set; }  // Timestamp de actualización
    public string TwinID { get; set; }                // ID del Twin (partition key)
    public string DocumentType { get; set; } = "travel"; // Tipo de documento
}
```

### **TravelType** - Tipos de viaje
```csharp
public enum TravelType
{
    Vacaciones,    // Viajes de placer/descanso
    Negocios,      // Viajes de trabajo
    Familiar,      // Viajes familiares
    Aventura,      // Viajes de aventura/deportes
    Cultural,      // Viajes culturales/educativos
    Otro           // Otros tipos
}
```

### **TravelStatus** - Estados del viaje
```csharp
public enum TravelStatus
{
    Planeando,     // En fase de planificación
    Confirmado,    // Confirmado y reservado
    EnProgreso,    // Viaje en curso
    Completado,    // Viaje terminado
    Cancelado      // Viaje cancelado
}
```

## ?? Configuración

### **Cosmos DB Container**
- **Container Name**: `TwinTravel`
- **Partition Key**: `/TwinID`
- **Throughput**: Compartido con la base de datos

### **Variables de Entorno Requeridas**
```json
{
  "COSMOS_ACCOUNT_NAME": "tu-cosmos-account",
  "COSMOS_DATABASE_NAME": "TwinHumanDB", 
  "COSMOS_KEY": "tu-cosmos-key"
}
```

## ?? Ejemplos de Uso

### **Crear un viaje de negocios**
```bash
curl -X POST https://tu-function-app.azurewebsites.net/api/twins/twin123/travels \
  -H "Content-Type: application/json" \
  -d '{
    "titulo": "Conferencia Tech Madrid",
    "descripcion": "Asistir a conferencia de tecnología",
    "paisDestino": "España",
    "ciudadDestino": "Madrid",
    "fechaInicio": "2025-03-10T09:00:00Z",
    "fechaFin": "2025-03-12T18:00:00Z",
    "presupuesto": 1200.00,
    "moneda": "EUR",
    "tipoViaje": "negocios",
    "estado": "confirmado",
    "transporte": "Avión",
    "alojamiento": "Hotel cerca del centro de convenciones",
    "actividades": "Keynotes, workshops, networking"
  }'
```

### **Buscar viajes completados por país**
```bash
curl "https://tu-function-app.azurewebsites.net/api/twins/twin123/travels?estado=completado&paisDestino=Francia&sortBy=fechaInicio&sortDirection=desc"
```

### **Actualizar calificación de un viaje**
```bash
curl -X PUT https://tu-function-app.azurewebsites.net/api/twins/twin123/travels/travel123 \
  -H "Content-Type: application/json" \
  -d '{
    "estado": "completado",
    "calificacion": 5,
    "highlights": "La comida francesa superó todas las expectativas"
  }'
```

## ?? Análisis y Estadísticas

El TravelAgent proporciona estadísticas automáticas en cada consulta:

- **Por Estado**: Conteo de viajes en cada estado
- **Por Tipo**: Distribución de tipos de viaje
- **Presupuesto Total**: Suma de todos los presupuestos
- **Top Países**: Países más visitados con frecuencia

Estas estadísticas son útiles para:
- Analizar patrones de viaje
- Presupuestación y planning financiero
- Identificar destinos favoritos
- Tracking de objetivos de viaje

## ??? Seguridad y Validación

### **Validaciones Implementadas**
- ? **Título requerido**: Mínimo 2 caracteres
- ? **Descripción requerida**: Texto obligatorio
- ? **Twin ID válido**: Partition key requerido
- ? **Calificación válida**: Entre 1 y 5 estrellas
- ? **Fechas lógicas**: Fecha fin posterior a fecha inicio
- ? **URLs válidas**: Validación de formato si se proporciona

### **CORS Configurado**
- ? **Origins permitidos**: localhost:5173, localhost:3000
- ? **Métodos**: GET, POST, PUT, DELETE, OPTIONS
- ? **Headers**: Content-Type, Authorization, Accept

## ?? Integración con TwinFx

El TravelAgent se integra perfectamente con el ecosistema TwinFx:

- **TwinAgentClient**: Puede ser invocado desde conversaciones
- **CosmosDB**: Utiliza la misma infraestructura de datos
- **Logging**: Sistema de logging unificado
- **Configuración**: Usa las mismas variables de entorno

## ?? Roadmap Futuro

- [ ] **Integración con APIs de viajes**: Precios de vuelos, hoteles
- [ ] **Geolocalización**: Mapas y coordenadas
- [ ] **Fotos de viaje**: Integración con PhotosAgent
- [ ] **Itinerarios detallados**: Planificación día a día
- [ ] **Gastos por viaje**: Tracking de gastos detallado
- [ ] **Recomendaciones IA**: Sugerencias basadas en historial
- [ ] **Compartir viajes**: Funcionalidad social
- [ ] **Reportes avanzados**: Analytics e insights más profundos

---

**Desarrollado como parte del ecosistema TwinFx** ??  
*Gestión inteligente de viajes con IA integrada*