import { useState } from 'react';

export interface PreviewBannerProps {
  onReset: () => void;
}

/** Slim notice above the app: this is a preview with a simulated API. */
export function PreviewBanner({ onReset }: PreviewBannerProps) {
  const [message, setMessage] = useState('');
  return (
    <aside className="preview-banner" aria-label="Preview notice">
      <p className="preview-banner__text">
        <strong>Live preview</strong> · the API runs in your browser with demo data
      </p>
      <button
        type="button"
        className="preview-banner__button"
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
