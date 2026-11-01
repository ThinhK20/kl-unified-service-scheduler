import { Alert } from '../../components/Alert';
import { Button } from '../../components/Button';
import { DateInput } from '../../components/DateInput';
import { Select } from '../../components/Select';
import { Spinner } from '../../components/Spinner';
import { formatDealershipOption, formatServiceOption, formatVehicleOption } from '../../lib/formatters';
import { BookingErrorBox } from './BookingErrorBox';
import { BookingSummary } from './BookingSummary';
import { TimeSlotPicker } from './TimeSlotPicker';
import type { BookingFormModel } from './useBookingForm';

export function BookingForm({ model }: { model: BookingFormModel }) {
  const { form, errors } = model;
  const { register } = form;

  if (model.vehiclesQuery.isPending || model.dealershipsQuery.isPending) {
    return <Spinner label="Loading booking options…" />;
  }
  if (model.vehiclesQuery.isError || model.dealershipsQuery.isError) {
    return (
      <Alert title="We could not load the booking options">
        <p className="alert__text">Please check that the service is running and try again.</p>
        <Button
          variant="secondary"
          onClick={() => {
            void model.vehiclesQuery.refetch();
            void model.dealershipsQuery.refetch();
          }}
        >
          Try again
        </Button>
      </Alert>
    );
  }

  const serviceHint = !model.hasDealership
    ? 'Choose a dealership first.'
    : model.serviceTypesQuery.isFetching
      ? 'Loading services…'
      : model.serviceTypesQuery.isError
        ? 'We could not load the services for this dealership.'
        : undefined;

  return (
    <form className="booking" onSubmit={model.onSubmit} noValidate aria-label="Book a service">
      <div className="booking__steps">
        <section className="card" aria-labelledby="step-vehicle">
          <h2 id="step-vehicle" className="card__title">
            1. Your vehicle
          </h2>
          <Select
            label="Vehicle"
            placeholder="Choose a vehicle"
            options={model.vehicles.map((v) => ({ value: String(v.id), label: formatVehicleOption(v) }))}
            error={errors.vehicleId?.message}
            {...register('vehicleId')}
          />
        </section>

        <section className="card" aria-labelledby="step-dealership">
          <h2 id="step-dealership" className="card__title">
            2. Dealership
          </h2>
          <Select
            label="Service centre"
            placeholder="Choose a dealership"
            options={model.dealerships.map((d) => ({ value: String(d.id), label: formatDealershipOption(d) }))}
            error={errors.dealershipId?.message}
            {...register('dealershipId', { onChange: model.onDealershipChange })}
          />
        </section>

        <section className="card" aria-labelledby="step-service">
          <h2 id="step-service" className="card__title">
            3. Service
          </h2>
          <Select
            label="Service type"
            placeholder="Choose a service"
            disabled={!model.hasDealership}
            options={model.serviceTypes.map((s) => ({ value: String(s.id), label: formatServiceOption(s) }))}
            hint={serviceHint}
            error={errors.serviceTypeId?.message}
            {...register('serviceTypeId', { onChange: model.onServiceOrDateChange })}
          />
        </section>

        <section className="card" aria-labelledby="step-time">
          <h2 id="step-time" className="card__title">
            4. Date and time
          </h2>
          <DateInput
            label="Date"
            min={model.minDate}
            error={errors.date?.message}
            {...register('date', { onChange: model.onServiceOrDateChange })}
          />
          <TimeSlotPicker
            state={model.slotsState}
            slots={model.slots}
            selectedKey={model.selectedSlotKey}
            onSelect={model.selectTime}
            onRetry={model.refetchSlots}
            error={errors.startTime?.message}
          />
        </section>
      </div>

      <aside className="card booking__aside" aria-labelledby="summary-title">
        <h2 id="summary-title" className="card__title">
          Your booking
        </h2>
        <BookingSummary {...model.summary} />
        <p className="booking__note">
          A technician qualified for this service and a free bay are assigned when you book.
        </p>
        {model.errorView && (
          <BookingErrorBox
            error={model.errorView}
            suggestions={model.suggestions}
            onPickSuggestion={model.selectTime}
            onRetry={model.retry}
            isSaving={model.isSaving}
          />
        )}
        <Button type="submit" fullWidth disabled={model.isSaving} aria-busy={model.isSaving || undefined}>
          {model.isSaving ? 'Booking…' : 'Book appointment'}
        </Button>
      </aside>
    </form>
  );
}
