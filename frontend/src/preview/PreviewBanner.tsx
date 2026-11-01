import { useState } from 'react';

export interface PreviewBannerProps {
  onReset: () => void;
  onPlay: () => void;
  playing: boolean;
}

/** Slim notice above the app: this is a preview with a simulated API. Starts the guided demo. */
export function PreviewBanner({ onReset, onPlay, playing }: PreviewBannerProps) {
  const [message, setMessage] = useState('');
  return (
    <aside className="preview-banner" aria-label="Preview notice">
      <p className="preview-banner__text">
        <strong>Live preview</strong> · the API runs in your browser with demo data
      </p>
      <button
        type="button"
        className="preview-banner__button preview-banner__button--play"
        onClick={() => {
          setMessage('');
          onPlay();
        }}
        disabled={playing}
      >
        <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true">
          <path d="M3 1.5v9l7.5-4.5z" fill="currentColor" />
        </svg>
        {playing ? 'Demo running…' : 'Play the demo'}
      </button>
      <button
        type="button"
        className="preview-banner__button"
        disabled={playing}
        onClick={() => {
          onReset();
          setMessage('Demo data was reset.');
        }}
      >
        Reset demo data
      </button>
      <span className="preview-banner__status" role="status">
        {message}
      </span>
    </aside>
  );
}
