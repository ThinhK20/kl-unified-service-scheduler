import { BookingConfirmation } from './BookingConfirmation';
import { BookingForm } from './BookingForm';
import { useBookingForm } from './useBookingForm';

export function BookingPage() {
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
