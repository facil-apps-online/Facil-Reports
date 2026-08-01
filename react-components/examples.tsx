// Example usage of Report Designer and Viewer components
// Copy these files to your React/Next.js project

// ============================================================
// FILE: app/documentos/disenador/page.tsx
// Page for designing/editing report templates
// ============================================================

'use client';

import { useState } from 'react';
import { ReportDesignerComponent } from '@/components/ReportDesigner';

export default function DisenadorPage() {
  const [templateName, setTemplateName] = useState<string>('');

  return (
    <div className="h-screen flex flex-col">
      <div className="p-4 border-b">
        <h1 className="text-2xl font-bold">Diseñador de Documentos</h1>
        <div className="mt-2 flex gap-4">
          <input
            type="text"
            placeholder="Nombre de la plantilla (ej: nomina)"
            value={templateName}
            onChange={(e) => setTemplateName(e.target.value)}
            className="px-3 py-2 border rounded"
          />
          <button
            onClick={() => setTemplateName('')}
            className="px-4 py-2 bg-gray-200 rounded"
          >
            Nuevo
          </button>
        </div>
      </div>

      <div className="flex-1">
        <ReportDesignerComponent
          templateName={templateName}
          onSave={(name) => {
            alert(`Plantilla "${name}" guardada exitosamente`);
          }}
        />
      </div>
    </div>
  );
}

// ============================================================
// FILE: app/documentos/ver/page.tsx
// Page for viewing/previewing reports
// ============================================================

'use client';

import { useState } from 'react';
import { ReportViewerComponent } from '@/components/ReportViewer';

export default function VerReportePage() {
  const [templateName, setTemplateName] = useState('nomina');
  const [empleadoId, setEmpleadoId] = useState('123');

  return (
    <div className="h-screen flex flex-col">
      <div className="p-4 border-b">
        <h1 className="text-2xl font-bold">Ver Reporte</h1>
        <div className="mt-2 flex gap-4">
          <select
            value={templateName}
            onChange={(e) => setTemplateName(e.target.value)}
            className="px-3 py-2 border rounded"
          >
            <option value="nomina">Nómina</option>
            <option value="comisiones">Comisiones</option>
            <option value="certificado">Certificado</option>
          </select>

          <input
            type="text"
            placeholder="ID Empleado"
            value={empleadoId}
            onChange={(e) => setEmpleadoId(e.target.value)}
            className="px-3 py-2 border rounded w-32"
          />
        </div>
      </div>

      <div className="flex-1">
        <ReportViewerComponent
          templateName={templateName}
          parameters={{ empleado_id: parseInt(empleadoId) }}
        />
      </div>
    </div>
  );
}

// ============================================================
// FILE: app/documentos/generar/page.tsx
// Page for generating and downloading reports
// ============================================================

'use client';

import { useState } from 'react';
import { useReport } from '@/hooks/useReport';

export default function GenerarPage() {
  const { generate, download, loading, error } = useReport();
  const [templateName, setTemplateName] = useState('nomina');
  const [empleadoId, setEmpleadoId] = useState('123');
  const [periodo, setPeriodo] = useState('2026-01');

  const handleGenerate = async () => {
    try {
      await download(
        {
          templateName,
          parameters: {
            empleado_id: parseInt(empleadoId),
            periodo,
          },
        },
        `${templateName}-${empleadoId}-${periodo}.pdf`
      );
    } catch (err) {
      console.error('Error generating report:', err);
    }
  };

  return (
    <div className="p-6">
      <h1 className="text-2xl font-bold mb-6">Generar Reporte</h1>

      <div className="max-w-md space-y-4">
        <div>
          <label className="block text-sm font-medium mb-1">
            Tipo de Reporte
          </label>
          <select
            value={templateName}
            onChange={(e) => setTemplateName(e.target.value)}
            className="w-full px-3 py-2 border rounded"
          >
            <option value="nomina">Nómina</option>
            <option value="comisiones">Comisiones</option>
            <option value="certificado">Certificado Laboral</option>
            <option value="desprendible">Desprendible de Pago</option>
          </select>
        </div>

        <div>
          <label className="block text-sm font-medium mb-1">
            ID Empleado
          </label>
          <input
            type="text"
            value={empleadoId}
            onChange={(e) => setEmpleadoId(e.target.value)}
            className="w-full px-3 py-2 border rounded"
          />
        </div>

        <div>
          <label className="block text-sm font-medium mb-1">
            Período
          </label>
          <input
            type="text"
            value={periodo}
            onChange={(e) => setPeriodo(e.target.value)}
            placeholder="YYYY-MM"
            className="w-full px-3 py-2 border rounded"
          />
        </div>

        <button
          onClick={handleGenerate}
          disabled={loading}
          className="w-full px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:bg-gray-400"
        >
          {loading ? 'Generando...' : 'Descargar PDF'}
        </button>

        {error && (
          <div className="p-3 bg-red-100 text-red-700 rounded">
            {error.message}
          </div>
        )}
      </div>
    </div>
  );
}
