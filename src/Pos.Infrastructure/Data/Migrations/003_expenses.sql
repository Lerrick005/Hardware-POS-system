-- Migration 003: add expenses table
--
-- Enables honest profit tracking. Without expenses, "profit" from revenue
-- minus COGS is gross profit only — it tells the shop owner they made 90,000
-- when rent, salaries and transport may have eaten all of it.
--
-- Categories are free-text (not a lookup table). Shops categorize differently:
-- some want "Rent", "Salary", "Transport"; others want "Fixed", "Variable".
-- A lookup table would force one model on every shop. Free text is simpler.
--
-- amount_cents is always positive. Expenses are outflows by nature; the sign
-- is contextual (positive number = money out). Never store negative amounts.

CREATE TABLE expenses (
    id            INTEGER PRIMARY KEY,
    category      TEXT NOT NULL,
    description   TEXT,
    amount_cents  INTEGER NOT NULL CHECK (amount_cents > 0),
    business_date TEXT NOT NULL CHECK (
                      business_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
    user_id       INTEGER REFERENCES users(id),
    notes         TEXT,
    created_at    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);

CREATE INDEX idx_expenses_bdate ON expenses(business_date);
CREATE INDEX idx_expenses_category ON expenses(category);

UPDATE app_meta SET value = '1.3' WHERE key = 'schema_version';
