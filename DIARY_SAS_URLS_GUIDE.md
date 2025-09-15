# ?? Gu�a de URLs SAS Autom�ticas para Archivos del Diario

## ?? **Nueva Funcionalidad: Generaci�n Autom�tica de URLs SAS**

El sistema ahora genera autom�ticamente **URLs SAS (Shared Access Signatures)** para los archivos almacenados en el campo `pathFile` de las entradas del diario, permitiendo acceso directo y seguro a los archivos desde el frontend.

## ?? **Cambio Implementado**

### ? **Nuevo Campo SasUrl**
```csharp
public class DiaryEntry 
{
    // ... otros campos ...
    
    /// <summary>
    /// Ruta del archivo adjunto (recibo, imagen, documento, etc.)
    /// </summary>
    [JsonPropertyName("pathFile")]
    [JsonProperty("pathFile")]
    public string PathFile { get; set; } = string.Empty;
    
    /// <summary>
    /// URL SAS para acceder al archivo (se genera din�micamente)
    /// </summary>
    [JsonPropertyName("sasUrl")]
    [JsonProperty("sasUrl")]
    public string SasUrl { get; set; } = string.Empty;
}
```

### ? **Generaci�n Autom�tica en Operaciones GET**
- **`GetDiaryEntries`**: Genera SAS URLs para todos los archivos de las entradas
- **`GetDiaryEntryById`**: Genera SAS URL para el archivo espec�fico de la entrada

## ?? **C�mo Funciona**

### ?? **1. Proceso Autom�tico**
```csharp
// En GetDiaryEntries y GetDiaryEntryById
foreach (var entry in entries)
{
    if (!string.IsNullOrEmpty(entry.PathFile))
    {
        try
        {
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(
                entry.PathFile, 
                TimeSpan.FromHours(24) // URL v�lida por 24 horas
            );
            entry.SasUrl = sasUrl ?? string.Empty;
        }
        catch (Exception ex)
        {
            // Si falla, SasUrl permanece vac�o
            entry.SasUrl = string.Empty;
        }
    }
}
```

### ?? **2. Caracter�sticas de las URLs SAS**
- **? Duraci�n**: 24 horas de validez
- **?? Seguridad**: Acceso temporal y controlado
- **?? Directo**: URL que funciona directamente en navegadores
- **?? No almacenado**: Se genera din�micamente, no se guarda en BD

## ?? **Respuestas de la API**

### ? **GET /api/twins/{twinId}/diary - Con Archivo**
```json
{
  "success": true,
  "entries": [
    {
      "id": "diary-entry-123",
      "twinId": "twin456",
      "titulo": "Almuerzo de trabajo",
      "descripcion": "Reuni�n con el equipo",
      "fecha": "2025-01-15T12:00:00Z",
      "tipoActividad": "trabajo",
      "pathFile": "diary/diary-entry-123/file_20250115_120000.jpg",
      "sasUrl": "https://datalake.../diary/diary-entry-123/file_20250115_120000.jpg?sv=2022-11-02&ss=bfqt&srt=co&sp=r&se=2025-01-16T12:00:00Z&st=2025-01-15T12:00:00Z&spr=https&sig=SIGNATURE_HERE",
      "fechaCreacion": "2025-01-15T12:00:00Z",
      "fechaModificacion": "2025-01-15T12:00:00Z"
    }
  ],
  "totalEntries": 1,
  "twinId": "twin456"
}
```

### ? **GET /api/twins/{twinId}/diary/{entryId} - Con Archivo**
```json
{
  "success": true,
  "entry": {
    "id": "diary-entry-123",
    "twinId": "twin456",
    "titulo": "Compra en supermercado",
    "descripcion": "Compras semanales",
    "tipoActividad": "compra",
    "gastoTotal": 150.75,
    "pathFile": "diary/diary-entry-123/recibo_compra.pdf",
    "sasUrl": "https://datalake.../diary/diary-entry-123/recibo_compra.pdf?sv=2022-11-02&ss=bfqt&srt=co&sp=r&se=2025-01-16T14:30:00Z&st=2025-01-15T14:30:00Z&spr=https&sig=ANOTHER_SIGNATURE",
    "fechaCreacion": "2025-01-15T14:30:00Z"
  },
  "twinId": "twin456"
}
```

### ?? **Sin Archivo**
```json
{
  "success": true,
  "entry": {
    "id": "diary-entry-124",
    "titulo": "Reflexi�n del d�a",
    "descripcion": "D�a de meditaci�n",
    "tipoActividad": "bienestar",
    "pathFile": "",
    "sasUrl": "",
    "fechaCreacion": "2025-01-15T20:00:00Z"
  }
}
```

## ?? **Uso en Frontend**

