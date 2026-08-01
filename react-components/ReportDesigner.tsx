'use client';

import React from 'react';
import ReportDesigner, {
  RequestOptions,
  Callbacks,
} from 'devexpress-reporting-react/dx-report-designer';

import "devextreme/dist/css/dx.light.css";
import "@devexpress/analytics-core/dist/css/dx-analytics.common.css";
import "@devexpress/analytics-core/dist/css/dx-analytics.light.css";
import "devexpress-reporting/dist/css/dx-webdocumentviewer.css";

const API_URL = process.env.NEXT_PUBLIC_REPORT_API || 'http://localhost:5000';
const API_KEY = process.env.NEXT_PUBLIC_REPORT_API_KEY || '';

interface Props {
  templateKey?: string;
  onSave?: (templateKey: string) => void;
}

export function ReportDesigner({ templateKey, onSave }: Props) {
  return (
    <div style={{ height: '100%', width: '100%' }}>
      <ReportDesigner reportUrl={templateKey || 'new-report'}>
        <RequestOptions
          host={API_URL}
          invokeAction="DXXRD"
          headers={{ 'X-API-Key': API_KEY }}
        />
        <Callbacks
          ReportSaved={() => onSave?.(templateKey || 'new-report')}
        />
      </ReportDesigner>
    </div>
  );
}
