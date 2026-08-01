'use client';

import { useState } from 'react';

const API_URL = process.env.NEXT_PUBLIC_REPORT_API || 'http://localhost:5000';
const API_KEY = process.env.NEXT_PUBLIC_REPORT_API_KEY || '';

interface SaveTemplateOptions {
  templateKey: string;
  repxBase64: string;
  description?: string;
}

export function useTemplates() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const save = async (options: SaveTemplateOptions) => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`${API_URL}/api/templates/save`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-API-Key': API_KEY,
        },
        body: JSON.stringify(options),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || `HTTP error ${response.status}`);
      }

      return await response.json();
    } catch (err) {
      const error = err instanceof Error ? err : new Error('Unknown error');
      setError(error);
      throw error;
    } finally {
      setLoading(false);
    }
  };

  return { save, loading, error };
}
