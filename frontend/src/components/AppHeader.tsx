import { NavLink } from 'react-router-dom';
import { CustomerSwitcher } from '../features/customers/CustomerSwitcher';

export function AppHeader() {
  const navClass = ({ isActive }: { isActive: boolean }) => `nav__link${isActive ? ' nav__link--active' : ''}`;
  return (
    <header className="header">
      <div className="header__inner">
        <div className="header__brand">
          <svg
            width="24"
            height="24"
            viewBox="0 0 24 24"
            fill="none"
            stroke="#F59E5B"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <circle cx="12" cy="12" r="9" />
            <path d="M12 7v5l3 2" />
          </svg>
          <span className="header__title">Service Scheduler</span>
        </div>
        <nav aria-label="Main" className="nav">
          <NavLink to="/" end className={navClass}>
            Book a service
          </NavLink>
          <NavLink to="/appointments" className={navClass}>
            My appointments
          </NavLink>
        </nav>
        <CustomerSwitcher />
      </div>
    </header>
  );
}
