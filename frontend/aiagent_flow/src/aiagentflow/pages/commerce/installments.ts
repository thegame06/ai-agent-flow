export type Frequency = 'weekly' | 'biweekly' | 'monthly';

export const FREQUENCY_LABEL: Record<Frequency, string> = {
  weekly: 'Semanal',
  biweekly: 'Quincenal',
  monthly: 'Mensual',
};

export type Installment = { number: number; dueDate: Date; amount: number };

const addPeriods = (date: Date, frequency: Frequency, periods: number) => {
  const next = new Date(date);
  if (frequency === 'weekly') next.setDate(next.getDate() + 7 * periods);
  else if (frequency === 'biweekly') next.setDate(next.getDate() + 14 * periods);
  else next.setMonth(next.getMonth() + periods);
  return next;
};

/**
 * Preview of the schedule the API builds (InstallmentPlanner): equal installments of the total
 * minus the down payment, rounded to cents, the last one absorbing the rounding; the first one
 * period after the sale.
 */
export function buildInstallments(total: number, count: number, frequency: Frequency, downPayment: number, soldAt: Date): Installment[] {
  if (count < 1) return [];
  const financed = Math.round((total - downPayment) * 100);
  const regular = Math.round(financed / count);
  const first = addPeriods(soldAt, frequency, 1);
  return Array.from({ length: count }, (_, i) => ({
    number: i + 1,
    dueDate: addPeriods(first, frequency, i),
    amount: (i === count - 1 ? financed - regular * (count - 1) : regular) / 100,
  }));
}
