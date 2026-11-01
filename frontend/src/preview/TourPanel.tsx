import type { TourStep } from './demoTour';

export type TourStatus = 'idle' | 'playing' | 'done' | 'failed';

export interface TourPanelProps {
  status: TourStatus;
  step: TourStep | null;
  error: string;
  onStop: () => void;
  onReplay: () => void;
  onClose: () => void;
}

/** Floating caption for the guided tour: what is happening now, progress, and Stop / Replay. */
export function TourPanel({ status, step, error, onStop, onReplay, onClose }: TourPanelProps) {
  if (status === 'idle') return null;
  const progress = step ? Math.round((step.index / step.total) * 100) : 0;

  return (
    <section className="tour-panel" aria-label="Guided demo" aria-live="polite">
      {status === 'failed' ? (
        <>
          <p className="tour-panel__eyebrow">Demo stopped</p>
          <p className="tour-panel__title">Something did not go as planned</p>
          <p className="tour-panel__detail">{error} Reset the data and try again.</p>
        </>
      ) : (
        <>
          <p className="tour-panel__eyebrow">{step ? `Step ${step.index} of ${step.total}` : 'Starting…'}</p>
          <p className="tour-panel__title">{step?.title ?? 'Getting ready'}</p>
          {step && <p className="tour-panel__detail">{step.detail}</p>}
          <div className="tour-panel__bar" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress}>
            <span style={{ width: `${progress}%` }} />
          </div>
        </>
      )}
      <div className="tour-panel__actions">
        {status === 'playing' ? (
          <button type="button" className="tour-panel__button" onClick={onStop}>
            Stop
          </button>
        ) : (
          <>
            <button type="button" className="tour-panel__button tour-panel__button--primary" onClick={onReplay}>
              Replay
            </button>
            <button type="button" className="tour-panel__button" onClick={onClose}>
              Close
            </button>
          </>
        )}
      </div>
    </section>
  );
}
