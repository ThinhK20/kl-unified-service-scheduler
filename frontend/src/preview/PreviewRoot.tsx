import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { App } from '../App';
import { clearStoredCustomerId } from '../features/customers/customerStorage';
import { runTour, TourStopped, type TourStep } from './demoTour';
import type { MockSchedulerBackend } from './mockBackend';
import { PreviewBanner } from './PreviewBanner';
import { TourPanel, type TourStatus } from './TourPanel';

export interface PreviewRootProps {
  onResetData: () => void;
  backend: MockSchedulerBackend;
  /** Start the guided tour as soon as the page opens. */
  autoPlay?: boolean;
}

/**
 * The real <App /> under a MemoryRouter (the hosting page cannot use path or hash routing),
 * with the preview banner above it and the guided-tour panel.
 */
export function PreviewRoot({ onResetData, backend, autoPlay = false }: PreviewRootProps) {
  const queryClient = useQueryClient();
  // A new key remounts the app, so forms and confirmation cards start fresh after a reset.
  const [generation, setGeneration] = useState(0);
  const [status, setStatus] = useState<TourStatus>('idle');
  const [step, setStep] = useState<TourStep | null>(null);
  const [error, setError] = useState('');
  const abortRef = useRef<AbortController | null>(null);

  const reset = useCallback(() => {
    onResetData();
    // Back to the first customer (An Nguyen) too; the remount below reads the selection again.
    clearStoredCustomerId();
    queryClient.clear();
    setGeneration((g) => g + 1);
  }, [onResetData, queryClient]);

  const play = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setError('');
    setStep(null);
    setStatus('playing');
    runTour({ backend, resetApp: reset, onStep: setStep, signal: controller.signal })
      .then(() => setStatus('done'))
      .catch((e: unknown) => {
        if (e instanceof TourStopped) {
          setStatus('idle');
          return;
        }
        setError(e instanceof Error ? e.message : 'The tour stopped unexpectedly.');
        setStatus('failed');
      });
  }, [backend, reset]);

  const stop = useCallback(() => abortRef.current?.abort(), []);

  const autoPlayed = useRef(false);
  useEffect(() => {
    if (autoPlay && !autoPlayed.current) {
      autoPlayed.current = true;
      play();
    }
  }, [autoPlay, play]);
  useEffect(() => () => abortRef.current?.abort(), []);

  const playing = status === 'playing';

  return (
    <>
      <PreviewBanner onReset={reset} onPlay={play} playing={playing} />
      <MemoryRouter key={generation}>
        <App />
      </MemoryRouter>
      {/* Not `inert`: HTMLElement.click() does nothing on inert elements. A shield keeps stray clicks out instead. */}
      {playing && <div className="tour-shield" aria-hidden="true" />}
      <TourPanel status={status} step={step} error={error} onStop={stop} onReplay={play} onClose={() => setStatus('idle')} />
    </>
  );
}
