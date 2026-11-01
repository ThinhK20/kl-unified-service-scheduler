import { forwardRef, type ButtonHTMLAttributes } from 'react';

type Variant = 'primary' | 'secondary' | 'accent-outline';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  fullWidth?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'primary', fullWidth = false, type = 'button', className, ...rest },
  ref,
) {
  const classes = ['btn', `btn--${variant}`, fullWidth ? 'btn--full' : '', className ?? ''].filter(Boolean).join(' ');
  return <button ref={ref} type={type} className={classes} {...rest} />;
});
