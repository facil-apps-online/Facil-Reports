# Facil Reports

API centralizada para generación de reportes y documentos personalizados.

## Arquitectura

```
┌─────────────────────┐     ┌─────────────────────┐     ┌─────────────────┐
│ React Frontend      │────▶│ Facil Reports       │────▶│ Supabase        │
│ (Glamtica, Nexu...) │     │ (ASP.NET Core)      │     │ (PostgreSQL)    │
└─────────────────────┘     └─────────────────────┘     └─────────────────┘
        │                           │
        │                           │
        ▼                           ▼
  Navegador del              reports.facil-apps.online
  usuario                    (Droplet DigitalOcean)
```

## Requisitos

- .NET 8 SDK (para desarrollo local)
- Docker y Docker Compose (para deploy)
- Nginx (en el servidor)
- Licencia de DevExpress

## Inicio Rápido (Desarrollo Local)

### 1. Clonar el repositorio

```bash
git clone https://github.com/facil-apps-online/Facil-Reports.git
cd FacilReports
```

### 2. Configurar variables de entorno

```bash
cp .env.example .env
# Editar .env con tus API keys
```

### 3. Ejecutar con Docker

```bash
docker compose up --build
```

El API estará disponible en `http://localhost:5000`

### 4. Verificar salud

```bash
curl http://localhost:5000/health
```

## Deploy en Producción

### 1. Preparar servidor DigitalOcean

```bash
# SSH al droplet
ssh root@YOUR_DROPLET_IP

# Clonar repositorio
git clone https://github.com/facil-apps-online/Facil-Reports.git /opt/FacilReports
cd /opt/FacilReports

# Ejecutar setup
chmod +x scripts/setup-server.sh
./scripts/setup-server.sh
```

### 2. Configurar API keys

```bash
cp .env.example .env
nano .env  # Agregar API keys reales
```

### 3. Desplegar

```bash
chmod +x scripts/deploy.sh
./scripts/deploy.sh
```

### 4. Configurar DNS

Crear registro A en DigitalOcean:
```
reports.facil-apps.online → IP_DEL_DROPLET
```

### 5. Configurar SSL

```bash
sudo certbot --nginx -d reports.facil-apps.online
```

## API Endpoints

### Health Check
```
GET /health
```

### API Keys
```
POST /api/apikey/generate   - Generar nueva API key
GET  /api/apikey/validate   - Validar API key
GET  /api/apikey/list       - Listar keys activas
DELETE /api/apikey          - Revocar key
```

### Plantillas
```
POST   /api/templates/save     - Guardar plantilla .repx
GET    /api/templates/{key}    - Obtener plantilla
GET    /api/templates/list     - Listar plantillas
DELETE /api/templates/{key}    - Eliminar plantilla
```

### Reportes
```
POST /api/reports/generate     - Generar PDF
```

## Autenticación

### API Key (para endpoints de reportes)
```bash
curl -X POST https://reports.facil-apps.online/api/reports/generate \
  -H "Content-Type: application/json" \
  -H "X-API-Key: glamtica_live_xxxxx" \
  -d '{"templateKey": "certificado-laboral", "data": {...}}'
```

### Admin Secret (para generar/revocar keys)
```bash
curl -X POST https://reports.facil-apps.online/api/apikey/generate \
  -H "Content-Type: application/json" \
  -H "X-Admin-Secret: tu_secret_aqui" \
  -d '{"platformSlug": "nexu"}'
```

## Uso en React

### Configurar variables de entorno

```env
# .env.local
VITE_REPORT_API_URL=https://reports.facil-apps.online
VITE_REPORT_API_KEY=nexu_live_xxxxx
```

### Usar hook para generar PDF

```tsx
import { useReportApi } from '@/hooks/useReportApi';
import { useReportingIntegration } from '@/hooks/useReportingIntegration';

function CertificatePage() {
  const { data: integration } = useReportingIntegration();
  const { generatePdf, loading } = useReportApi({
    apiUrl: integration?.apiUrl || '',
    apiKey: integration?.apiKey || '',
  });

  const handleGenerate = async () => {
    const result = await generatePdf({
      templateKey: 'certificado-laboral',
      data: {
        nombre_empleado: 'Juan Pérez',
        empresa: 'Mi Empresa',
        // ... más datos
      },
    });
    
    if (result instanceof Blob) {
      const url = URL.createObjectURL(result);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'certificado.pdf';
      a.click();
    }
  };

  return (
    <button onClick={handleGenerate} disabled={loading}>
      {loading ? 'Generando...' : 'Descargar PDF'}
    </button>
  );
}
```

### Subir plantilla .repx

```tsx
import { useReportApi } from '@/hooks/useReportApi';

function TemplateUpload() {
  const { saveTemplate, loading } = useReportApi({
    apiUrl: 'https://reports.facil-apps.online',
    apiKey: 'tu_api_key',
  });

  const handleUpload = async (file: File) => {
    const buffer = await file.arrayBuffer();
    const base64 = btoa(
      new Uint8Array(buffer).reduce((data, byte) => data + String.fromCharCode(byte), '')
    );

    await saveTemplate({
      templateKey: 'mi-plantilla',
      repxBase64: base64,
    });
  };

  return <input type="file" onChange={e => e.target.files?.[0] && handleUpload(e.target.files[0])} />;
}
```

## Estructura de Archivos

```
FacilReports/
├── FacilReports/               # Proyecto .NET
│   ├── Controllers/            # API Controllers
│   ├── Services/               # Lógica de negocio
│   ├── Middleware/              # Autenticación
│   ├── Models/                 # Modelos de datos
│   └── Reports/                # Plantillas .repx
├── react-components/           # Componentes React
├── nginx/                      # Configuración Nginx
├── scripts/                    # Scripts de deploy
├── Dockerfile
├── docker-compose.yml
└── .env.example
```

## Troubleshooting

### El API no responde

```bash
# Verificar contenedor
docker compose ps

# Ver logs
docker compose logs -f

# Reiniciar
docker compose restart
```

### Error de CORS

Verificar que el dominio del frontend esté en la lista de CORS en `appsettings.json`.

### Plantilla no encontrada

Verificar que el archivo `.repx` fue subido correctamente a Google Drive.

## Soporte

- Issues: https://github.com/facil-apps-online/Facil-Reports/issues
