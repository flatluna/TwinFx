# Cursos API Test

## Test POST Request

Here's a test request to create a course:

```bash
curl -X POST "https://localhost:7071/api/twins/test-twin-123/cursos" \
  -H "Content-Type: application/json" \
  -d '{
  "curso": {
    "nombreClase": "Curso de Manejo Básico Estándar",
    "instructor": "Equipo de Instructores Escuela Mexicana de Manejo",
    "plataforma": "Escuela Mexicana de Manejo",
    "categoria": "Conducción básica",
    "duracion": "5 horas prácticas y 6 teóricas",
    "requisitos": "Ninguno, adecuado para personas que ya conducen",
    "loQueAprendere": "Principios básicos de conducción y técnicas para manejar con seguridad",
    "precio": "$1,590 MXN",
    "recursos": "Autos asegurados, métodos especializados",
    "idioma": "Español",
    "fechaInicio": "Flexible, clases disponibles todo el año",
    "fechaFin": "Flexible según progreso",
    "objetivosdeAprendizaje": "Desarrollar habilidades de manejo seguro",
    "habilidadesCompetencias": "Manejo defensivo, control del vehículo, conocimiento de señalización",
    "prerequisitos": "Licencia de aprendiz o solicitud en trámite",
    "enlaces": {
      "enlaceClase": "https://www.escuelamexicanademanejo.mx/",
      "enlaceInstructor": "https://www.escuelamexicanademanejo.mx/instructor",
      "enlacePlataforma": "https://www.escuelamexicanademanejo.mx/",
      "enlaceCategoria": "https://www.escuelamexicanademanejo.mx/categoria"
    }
  },
  "metadatos": {
    "fechaSeleccion": "2025-09-23T21:45:00.000Z",
    "estadoCurso": "seleccionado",
    "origenBusqueda": "ia_search",
    "consultaOriginal": "cursos de manejo en México"
  }
}'
```

## Test UPDATE Request with AI Analysis

Here's a test request to update a course (which will automatically generate AI analysis in htmlDetails):

```bash
curl -X PUT "https://localhost:7071/api/twins/test-twin-123/cursos/{cursoId}" \
  -H "Content-Type: application/json" \
  -d '{
  "curso": {
    "nombreClase": "Curso de Manejo Básico Estándar - ACTUALIZADO",
    "instructor": "Equipo de Instructores Escuela Mexicana de Manejo",
    "plataforma": "Escuela Mexicana de Manejo",
    "categoria": "Conducción básica y avanzada",
    "duracion": "6 horas prácticas y 8 teóricas",
    "requisitos": "Licencia de aprendiz vigente",
    "loQueAprendere": "Principios básicos y avanzados de conducción, técnicas de manejo defensivo y seguridad vial",
    "precio": "$1,890 MXN",
    "recursos": "Autos asegurados, simuladores, métodos especializados",
    "idioma": "Español",
    "fechaInicio": "Disponible todo el año con horarios flexibles",
    "fechaFin": "Según progreso individual del estudiante",
    "objetivosdeAprendizaje": "Desarrollar habilidades de manejo seguro y defensivo, preparación para examen oficial",
    "habilidadesCompetencias": "Manejo defensivo avanzado, control total del vehículo, conocimiento completo de señalización, estacionamiento en paralelo",
    "prerequisitos": "Licencia de aprendiz o solicitud en trámite, edad mínima 18 años",
    "etiquetas": "manejo, conducción, seguridad vial, curso actualizado, premium",
    "NotasPersonales": "Curso mejorado con más horas prácticas y contenido actualizado. Incluye preparación para examen oficial.",
    "enlaces": {
      "enlaceClase": "https://www.escuelamexicanademanejo.mx/",
      "enlaceInstructor": "https://www.escuelamexicanademanejo.mx/instructor",
      "enlacePlataforma": "https://www.escuelamexicanademanejo.mx/",
      "enlaceCategoria": "https://www.escuelamexicanademanejo.mx/categoria"
    }
  },
  "metadatos": {
    "fechaSeleccion": "2025-09-23T21:45:00.000Z",
    "estadoCurso": "en_progreso",
    "origenBusqueda": "ia_search",
    "consultaOriginal": "cursos de manejo en México"
  }
}'
```

## API Endpoints Created

### POST /api/twins/{twinId}/cursos
Creates a new course

### GET /api/twins/{twinId}/cursos
Gets all courses for a twin

### GET /api/twins/{twinId}/cursos/{cursoId}
Gets a specific course by ID

### PUT /api/twins/{twinId}/cursos/{cursoId}
?? **Updates an existing course AND generates AI analysis in htmlDetails**
- Automatically calls `ProcessUpdateCursoAsync` from `CursosAgent`
- Generates comprehensive HTML analysis using AI
- Stores the result in `curso.htmlDetails` property
- Includes personalized recommendations based on tags and notes

### DELETE /api/twins/{twinId}/cursos/{cursoId}
Deletes a course

### PATCH /api/twins/{twinId}/cursos/{cursoId}/status
Updates course status

## Test Status Update

```bash
curl -X PATCH "https://localhost:7071/api/twins/test-twin-123/cursos/{cursoId}/status" \
  -H "Content-Type: application/json" \
  -d '{
    "estadoCurso": "en_progreso"
  }'
```

## Allowed Status Values
- seleccionado
- en_progreso
- completado  
- pausado
- cancelado

## ?? New AI Analysis Features

When you update a course using `PUT /api/twins/{twinId}/cursos/{cursoId}`, the system will:

1. **Execute AI Analysis**: Automatically calls `GenerateCreateCourseAnalysisAsync` method
2. **Store HTML Details**: Saves comprehensive AI-generated HTML analysis in `htmlDetails` property
3. **Personalized Insights**: Includes analysis of personal tags and notes
4. **Update-Specific Analysis**: Focuses on improvements and changes made to the course
5. **Fallback Handling**: If AI analysis fails, provides a basic HTML summary

The `htmlDetails` property will contain rich HTML content with:
- Executive summary of the course updates
- Detailed analysis with educational colors and icons
- Personalized recommendations based on tags and notes
- Action plans and study strategies
- Progress tracking suggestions

## Example Response Structure

After updating a course, the response will include:

```json
{
  "success": true,
  "twinId": "test-twin-123",
  "cursoId": "curso-123",
  "curso": {
    "nombreClase": "Course Name",
    // ... other course properties
    "htmlDetails": "<div style='background: linear-gradient...'>Rich HTML Analysis</div>"
  }
}