// The one-click guided tour for the live preview. It drives the real app through the DOM, the same way a
// person would: it picks options, types the date, clicks time slots and buttons, and waits for the
// (simulated) API to answer. Nothing in the app is mocked or skipped; only the clicks are scripted.

import { addDays, localToUtc, type MockSchedulerBackend } from './mockBackend';
import { TIME_ZONE } from './seed';

export interface TourStep {
  index: number;
  total: number;
  title: string;
  detail: string;
}

export interface TourOptions {
  backend: MockSchedulerBackend;
  /** Restore the seed data and remount the app (the banner's reset). */
  resetApp: () => void;
  onStep: (step: TourStep) => void;
  signal: AbortSignal;
  /** Multiplies every pause. 1 = normal speed. */
  pace?: number;
  root?: ParentNode;
}

export class TourStopped extends Error {
  constructor() {
    super('The tour was stopped.');
  }
}

const TOTAL_STEPS = 7;

export async function runTour(options: TourOptions): Promise<void> {
  const { backend, resetApp, onStep, signal } = options;
  const pace = options.pace ?? 1;
  const root = options.root ?? document;
  const cursor = createCursor();

  const check = () => {
    if (signal.aborted) throw new TourStopped();
  };
  const sleep = (ms: number) =>
    new Promise<void>((resolve, reject) => {
      const timer = setTimeout(resolve, ms * pace);
      signal.addEventListener(
        'abort',
        () => {
          clearTimeout(timer);
          reject(new TourStopped());
        },
        { once: true },
      );
    });
  const waitFor = async <T>(find: () => T | null | undefined, what: string, timeoutMs = 10_000): Promise<T> => {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      check();
      const found = find();
      if (found) return found;
      if (Date.now() > deadline) throw new Error(`The tour could not find ${what}.`);
      await new Promise((r) => setTimeout(r, 60));
    }
  };

  const step = (index: number, title: string, detail: string) => {
    check();
    onStep({ index, total: TOTAL_STEPS, title, detail });
  };

  // --- DOM helpers -----------------------------------------------------------------------------

  const text = (el: Element) => (el.textContent ?? '').replace(/\s+/g, ' ').trim();
  const findButton = (label: string, scope: ParentNode = root) =>
    Array.from(scope.querySelectorAll<HTMLElement>('button, a')).find((el) => text(el) === label && !el.hasAttribute('disabled'));
  const fieldByLabel = <T extends HTMLElement>(label: string) => {
    const l = Array.from(root.querySelectorAll('label')).find((x) => text(x) === label);
    const el = l?.htmlFor ? document.getElementById(l.htmlFor) : null;
    return (el as T | null) ?? null;
  };

  const point = async (el: HTMLElement) => {
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    await sleep(350);
    cursor.moveTo(el);
    el.classList.add('demo-target');
    await sleep(650);
  };
  const release = (el: HTMLElement) => el.classList.remove('demo-target');

  const click = async (el: HTMLElement) => {
    await point(el);
    cursor.press();
    el.click();
    release(el);
    await sleep(500);
  };

  const choose = async (label: string, optionText: string) => {
    const select = await waitFor(
      () => {
        const s = fieldByLabel<HTMLSelectElement>(label);
        return s && !s.disabled && Array.from(s.options).some((o) => o.text.includes(optionText)) ? s : null;
      },
      `the "${label}" list`,
    );
    await point(select);
    const option = Array.from(select.options).find((o) => o.text.includes(optionText))!;
    setNativeValue(select, option.value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
    release(select);
    await sleep(500);
  };

  const typeDate = async (label: string, value: string) => {
    const input = await waitFor(() => fieldByLabel<HTMLInputElement>(label), `the "${label}" field`);
    await point(input);
    setNativeValue(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
    release(input);
    await sleep(500);
  };

  const pickSlot = async (label: string) => {
    const slot = await waitFor(
      () => Array.from(root.querySelectorAll<HTMLButtonElement>('.slots__grid .slot')).find((b) => text(b) === label),
      `the ${label} time slot`,
    );
    await click(slot);
  };

  const fillBooking = async (vehicle: string, service: string, date: string) => {
    await choose('Vehicle', vehicle);
    await choose('Service centre', 'District 1');
    await choose('Service type', service);
    await typeDate('Date', date);
  };

  const book = async () => {
    const submit = await waitFor(() => findButton('Book appointment'), 'the Book appointment button');
    await click(submit);
  };

  const confirmationValue = (term: string) => {
    const dt = Array.from(root.querySelectorAll('.confirm__grid dt')).find((x) => text(x) === term);
    return dt?.nextElementSibling ? text(dt.nextElementSibling) : '';
  };

  try {
    // 1. Fresh start.
    step(1, 'Starting fresh', 'Resetting the demo data so every run looks the same.');
    resetApp();
    await sleep(900);
    const dateInput = await waitFor(() => fieldByLabel<HTMLInputElement>('Date'), 'the booking form');
    const demoDate = addDays(dateInput.min || new Date().toISOString().slice(0, 10), 1);

    // 2. First booking.
    step(2, 'Booking an EV battery check', 'Toyota Vios at District 1, tomorrow. Only free start times are offered.');
    await fillBooking('Toyota', 'EV battery check', demoDate);
    await pickSlot('10:00');
    await book();
    await waitFor(() => root.querySelector('.confirm'), 'the confirmation');
    const firstRef = confirmationValue('Booking reference');
    step(
      3,
      'Booked: the system chose the people and the place',
      `Technician ${confirmationValue('Technician')} (the EV specialist) and ${confirmationValue('Service bay')} (the EV bay) were assigned automatically.`,
    );
    await point(await waitFor(() => root.querySelector<HTMLElement>('.confirm__grid'), 'the details'));
    await sleep(2600);
    release(root.querySelector<HTMLElement>('.confirm__grid')!);

    // 4. Second booking, with a race.
    const another = await waitFor(() => findButton('Book another service'), 'Book another service');
    await click(another);
    step(4, 'A second booking, and someone is faster', 'Honda CR-V, same service, 11:00. While we fill the form, another customer takes 11:00.');
    await fillBooking('Honda', 'EV battery check', demoDate);
    await pickSlot('11:00');
    backend.simulateOtherCustomerBooking(1, 4, localToUtc(demoDate, '11:00', TIME_ZONE));
    await sleep(600);
    await book();
    const suggestion = await waitFor(
      () => {
        const group = root.querySelector('[aria-label="Suggested start times"]');
        return group ? group.querySelector<HTMLButtonElement>('button') : null;
      },
      'the suggested times',
    );
    step(5, 'No double booking: 409 with suggestions', 'The slot was taken a moment ago, so the API refused it and offered the nearest free times.');
    await point(suggestion.closest('.alert') as HTMLElement);
    await sleep(2600);
    release(suggestion.closest('.alert') as HTMLElement);
    await click(suggestion);
    await book();
    await waitFor(() => root.querySelector('.confirm'), 'the second confirmation');
    await sleep(1800);

    // 6. Cancel the first booking.
    step(6, 'Cancelling the first booking', 'Cancel asks for confirmation, then frees the technician, bay and car.');
    await click(await waitFor(() => findButton('View my appointments'), 'View my appointments'));
    const card = await waitFor(
      () => Array.from(root.querySelectorAll<HTMLElement>('article.appt')).find((a) => firstRef && text(a).includes(firstRef)),
      'the first appointment',
    );
    await click(await waitFor(() => findButton('Cancel', card), 'the Cancel button'));
    await sleep(900);
    await click(await waitFor(() => findButton('Yes, cancel', card), 'Yes, cancel'));
    await waitFor(() => (text(card).includes('Cancelled') ? card : null), 'the cancelled status');
    await point(card);
    await sleep(1500);
    release(card);

    step(7, 'Tour complete', 'Everything you saw ran through the real screens. Try it yourself, or reset the data.');
  } finally {
    cursor.remove();
    root.querySelectorAll('.demo-target').forEach((el) => el.classList.remove('demo-target'));
  }
}

/** React tracks the value it last set, so set the DOM value through the prototype setter before dispatching. */
function setNativeValue(el: HTMLInputElement | HTMLSelectElement, value: string) {
  const proto = el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(proto, 'value')?.set?.call(el, value);
}

function createCursor() {
  const dot = document.createElement('div');
  dot.className = 'demo-cursor';
  dot.setAttribute('aria-hidden', 'true');
  document.body.appendChild(dot);
  return {
    moveTo(el: HTMLElement) {
      const r = el.getBoundingClientRect();
      dot.style.transform = `translate(${r.left + Math.min(r.width / 2, 60)}px, ${r.top + r.height / 2}px)`;
      dot.classList.add('demo-cursor--visible');
    },
    press() {
      dot.classList.remove('demo-cursor--press');
      void dot.offsetWidth;
      dot.classList.add('demo-cursor--press');
    },
    remove() {
      dot.remove();
    },
  };
}
