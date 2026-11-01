import type { ReactNode } from 'react';

export interface AlertProps {
  title: string;
  tone?: 'error' | 'neutral';
  children?: ReactNode;
}

/** An error / problem box. Uses role="alert" so screen readers announce it. */
export function Alert({ title, tone = 'error', children }: AlertProps) {
  return (
    <div role="alert" className={`alert alert--${tone}`}>
      <p className="alert__title">{title}</p>
      {children}
    </div>
  );
}
