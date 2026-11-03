import { BookingConfirmation } from './BookingConfirmation';
import { BookingForm } from './BookingForm';
import { useCustomer } from '../customers/customerContext';
import { useBookingForm } from './useBookingForm';

/** A new key per customer: switching customer starts a fresh form (and forgets the last booking). */
export function BookingPage() {
  const { customerId } = useCustomer();
  return <BookingPageContent key={customerId ?? 'none'} />;
}

function BookingPageContent() {
  const model = useBookingForm();

  return (
    <main className="page page--wide">
      <h1 className="page__title">Book a service</h1>
      <p className="page__lead">
        Pick your car, the service and a time. We assign a qualified technician and a free bay for you.
      </p>
      {model.appointment ? (
        <BookingConfirmation appointment={model.appointment} onBookAnother={model.startOver} />
      ) : (
        <BookingForm model={model} />
      )}
    </main>
  );
}
