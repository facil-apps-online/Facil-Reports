// ReportViewer.tsx
// Component for viewing/previewing reports
// Uses DevExpress Web Document Viewer

'use client';

import React from 'react';
import ReportViewer, {
  RequestOptions,
  Callbacks,
} from 'devexpress-reporting-react/dx-report-viewer';

// Styles
import "devextreme/dist/css/dx.light.css";
import "@devexpress/analytics-core/dist/css/dx-analytics.common.css";
import "devexpress-reporting/dist/css/dx-webdocumentviewer.css";

const API_URL = process.env.NEXT_PUBLIC_REPORT_API || 'http://localhost:5000';
const API_KEY = process.env.NEXT_PUBLIC_REPORT_API_KEY || '';

interface ReportViewerProps {
  /** Template name to display */
  templateName: string;
  /** Report parameters */
  parameters?: Record<string, any>;
  /** Height of the viewer container */
  height?: string | number;
  /** Width of the viewer container */
  width?: string | number;
  /** Called when report is loaded */
  onLoad?: () => void;
  /** Called on error */
  onError?: (error: Error) => void;
  /** Show toolbar (default: true) */
  showToolbar?: boolean;
}

export function ReportViewerComponent({
  templateName,
  parameters,
  height = '100%',
  width = '100%',
  onLoad,
  onError,
  showToolbar = true,
}: ReportViewerProps) {
  const handleCustomizeParameterPanel = ({ args }: { args: any }) => {
    // Customize parameter panel if needed
  };

  const handleError = ({ args }: { args: any }) => {
    console.error('[ReportViewer] Error:', args);
    onError?.(new Error(args?.error || 'Unknown error'));
  };

  return (
    <div style={{ height, width }}>
      <ReportViewer
        reportUrl={templateName}
      >
        <RequestOptions
          host={API_URL}
          invokeAction="DXXRDV"
          headers={{ 'X-API-Key': API_KEY }}
        />
        <Callbacks
          CustomizeParameterPanel={handleCustomizeParameterPanel}
        />
      </ReportViewer>
    </div>
  );
}

export default ReportViewerComponent;