### ?? **React/JavaScript - Mostrar Archivo**
```jsx
const DiaryEntryCard = ({ entry }) => {
  const hasFile = entry.pathFile && entry.sasUrl;
  
  return (
    <div className="diary-entry-card">
      <h3>{entry.titulo}</h3>
      <p>{entry.descripcion}</p>
      
      {hasFile && (
        <div className="file-section">
          <h4>?? Archivo adjunto:</h4>
          
          {/* Para im�genes */}
          {isImageFile(entry.pathFile) && (
            <img 
              src={entry.sasUrl} 
              alt="Archivo adjunto"
              className="attached-image"
              style={{ maxWidth: '300px', height: 'auto' }}
            />
          )}
          
          {/* Para PDFs y otros archivos */}
          {!isImageFile(entry.pathFile) && (
            <a 
              href={entry.sasUrl} 
              target="_blank" 
              rel="noopener noreferrer"
              className="file-download-link"
            >
              ?? Ver archivo: {getFileName(entry.pathFile)}
            </a>
          )}
          
          {/* Bot�n de descarga */}
          <button onClick={() => downloadFile(entry.sasUrl, entry.pathFile)}>
            ?? Descargar
          </button>
        </div>
      )}
    </div>
  );
};

// Funciones helper
const isImageFile = (filePath) => {
  const imageExtensions = ['.jpg', '.jpeg', '.png', '.gif', '.webp'];
  return imageExtensions.some(ext => 
    filePath.toLowerCase().endsWith(ext)
  );
};

const getFileName = (filePath) => {
  return filePath.split('/').pop() || 'archivo';
};

const downloadFile = async (sasUrl, fileName) => {
  try {
    const response = await fetch(sasUrl);
    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    
    const a = document.createElement('a');
    a.href = url;
    a.download = getFileName(fileName);
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    window.URL.revokeObjectURL(url);
  } catch (error) {
    console.error('Error downloading file:', error);
  }
};
```

### ??? **Galer�a de Im�genes del Diario**
```jsx
const DiaryImageGallery = ({ entries }) => {
  const entriesWithImages = entries.filter(entry => 
    entry.pathFile && 
    entry.sasUrl && 
    isImageFile(entry.pathFile)
  );

  return (
    <div className="diary-gallery">
      <h2>?? Galer�a del Diario ({entriesWithImages.length} im�genes)</h2>
      
      <div className="image-grid">
        {entriesWithImages.map(entry => (
          <div key={entry.id} className="gallery-item">
            <img 
              src={entry.sasUrl}
              alt={entry.titulo}
              className="gallery-image"
              onClick={() => openImageModal(entry)}
            />
            <div className="image-info">
              <h4>{entry.titulo}</h4>
              <p>{new Date(entry.fecha).toLocaleDateString()}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
```

### ?? **Dashboard con Estad�sticas de Archivos**
```jsx
const DiaryFileStats = ({ entries }) => {
  const entriesWithFiles = entries.filter(entry => entry.pathFile);
  const imageCount = entriesWithFiles.filter(entry => isImageFile(entry.pathFile)).length;
  const pdfCount = entriesWithFiles.filter(entry => entry.pathFile.toLowerCase().endsWith('.pdf')).length;
  const otherCount = entriesWithFiles.length - imageCount - pdfCount;

  return (
    <div className="file-stats">
      <h3>?? Archivos del Diario</h3>
      
      <div className="stats-grid">
        <div className="stat-card">
          <h4>?? Im�genes</h4>
          <span className="stat-number">{imageCount}</span>
        </div>
        
        <div className="stat-card">
          <h4>?? PDFs</h4>
          <span className="stat-number">{pdfCount}</span>
        </div>
        
        <div className="stat-card">
          <h4>?? Otros</h4>
          <span className="stat-number">{otherCount}</span>
        </div>
        
        <div className="stat-card">
          <h4>?? Total</h4>
          <span className="stat-number">{entriesWithFiles.length}</span>
        </div>
      </div>
    </div>
  );
};
```

## ?? **Seguridad y Consideraciones**

### ? **1. Duraci�n de URLs**
- **24 horas**: Tiempo suficiente para uso normal
- **Renovaci�n autom�tica**: En cada consulta API se regeneran
- **No permanentes**: Las URLs expiran por seguridad

### ??? **2. Control de Acceso**
- **Solo lectura**: Las URLs SAS son de solo lectura
- **Twin espec�fico**: Solo acceso a archivos del Twin propietario
- **Temporal**: Acceso limitado en tiempo

### ?? **3. Manejo de Errores**
```javascript
const handleFileAccess = async (entry) => {
  if (!entry.sasUrl) {
    console.warn('No hay URL SAS disponible para este archivo');
    return;
  }
  
  try {
    // Verificar si la URL a�n es v�lida
    const response = await fetch(entry.sasUrl, { method: 'HEAD' });
    
    if (!response.ok) {
      console.warn('La URL SAS ha expirado, refrescando entrada...');
      // Refrescar la entrada del diario para obtener nueva URL SAS
      await refreshDiaryEntry(entry.id);
    }
  } catch (error) {
    console.error('Error accediendo al archivo:', error);
  }
};
```

## ?? **Beneficios Implementados**

### ? **Para Desarrolladores**
- **?? Autom�tico**: No requiere l�gica adicional en el frontend
- **?? Seguro**: URLs temporales y controladas
- **?? Compatible**: Funciona en cualquier navegador/app
- **? R�pido**: Acceso directo a los archivos

### ? **Para Usuarios**
- **??? Vista previa**: Im�genes se pueden mostrar directamente
- **?? Descarga**: PDFs y documentos accesibles con un clic
- **?? Privado**: Solo el propietario puede acceder
- **? Actualizado**: URLs siempre v�lidas al consultar

### ? **Para el Sistema**
- **?? Eficiente**: No almacena URLs innecesarias en BD
- **?? Din�mico**: Se genera cuando se necesita
- **?? Trazable**: Logs detallados de generaci�n de URLs
- **??? Robusto**: Manejo de errores gracioso

---

**�Ahora todas las entradas del diario con archivos incluyen autom�ticamente URLs SAS listas para usar! ??????**