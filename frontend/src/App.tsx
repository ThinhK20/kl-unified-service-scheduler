import { Navigate, Route, Routes } from 'react-router-dom';
import { AppHeader } from './components/AppHeader';
import { AppointmentsPage } from './features/appointments/AppointmentsPage';
import { BookingPage } from './features/booking/BookingPage';
import { CustomerProvider } from './features/customers/CustomerProvider';

export function App() {
  return (
    <CustomerProvider>
      <a href="#content" className="skip-link">
        Skip to content
      </a>
      <AppHeader />
      <div id="content" tabIndex={-1}>
        <Routes>
          <Route path="/" element={<BookingPage />} />
          <Route path="/appointments" element={<AppointmentsPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </div>
    </CustomerProvider>
  );
}
