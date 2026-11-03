import { useId, type ReactNode } from 'react';
import type { DealershipSetup } from '../../api/types';
import { formatDuration } from '../../lib/formatters';

type Service = DealershipSetup['serviceTypes'][number];
type Technician = DealershipSetup['technicians'][number];
type Bay = DealershipSetup['serviceBays'][number];

/**
 * A table that scrolls sideways inside its own box on narrow screens (never the page).
 * The box is focusable and named, so keyboard users can scroll it too.
 */
function ScrollTable({ caption, compact = false, children }: { caption: string; compact?: boolean; children: ReactNode }) {
  const captionId = useId();
  return (
    <div className="table-scroll" role="region" aria-labelledby={captionId} tabIndex={0}>
      <table className={compact ? 'table table--compact' : 'table'}>
        <caption id={captionId} className="sr-only">
          {caption}
        </caption>
        {children}
      </table>
    </div>
  );
}

function StatusCell({ isActive }: { isActive: boolean }) {
  return <td>{isActive ? 'Active' : <span className="table__inactive-label">Inactive</span>}</td>;
}

const rowClass = (isActive: boolean) => (isActive ? undefined : 'table__row--inactive');

/** "EV, Senior or above" */
function formatNeeds(service: Pick<Service, 'requiredSkill' | 'minimumTier'>): string {
  return `${service.requiredSkill}, ${service.minimumTier} or above`;
}

function WhoCanDoIt({ service, technicians }: { service: Service; technicians: Technician[] }) {
  const names = service.qualifiedTechnicianIds.map((id) => technicians.find((t) => t.id === id)?.name ?? `#${id}`);
  if (names.length === 0) return <span className="muted">Nobody active</span>;
  return (
    <span className="who">
      {names.join(', ')}
      {names.length === 1 && <span className="chip chip--accent">only one</span>}
    </span>
  );
}

export function ServicesTable({ services, technicians }: { services: Service[]; technicians: Technician[] }) {
  if (services.length === 0) return <p className="muted">This dealership has no services.</p>;
  return (
    <ScrollTable caption="Services">
      <thead>
        <tr>
          <th scope="col">Service</th>
          <th scope="col">Duration</th>
          <th scope="col">Needs</th>
          <th scope="col">Bay type</th>
          <th scope="col">Who can do it</th>
        </tr>
      </thead>
      <tbody>
        {services.map((s) => (
          <tr key={s.id} className={rowClass(s.isActive)}>
            <th scope="row">
              {s.name}
              {!s.isActive && <span className="table__inactive-label"> · Inactive</span>}
            </th>
            <td>{formatDuration(s.durationMinutes)}</td>
            <td>{formatNeeds(s)}</td>
            <td>{s.requiredBayType}</td>
            <td>
              <WhoCanDoIt service={s} technicians={technicians} />
            </td>
          </tr>
        ))}
      </tbody>
    </ScrollTable>
  );
}

export function TechniciansTable({ technicians }: { technicians: Technician[] }) {
  if (technicians.length === 0) return <p className="muted">This dealership has no technicians.</p>;
  return (
    <ScrollTable caption="Technicians">
      <thead>
        <tr>
          <th scope="col">Name</th>
          <th scope="col">Shift</th>
          <th scope="col">Skills</th>
          <th scope="col">Status</th>
        </tr>
      </thead>
      <tbody>
        {technicians.map((t) => (
          <tr key={t.id} className={rowClass(t.isActive)}>
            <th scope="row">{t.name}</th>
            <td className="table__nowrap">
              {t.shiftStart}–{t.shiftEnd}
            </td>
            <td>
              {t.skills.length === 0 ? (
                <span className="muted">None</span>
              ) : (
                <ul className="chips" aria-label={`Skills of ${t.name}`}>
                  {t.skills.map((s) => (
                    <li key={s.skill} className="chip">
                      {s.skill} · {s.tier}
                    </li>
                  ))}
                </ul>
              )}
            </td>
            <StatusCell isActive={t.isActive} />
          </tr>
        ))}
      </tbody>
    </ScrollTable>
  );
}

export function BaysTable({ bays }: { bays: Bay[] }) {
  if (bays.length === 0) return <p className="muted">This dealership has no service bays.</p>;
  return (
    <ScrollTable caption="Service bays" compact>
      <thead>
        <tr>
          <th scope="col">Name</th>
          <th scope="col">Type</th>
          <th scope="col">Status</th>
        </tr>
      </thead>
      <tbody>
        {bays.map((b) => (
          <tr key={b.id} className={rowClass(b.isActive)}>
            <th scope="row">{b.name}</th>
            <td>{b.bayType}</td>
            <StatusCell isActive={b.isActive} />
          </tr>
        ))}
      </tbody>
    </ScrollTable>
  );
}
