-- Migration 004: multi-unit pricing
--
-- Real hardware shops sell the same physical stock in multiple units at
-- different prices. Roofing nails can go out as 1 piece (KES 5), 1 kg
-- (~25 pieces, KES 400), or 1 box (120 pieces, KES 500) — all from the
-- same stockpile. This migration:
--
--   1. Adds missing unit types (m², m³, ft, wheelbarrow, tipper)
--   2. Rebuilds product_prices to key on (product, tier, unit) instead of
--      just (product, tier)
--   3. Backfills every existing price with the product's base_unit_id,
--      which preserves the current one-price-per-product behaviour
--
-- Product_units (the conversion table) already exists in the schema but
-- was never populated. That table + the new product_prices together are
-- what make multi-unit selling work.

-- ========== 1. Add missing unit types ==========

INSERT INTO units(name, symbol, allows_decimal) VALUES
 ('Square Metre', 'm²', 1),
 ('Cubic Metre',  'm³', 1),
 ('Foot',         'ft', 1),
 ('Wheelbarrow',  'wbl', 1),
 ('Tipper',       'tip', 1);

-- ========== 2. Rebuild product_prices with unit_id ==========
--
-- SQLite has no ALTER TABLE ADD CONSTRAINT or DROP CONSTRAINT.
-- The standard pattern is: create new table, copy data, drop old, rename new.
-- We run inside the migration runner's transaction so this is atomic.

CREATE TABLE product_prices_new (
    product_id  INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    tier_id     INTEGER NOT NULL REFERENCES price_tiers(id),
    unit_id     INTEGER NOT NULL REFERENCES units(id),
    price_cents INTEGER NOT NULL CHECK (price_cents >= 0),
    PRIMARY KEY (product_id, tier_id, unit_id)
);

-- Backfill: every existing price becomes the price for the product's
-- base unit. Correct because until now that was the only unit we sold.
INSERT INTO product_prices_new (product_id, tier_id, unit_id, price_cents)
SELECT pp.product_id,
       pp.tier_id,
       p.base_unit_id,
       pp.price_cents
  FROM product_prices pp
  JOIN products p ON p.id = pp.product_id;

DROP TABLE product_prices;
ALTER TABLE product_prices_new RENAME TO product_prices;

CREATE INDEX idx_product_prices_product ON product_prices(product_id);

-- ========== 3. Update schema version ==========

UPDATE app_meta SET value = '1.4' WHERE key = 'schema_version';
