# Cursos API Test

## Test POST Request

Here's a test request to create a course:

```bash
curl -X POST "https://localhost:7071/api/twins/test-twin-123/cursos" \
  -H "Content-Type: application/json" \
  -d '{
  "curso": {
    "nombreClase": "Curso de Manejo B�sico Est�ndar",
    "instructor": "Equipo de Instructores Escuela Mexicana de Manejo",
    "plataforma": "Escuela Mexicana de Manejo",
    "categoria": "Conducci�n b�sica",
    "duracion": "5 horas pr�cticas y 6 te�ricas",
    "requisitos": "Ninguno, adecuado para personas que ya conducen",
    "loQueAprendere": "Principios b�sicos de conducci�n y t�cnicas para manejar con seguridad",
    "precio": "$1,590 MXN",
    "recursos": "Autos asegurados, m�todos especializados",
    "idioma": "Espa�ol",
    "fechaInicio": "Flexible, clases disponibles todo el a�o",
    "fechaFin": "Flexible seg�n progreso",
    "objetivosdeAprendizaje": "Desarrollar habilidades de manejo seguro",
    "habilidadesCompetencias": "Manejo defensivo, control del veh�culo, conocimiento de se�alizaci�n",
    "prerequisitos": "Licencia de aprendiz o solicitud en tr�mite",
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
    "consultaOriginal": "cursos de manejo en M�xico"
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
    "nombreClase": "Curso de Manejo B�sico Est�ndar - ACTUALIZADO",
    "instructor": "Equipo de Instructores Escuela Mexicana de Manejo",
    "plataforma": "Escuela Mexicana de Manejo",
    "categoria": "Conducci�n b�sica y avanzada",
    "duracion": "6 horas pr�cticas y 8 te�ricas",
    "requisitos": "Licencia de aprendiz vigente",
    "loQueAprendere": "Principios b�sicos y avanzados de conducci�n, t�cnicas de manejo defensivo y seguridad vial",
    "precio": "$1,890 MXN",
    "recursos": "Autos asegurados, simuladores, m�todos especializados",
    "idioma": "Espa�ol",
    "fechaInicio": "Disponible todo el a�o con horarios flexibles",
    "fechaFin": "Seg�n progreso individual del estudiante",
    "objetivosdeAprendizaje": "Desarrollar habilidades de manejo seguro y defensivo, preparaci�n para examen oficial",
    "habilidadesCompetencias": "Manejo defensivo avanzado, control total del veh�culo, conocimiento completo de se�alizaci�n, estacionamiento en paralelo",
    "prerequisitos": "Licencia de aprendiz o solicitud en tr�mite, edad m�nima 18 a�os",
    "etiquetas": "manejo, conducci�n, seguridad vial, curso actualizado, premium",
    "NotasPersonales": "Curso mejorado con m�s horas pr�cticas y contenido actualizado. Incluye preparaci�n para examen oficial.",
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
    "consultaOriginal": "cursos de manejo en M�xico"
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