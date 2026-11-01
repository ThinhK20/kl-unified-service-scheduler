// Entry for the self-contained live preview: the real app with an in-browser API.
import { QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createQueryClient } from '../lib/queryClient';
import '../styles.css';
import './preview.css';
import { installMockApi } from './mockApi';
import { PreviewRoot } from './PreviewRoot';

const mockApi = installMockApi();
const queryClient = createQueryClient();

const root = document.getElementById('root');
if (!root) throw new Error('Missing #root element');

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <PreviewRoot onResetData={mockApi.reset} backend={mockApi.backend} />
    </QueryClientProvider>
  </StrictMode>,
);
