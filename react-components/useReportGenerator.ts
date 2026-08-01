'use client';

import { useState } from 'react';

const API_URL = process.env.NEXT_PUBLIC_REPORT_API || 'http://localhost:5000';
const API_KEY = process.env.NEXT_PUBLIC_REPORT_API_KEY || '';

interface GenerateOptions {
  templateKey: string;
  data: Record<string, any>;
  asBase64?: boolean;
}

interface UseReportGeneratorReturn {
  generate: (options: GenerateOptions) => Promise<Blob | { pdfBase64: string }>;
  download: (options: GenerateOptions, filename?: string) => Promise<void>;
  loading: boolean;
  error: Error | null;
}

export function useReportGenerator(): UseReportGeneratorReturn {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const generate = async (options: GenerateOptions): Promise<Blob | { pdfBase64: string }> => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`${API_URL}/api/reports/generate`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-API-Key': API_KEY,
        },
        body: JSON.stringify({
          templateKey: options.templateKey,
          data: options.data,
          asBase64: options.asBase64 || false,
        }),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || `HTTP error ${response.status}`);
      }

      if (options.asBase64) {
        return await response.json();
      }

      return await response.blob();
    } catch (err) {
      const error = err instanceof Error ? err : new Error('Unknown error');
      setError(error);
      throw error;
    } finally {
      setLoading(false);
    }
  };

  const download = async (options: GenerateOptions, filename?: string) => {
    const result = await generate(options);

    if ('pdfBase64' in result) {
      // Download from base64
      const byteCharacters = atob(result.pdfBase64);
      const byteNumbers = new Array(byteCharacters.length);
      for (let i = 0; i < byteCharacters.length; i++) {
        byteNumbers[i] = byteCharacters.charCodeAt(i);
      }
      const byteArray = new Uint8Array(byteNumbers);
      const blob = new Blob([byteArray], { type: 'application/pdf' });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename || `${options.templateKey}.pdf`;
      a.click();
      window.URL.revokeObjectURL(url);
    } else {
      // Download from blob
      const url = window.URL.createObjectURL(result);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename || `${options.templateKey}.pdf`;
      a.click();
      window.URL.revokeObjectURL(url);
    }
  };

  return { generate, download, loading, error };
}
