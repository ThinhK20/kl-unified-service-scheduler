import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { App } from '../App';
import { PreviewBanner } from './PreviewBanner';

/**
 * The real <App /> under a MemoryRouter (the hosting page cannot use path or hash routing),
 * with the preview banner above it.
 */
export function PreviewRoot({ onResetData }: { onResetData: () => void }) {
  const queryClient = useQueryClient();
  // A new key remounts the app, so forms and confirmation cards start fresh after a reset.
  const [generation, setGeneration] = useState(0);
  const reset = () => {
    onResetData();
    queryClient.clear();
    setGeneration((g) => g + 1);
  };
  return (
    <>
      <PreviewBanner onReset={reset} />
      <MemoryRouter key={generation}>
        <App />
      </MemoryRouter>
    </>
  );
}
