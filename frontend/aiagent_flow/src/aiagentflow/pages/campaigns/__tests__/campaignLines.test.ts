import { it, expect, describe } from 'vitest';

import { lineMismatch, campaignPurpose } from '../campaignModel';

// Campaign types as the server numbers them: Sales, Collections, Reminder, Reactivation, Custom.
const SALES = 0;
const COLLECTIONS = 1;
const REMINDER = 2;
const REACTIVATION = 3;
const CUSTOM = 4;

describe('campaign lines', () => {
  it('reads a campaign purpose from its type', () => {
    expect(campaignPurpose(COLLECTIONS)).toBe('collections');
    expect(campaignPurpose(REMINDER)).toBe('collections');
    expect(campaignPurpose(SALES)).toBe('sales');
    expect(campaignPurpose(REACTIVATION)).toBe('sales');
    expect(campaignPurpose(CUSTOM)).toBe('general');
  });

  it('keeps collections and sales on their own lines', () => {
    expect(lineMismatch('sales', COLLECTIONS)).toContain('es de ventas y la campaña es de cobranza');
    expect(lineMismatch('collections', SALES)).toContain('es de cobranza y la campaña es de ventas');
    expect(lineMismatch('marketing', COLLECTIONS)).not.toBeNull();
  });

  it('lets a campaign use a line of its purpose, a general line or any line when custom', () => {
    expect(lineMismatch('collections', REMINDER)).toBeNull();
    expect(lineMismatch('marketing', SALES)).toBeNull();
    expect(lineMismatch('general', COLLECTIONS)).toBeNull();
    expect(lineMismatch(undefined, SALES)).toBeNull();
    expect(lineMismatch('collections', CUSTOM)).toBeNull();
  });
});
