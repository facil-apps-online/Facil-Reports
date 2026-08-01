// useReport.ts
// Hook for generating and downloading reports

'use client';

import { useState, useCallback } from 'react';

const API_URL = process.env.NEXT_PUBLIC_REPORT_API || 'http://localhost:5000';
const API_KEY = process.env.NEXT_PUBLIC_REPORT_API_KEY || '';

interface UseReportOptions {
  templateName: string;
  parameters?: Record<string, any>;
}

interface UseReportReturn {
  generate: (options?: UseReportOptions) => Promise<Blob>;
  download: (options?: UseReportOptions, filename?: string) => Promise<void>;
  loading: boolean;
  error: Error | null;
}

export function useReport(): UseReportReturn {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const generate = useCallback(async (options?: UseReportOptions): Promise<Blob> => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`${API_URL}/api/report/generate`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-API-Key': API_KEY,
        },
        body: JSON.stringify({
          templateName: options?.templateName,
          parameters: options?.parameters || {},
        }),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || `HTTP error ${response.status}`);
      }

      return await response.blob();
    } catch (err) {
      const error = err instanceof Error ? err : new Error('Unknown error');
      setError(error);
      throw error;
    } finally {
      setLoading(false);
    }
  }, []);

  const download = useCallback(async (
    options?: UseReportOptions,
    filename?: string
  ) => {
    const blob = await generate(options);
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename || `${options?.templateName || 'report'}.pdf`;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
  }, [generate]);

  return { generate, download, loading, error };
}

// Hook for listing templates
interface UseTemplatesReturn {
  templates: Array<{ name: string; fileName: string; modified: string }>;
  loading: boolean;
  error: Error | null;
  refresh: () => Promise<void>;
}

export function useTemplates(): UseTemplatesReturn {
  const [templates, setTemplates] = useState<Array<{ name: string; fileName: string; modified: string }>>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const fetchTemplates = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`${API_URL}/api/template`, {
        headers: {
          'X-API-Key': API_KEY,
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error ${response.status}`);
      }

      const data = await response.json();
      setTemplates(data.templates || []);
    } catch (err) {
      const error = err instanceof Error ? err : new Error('Unknown error');
      setError(error);
    } finally {
      setLoading(false);
    }
  }, []);

  return {
    templates,
    loading,
    error,
    refresh: fetchTemplates,
  };
}
