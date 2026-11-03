import { useState } from 'react';
import { Alert } from '../../components/Alert';
import { Button } from '../../components/Button';
import { Select } from '../../components/Select';
import { Spinner } from '../../components/Spinner';
import { useDealerships } from '../booking/useCatalog';
import { BaysTable, ServicesTable, TechniciansTable } from './SetupTables';
import { useDealershipSetup } from './useDealershipSetup';

/** Read-only view of the technicians, bays and services the booking rules work with. */
export function SetupPage() {
  const dealershipsQuery = useDealerships();
  const [chosenId, setChosenId] = useState<number | null>(null);
  const dealerships = dealershipsQuery.data ?? [];
  const dealershipId = chosenId ?? dealerships[0]?.id ?? null;
  const setupQuery = useDealershipSetup(dealershipId);

  return (
    <main className="page page--narrow">
      <h1 className="page__title">Dealership setup</h1>
      <p className="page__lead">
        The technicians, bays and services the system uses when it books. You never pick them; the system does.
      </p>

      {dealershipsQuery.isPending && <Spinner label="Loading dealerships…" />}
      {dealershipsQuery.isError && (
        <Alert title="We could not load the dealerships">
          <Button variant="secondary" onClick={() => void dealershipsQuery.refetch()}>
            Try again
          </Button>
        </Alert>
      )}
      {dealershipsQuery.data && dealerships.length === 0 && <p className="muted">There are no dealerships yet.</p>}

      {dealershipId !== null && (
        <div className="setup">
          <div className="card">
            <Select
              label="Dealership"
              options={dealerships.map((d) => ({ value: String(d.id), label: d.name }))}
              value={String(dealershipId)}
              onChange={(e) => setChosenId(Number(e.target.value))}
            />
          </div>

          {setupQuery.isPending && <Spinner label="Loading the setup…" />}
          {setupQuery.isError && (
            <Alert title="We could not load the setup of this dealership">
              <Button variant="secondary" onClick={() => void setupQuery.refetch()}>
                Try again
              </Button>
            </Alert>
          )}
          {setupQuery.data && <SetupSections setup={setupQuery.data} />}
        </div>
      )}
    </main>
  );
}

function SetupSections({ setup }: { setup: NonNullable<ReturnType<typeof useDealershipSetup>['data']> }) {
  const { dealership } = setup;
  return (
    <>
      <section className="card" aria-labelledby="setup-hours">
        <h2 id="setup-hours" className="card__title">
          Opening hours
        </h2>
        <p className="setup__text">
          Open {dealership.openTime}–{dealership.closeTime} every day ({dealership.timeZoneId} time).
        </p>
        {!dealership.isActive && (
          <p className="setup__text setup__text--warning">This dealership is not active, so it takes no bookings.</p>
        )}
      </section>

      <section className="card" aria-labelledby="setup-services">
        <h2 id="setup-services" className="card__title">
          Services
        </h2>
        <ServicesTable services={setup.serviceTypes} technicians={setup.technicians} />
      </section>

      <section className="card" aria-labelledby="setup-technicians">
        <h2 id="setup-technicians" className="card__title">
          Technicians
        </h2>
        <TechniciansTable technicians={setup.technicians} />
      </section>

      <section className="card" aria-labelledby="setup-bays">
        <h2 id="setup-bays" className="card__title">
          Service bays
        </h2>
        <BaysTable bays={setup.serviceBays} />
      </section>

      <section className="card setup__how" aria-labelledby="setup-how">
        <h2 id="setup-how" className="card__title">
          How the system picks
        </h2>
        <p className="setup__text">
          A technician needs the service's skill at or above its minimum level, and must be on shift for the whole
          job. The bay must be the right type for the service.
        </p>
        <p className="setup__text">
          When several are free, the system keeps rare specialists free. For example, Lan is the only EV technician,
          so an oil change goes to Nam.
        </p>
      </section>
    </>
  );
}
