-- =====================================================================
-- HARDWARE STORE POS : SQLite schema, version 1.1
-- Run at every connection:
--   PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;
--   PRAGMA synchronous = FULL; PRAGMA busy_timeout = 5000;
-- Money = INTEGER cents. Quantities = INTEGER thousandths. No floats.
-- business_date = shop-local 'YYYY-MM-DD'. All daily reports group by it.
-- stock_movements and audit_log are append-only (triggers).
-- =====================================================================

PRAGMA foreign_keys = ON;

CREATE TABLE app_meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE settings (
    key        TEXT PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);

CREATE TABLE document_sequences (
    doc_type    TEXT PRIMARY KEY,
    prefix      TEXT NOT NULL,
    next_number INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE users (
    id              INTEGER PRIMARY KEY,
    name            TEXT NOT NULL,
    role            TEXT NOT NULL CHECK (role IN ('ADMIN','MANAGER','CASHIER')),
    pin_hash        TEXT NOT NULL,
    is_active       INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    failed_attempts INTEGER NOT NULL DEFAULT 0,
    locked_until    TEXT,
    last_login_at   TEXT,
    created_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);

CREATE TABLE _stock_guard_ctx (
    id     INTEGER PRIMARY KEY CHECK (id = 1),
    active INTEGER NOT NULL DEFAULT 0 CHECK (active IN (0,1))
);
INSERT INTO _stock_guard_ctx(id, active) VALUES (1, 0);

CREATE TABLE categories (
    id        INTEGER PRIMARY KEY,
    name      TEXT NOT NULL UNIQUE,
    parent_id INTEGER REFERENCES categories(id),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);

CREATE TABLE units (
    id             INTEGER PRIMARY KEY,
    name           TEXT NOT NULL UNIQUE,
    symbol         TEXT NOT NULL,
    allows_decimal INTEGER NOT NULL DEFAULT 0 CHECK (allows_decimal IN (0,1)),
    is_active      INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);

CREATE TABLE tax_classes (
    id        INTEGER PRIMARY KEY,
    name      TEXT NOT NULL UNIQUE,
    rate_bp   INTEGER NOT NULL CHECK (rate_bp BETWEEN 0 AND 10000),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);

CREATE TABLE import_batches (
    id          INTEGER PRIMARY KEY,
    source_file TEXT NOT NULL,
    kind        TEXT NOT NULL CHECK (kind IN ('PRODUCTS','CUSTOMERS','SUPPLIERS','OPENING_STOCK')),
    row_count   INTEGER NOT NULL,
    created_by  INTEGER REFERENCES users(id),
    created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    undone_at   TEXT
);

CREATE TABLE products (
    id                  INTEGER PRIMARY KEY,
    uuid                TEXT NOT NULL UNIQUE,
    sku                 TEXT UNIQUE,
    name                TEXT NOT NULL,
    category_id         INTEGER REFERENCES categories(id),
    base_unit_id        INTEGER NOT NULL REFERENCES units(id),
    tax_class_id        INTEGER REFERENCES tax_classes(id),
    track_stock         INTEGER NOT NULL DEFAULT 1 CHECK (track_stock IN (0,1)),
    stock_milli         INTEGER NOT NULL DEFAULT 0,
    avg_cost_cents      INTEGER NOT NULL DEFAULT 0 CHECK (avg_cost_cents >= 0),
    last_cost_cents     INTEGER NOT NULL DEFAULT 0 CHECK (last_cost_cents >= 0),
    reorder_level_milli INTEGER NOT NULL DEFAULT 0 CHECK (reorder_level_milli >= 0),
    is_active           INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    import_batch_id     INTEGER REFERENCES import_batches(id),
    created_at          TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    updated_at          TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);
CREATE INDEX idx_products_name     ON products(name);
CREATE INDEX idx_products_category ON products(category_id);
CREATE INDEX idx_products_batch    ON products(import_batch_id);

CREATE TABLE product_barcodes (
    barcode    TEXT PRIMARY KEY,
    product_id INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    is_primary INTEGER NOT NULL DEFAULT 0 CHECK (is_primary IN (0,1))
);
CREATE INDEX idx_barcodes_product ON product_barcodes(product_id);

CREATE TABLE product_units (
    id           INTEGER PRIMARY KEY,
    product_id   INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    unit_id      INTEGER NOT NULL REFERENCES units(id),
    factor_milli INTEGER NOT NULL CHECK (factor_milli > 0),
    UNIQUE (product_id, unit_id)
);

CREATE TABLE price_tiers (
    id         INTEGER PRIMARY KEY,
    name       TEXT NOT NULL UNIQUE,
    is_default INTEGER NOT NULL DEFAULT 0 CHECK (is_default IN (0,1)),
    is_active  INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);
CREATE UNIQUE INDEX ux_price_tier_default
    ON price_tiers(is_default) WHERE is_default = 1;

CREATE TABLE product_prices (
    product_id  INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    tier_id     INTEGER NOT NULL REFERENCES price_tiers(id),
    price_cents INTEGER NOT NULL CHECK (price_cents >= 0),
    PRIMARY KEY (product_id, tier_id)
);

CREATE TABLE suppliers (
    id              INTEGER PRIMARY KEY,
    name            TEXT NOT NULL,
    phone           TEXT,
    email           TEXT,
    notes           TEXT,
    is_active       INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    import_batch_id INTEGER REFERENCES import_batches(id)
);

CREATE TABLE customers (
    id                 INTEGER PRIMARY KEY,
    uuid               TEXT NOT NULL UNIQUE,
    name               TEXT NOT NULL,
    phone              TEXT,
    email              TEXT,
    kra_pin            TEXT,
    price_tier_id      INTEGER REFERENCES price_tiers(id),
    credit_limit_cents INTEGER NOT NULL DEFAULT 0 CHECK (credit_limit_cents >= 0),
    notes              TEXT,
    is_active          INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    import_batch_id    INTEGER REFERENCES import_batches(id),
    created_at         TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);
CREATE INDEX idx_customers_name  ON customers(name);
CREATE INDEX idx_customers_phone ON customers(phone);
CREATE INDEX idx_customers_batch ON customers(import_batch_id);

CREATE TABLE stock_movements (
    id              INTEGER PRIMARY KEY,
    product_id      INTEGER NOT NULL REFERENCES products(id),
    movement_type   TEXT NOT NULL CHECK (movement_type IN (
                        'OPENING','PURCHASE','SALE','SALE_VOID',
                        'RETURN_IN','RETURN_OUT','ADJUST_GAIN','ADJUST_LOSS','DAMAGE')),
    qty_milli       INTEGER NOT NULL CHECK (qty_milli <> 0),
    unit_cost_cents INTEGER,
    ref_type        TEXT CHECK (ref_type IS NULL OR ref_type IN
                        ('SALE','RETURN','GRN','ADJUSTMENT','OPENING','IMPORT')),
    ref_id          INTEGER,
    reason          TEXT,
    user_id         INTEGER REFERENCES users(id),
    import_batch_id INTEGER REFERENCES import_batches(id),
    business_date   TEXT NOT NULL CHECK (
                        business_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
    created_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CHECK (
        (movement_type IN ('OPENING','PURCHASE','SALE_VOID','RETURN_IN','ADJUST_GAIN') AND qty_milli > 0)
        OR
        (movement_type IN ('SALE','RETURN_OUT','ADJUST_LOSS','DAMAGE') AND qty_milli < 0)
    ),
    CHECK (
        movement_type NOT IN ('ADJUST_GAIN','ADJUST_LOSS','DAMAGE')
        OR (reason IS NOT NULL AND length(trim(reason)) > 0)
    )
);
CREATE INDEX idx_movements_product ON stock_movements(product_id, created_at);
CREATE INDEX idx_movements_ref     ON stock_movements(ref_type, ref_id);
CREATE INDEX idx_movements_batch   ON stock_movements(import_batch_id);
CREATE INDEX idx_movements_bdate   ON stock_movements(business_date);

CREATE TRIGGER trg_movement_apply AFTER INSERT ON stock_movements
BEGIN
    UPDATE _stock_guard_ctx SET active = 1 WHERE id = 1;
    UPDATE products
       SET stock_milli = stock_milli + NEW.qty_milli,
           updated_at  = strftime('%Y-%m-%dT%H:%M:%SZ','now')
     WHERE id = NEW.product_id;
    UPDATE _stock_guard_ctx SET active = 0 WHERE id = 1;
END;

CREATE TRIGGER trg_products_stock_guard
BEFORE UPDATE OF stock_milli ON products
WHEN NEW.stock_milli <> OLD.stock_milli
 AND (SELECT active FROM _stock_guard_ctx WHERE id = 1) <> 1
BEGIN
    SELECT RAISE(ABORT, 'products.stock_milli is maintained by stock_movements only');
END;

CREATE TRIGGER trg_movement_no_update BEFORE UPDATE ON stock_movements
BEGIN SELECT RAISE(ABORT, 'stock_movements is append-only'); END;

CREATE TRIGGER trg_movement_no_delete BEFORE DELETE ON stock_movements
BEGIN SELECT RAISE(ABORT, 'stock_movements is append-only'); END;

CREATE TABLE goods_receipts (
    id            INTEGER PRIMARY KEY,
    grn_number    TEXT NOT NULL UNIQUE,
    supplier_id   INTEGER REFERENCES suppliers(id),
    supplier_ref  TEXT,
    total_cents   INTEGER NOT NULL DEFAULT 0 CHECK (total_cents >= 0),
    paid_cents    INTEGER NOT NULL DEFAULT 0 CHECK (paid_cents >= 0),
    user_id       INTEGER REFERENCES users(id),
    business_date TEXT NOT NULL CHECK (
                        business_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
    created_at    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CHECK (paid_cents <= total_cents)
);

CREATE TABLE goods_receipt_items (
    id              INTEGER PRIMARY KEY,
    receipt_id      INTEGER NOT NULL REFERENCES goods_receipts(id) ON DELETE CASCADE,
    product_id      INTEGER NOT NULL REFERENCES products(id),
    qty_milli       INTEGER NOT NULL CHECK (qty_milli > 0),
    unit_cost_cents INTEGER NOT NULL CHECK (unit_cost_cents >= 0)
);

CREATE TABLE cash_sessions (
    id                  INTEGER PRIMARY KEY,
    user_id             INTEGER NOT NULL REFERENCES users(id),
    opened_at           TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    opening_float_cents INTEGER NOT NULL DEFAULT 0,
    closed_at           TEXT,
    counted_cash_cents  INTEGER,
    expected_cash_cents INTEGER,
    notes               TEXT
);
CREATE UNIQUE INDEX ux_cash_session_open
    ON cash_sessions(user_id) WHERE closed_at IS NULL;

CREATE TABLE cash_movements (
    id              INTEGER PRIMARY KEY,
    cash_session_id INTEGER NOT NULL REFERENCES cash_sessions(id),
    direction       TEXT NOT NULL CHECK (direction IN ('IN','OUT')),
    amount_cents    INTEGER NOT NULL CHECK (amount_cents > 0),
    reason          TEXT NOT NULL,
    user_id         INTEGER REFERENCES users(id),
    business_date   TEXT NOT NULL CHECK (
                        business_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
    created_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);
CREATE INDEX idx_cash_movements_session ON cash_movements(cash_session_id);

CREATE TABLE sales (
    id                 INTEGER PRIMARY KEY,
    uuid               TEXT NOT NULL UNIQUE,
    receipt_number     TEXT,
    status             TEXT NOT NULL DEFAULT 'COMPLETED'
                       CHECK (status IN ('HELD','COMPLETED','VOIDED')),
    customer_id        INTEGER REFERENCES customers(id),
    user_id            INTEGER NOT NULL REFERENCES users(id),
    cash_session_id    INTEGER REFERENCES cash_sessions(id),
    price_tier_id      INTEGER REFERENCES price_tiers(id),
    prices_include_tax INTEGER NOT NULL,
    subtotal_cents     INTEGER NOT NULL CHECK (subtotal_cents >= 0),
    discount_cents     INTEGER NOT NULL DEFAULT 0 CHECK (discount_cents >= 0),
    tax_cents          INTEGER NOT NULL DEFAULT 0 CHECK (tax_cents >= 0),
    total_cents        INTEGER NOT NULL CHECK (total_cents >= 0),
    fiscal_ref         TEXT,
    void_reason        TEXT,
    voided_by          INTEGER REFERENCES users(id),
    voided_at          TEXT,
    business_date      TEXT NOT NULL CHECK (
                        business_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
    created_at         TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CHECK (status = 'HELD' OR receipt_number IS NOT NULL)
);
CREATE UNIQUE INDEX ux_sales_receipt
    ON sales(receipt_number) WHERE receipt_number IS NOT NULL;
CREATE INDEX idx_sales_bdate    ON sales(business_date);
CREATE INDEX idx_sales_customer ON sales(customer_id);
CREATE INDEX idx_sales_status   ON sales(status);
CREATE INDEX idx_sales_session  ON sales(cash_session_id);

CREATE TRIGGER trg_sales_no_delete
BEFORE DELETE ON sales
WHEN OLD.status <> 'HELD'
BEGIN
    SELECT RAISE(ABORT, 'completed or voided sales cannot be deleted');
END;

CREATE TABLE sale_items (
    id               INTEGER PRIMARY KEY,
    sale_id          INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
    product_id       INTEGER NOT NULL REFERENCES products(id),
    product_name     TEXT NOT NULL,
    product_sku      TEXT,
    unit_id          INTEGER NOT NULL REFERENCES units(id),
    qty_milli        INTEGER NOT NULL CHECK (qty_milli > 0),
    base_qty_milli   INTEGER NOT NULL CHECK (base_qty_milli > 0),
    unit_price_cents INTEGER NOT NULL CHECK (unit_price_cents >= 0),
    unit_cost_cents  INTEGER NOT NULL CHECK (unit_cost_cents >= 0),
    discount_cents   INTEGER NOT NULL DEFAULT 0 CHECK (discount_cents >= 0),
    tax_rate_bp      INTEGER NOT NULL DEFAULT 0,
    line_total_cents INTEGER NOT NULL CHECK (line_total_cents >= 0),
    notes            TEXT
);
CREATE INDEX idx_sale_items_sale    ON sale_items(sale_id);
CREATE INDEX idx_sale_items_product ON sale_items(product_id);

CREATE TABLE payments (
    id            INTEGER PRIMARY KEY,
    sale_id       INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
    method        TEXT NOT NULL CHECK (method IN ('CASH','MPESA','CARD','BANK','CREDIT','OTHER')),
    amount_cents  INTEGER NOT NULL CHECK (amount_cents > 0),
    reference     TEXT,
    business_date TEXT NOT NULL CHECK (
                        business_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
    created_at    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);
CREATE INDEX idx_payments_sale      ON payments(sale_id);
CREATE INDEX idx_payments_reference ON payments(reference);
CREATE INDEX idx_payments_method    ON payments(method);
CREATE INDEX idx_payments_bdate     ON payments(business_date);

CREATE UNIQUE INDEX ux_payments_mpesa_ref
    ON payments(reference)
    WHERE method = 'MPESA' AND reference IS NOT NULL AND reference <> '';

CREATE TABLE returns (
    id                 INTEGER PRIMARY KEY,
    uuid               TEXT NOT NULL UNIQUE,
    return_number      TEXT NOT NULL UNIQUE,
    original_sale_id   INTEGER NOT NULL REFERENCES sales(id),
    customer_id        INTEGER REFERENCES customers(id),
    user_id            INTEGER NOT NULL REFERENCES users(id),
    cash_session_id    INTEGER REFERENCES cash_sessions(id),
    refund_method      TEXT NOT NULL CHECK (refund_method IN
                       ('CASH','MPESA','CARD','BANK','CREDIT_NOTE','ACCOUNT_CREDIT')),
    refund_cents       INTEGER NOT NULL CHECK (refund_cents >= 0),
    prices_include_tax INTEGER NOT NULL,
    reason             TEXT NOT NULL,
    business_date      TEXT NOT NULL CHECK (
                        business_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
    created_at         TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);
CREATE INDEX idx_returns_sale  ON returns(original_sale_id);
CREATE INDEX idx_returns_bdate ON returns(business_date);

CREATE TABLE return_items (
    id               INTEGER PRIMARY KEY,
    return_id        INTEGER NOT NULL REFERENCES returns(id) ON DELETE CASCADE,
    sale_item_id     INTEGER NOT NULL REFERENCES sale_items(id),
    product_id       INTEGER NOT NULL REFERENCES products(id),
    qty_milli        INTEGER NOT NULL CHECK (qty_milli > 0),
    base_qty_milli   INTEGER NOT NULL CHECK (base_qty_milli > 0),
    unit_price_cents INTEGER NOT NULL CHECK (unit_price_cents >= 0),
    unit_cost_cents  INTEGER NOT NULL CHECK (unit_cost_cents >= 0),
    tax_rate_bp      INTEGER NOT NULL DEFAULT 0,
    refund_cents     INTEGER NOT NULL CHECK (refund_cents >= 0)
);
CREATE INDEX idx_return_items_return ON return_items(return_id);
CREATE INDEX idx_return_items_sitem  ON return_items(sale_item_id);

CREATE TABLE customer_ledger (
    id              INTEGER PRIMARY KEY,
    customer_id     INTEGER NOT NULL REFERENCES customers(id),
    entry_type      TEXT NOT NULL CHECK (entry_type IN ('OPENING','CHARGE','PAYMENT','ADJUSTMENT')),
    amount_cents    INTEGER NOT NULL CHECK (amount_cents <> 0),
    sale_id         INTEGER REFERENCES sales(id),
    return_id       INTEGER REFERENCES returns(id),
    cash_session_id INTEGER REFERENCES cash_sessions(id),
    method          TEXT CHECK (method IS NULL OR method IN
                        ('CASH','MPESA','CARD','BANK','CREDIT','OTHER')),
    reference       TEXT,
    notes           TEXT,
    user_id         INTEGER REFERENCES users(id),
    import_batch_id INTEGER REFERENCES import_batches(id),
    business_date   TEXT NOT NULL CHECK (
                        business_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
    created_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);
CREATE INDEX idx_ledger_customer ON customer_ledger(customer_id, created_at);
CREATE INDEX idx_ledger_bdate    ON customer_ledger(business_date);
CREATE INDEX idx_ledger_batch    ON customer_ledger(import_batch_id);

CREATE TABLE quotations (
    id                 INTEGER PRIMARY KEY,
    quote_number       TEXT NOT NULL UNIQUE,
    customer_id        INTEGER REFERENCES customers(id),
    status             TEXT NOT NULL DEFAULT 'OPEN'
                       CHECK (status IN ('OPEN','CONVERTED','EXPIRED','CANCELLED')),
    valid_until        TEXT,
    prices_include_tax INTEGER NOT NULL,
    subtotal_cents     INTEGER NOT NULL DEFAULT 0 CHECK (subtotal_cents >= 0),
    discount_cents     INTEGER NOT NULL DEFAULT 0 CHECK (discount_cents >= 0),
    tax_cents          INTEGER NOT NULL DEFAULT 0 CHECK (tax_cents >= 0),
    total_cents        INTEGER NOT NULL DEFAULT 0 CHECK (total_cents >= 0),
    sale_id            INTEGER REFERENCES sales(id),
    notes              TEXT,
    user_id            INTEGER REFERENCES users(id),
    created_at         TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);

CREATE TABLE quotation_items (
    id               INTEGER PRIMARY KEY,
    quotation_id     INTEGER NOT NULL REFERENCES quotations(id) ON DELETE CASCADE,
    product_id       INTEGER NOT NULL REFERENCES products(id),
    product_name     TEXT NOT NULL,
    unit_id          INTEGER NOT NULL REFERENCES units(id),
    qty_milli        INTEGER NOT NULL CHECK (qty_milli > 0),
    base_qty_milli   INTEGER NOT NULL CHECK (base_qty_milli > 0),
    unit_price_cents INTEGER NOT NULL CHECK (unit_price_cents >= 0),
    discount_cents   INTEGER NOT NULL DEFAULT 0 CHECK (discount_cents >= 0),
    tax_rate_bp      INTEGER NOT NULL DEFAULT 0,
    line_total_cents INTEGER NOT NULL CHECK (line_total_cents >= 0),
    notes            TEXT
);

CREATE TABLE audit_log (
    id          INTEGER PRIMARY KEY,
    user_id     INTEGER REFERENCES users(id),
    action      TEXT NOT NULL,
    entity_type TEXT,
    entity_id   INTEGER,
    old_value   TEXT,
    new_value   TEXT,
    created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);
CREATE INDEX idx_audit_entity ON audit_log(entity_type, entity_id);
CREATE INDEX idx_audit_user   ON audit_log(user_id);
CREATE INDEX idx_audit_time   ON audit_log(created_at);

CREATE TRIGGER trg_audit_no_update BEFORE UPDATE ON audit_log
BEGIN SELECT RAISE(ABORT, 'audit_log is append-only'); END;

CREATE TRIGGER trg_audit_no_delete BEFORE DELETE ON audit_log
BEGIN SELECT RAISE(ABORT, 'audit_log is append-only'); END;

CREATE VIEW v_customer_balance AS
SELECT c.id AS customer_id, c.name, c.credit_limit_cents,
       COALESCE(SUM(l.amount_cents), 0) AS balance_cents
FROM customers c LEFT JOIN customer_ledger l ON l.customer_id = c.id
GROUP BY c.id;

CREATE VIEW v_low_stock AS
SELECT id, name, stock_milli, reorder_level_milli
FROM products
WHERE is_active = 1 AND track_stock = 1 AND stock_milli <= reorder_level_milli;

CREATE VIEW v_daily_sales AS
SELECT business_date AS day,
       COUNT(*)         AS sales_count,
       SUM(total_cents) AS total_cents
FROM sales
WHERE status = 'COMPLETED'
GROUP BY business_date;

INSERT INTO app_meta(key, value) VALUES ('schema_version', '1.1');

INSERT INTO units(name, symbol, allows_decimal) VALUES
 ('Piece','pc',0), ('Kilogram','kg',1), ('Metre','m',1),
 ('Litre','L',1), ('Bag','bag',0), ('Box','box',0), ('Roll','roll',0);

INSERT INTO tax_classes(name, rate_bp) VALUES
 ('Standard',1600), ('Zero-rated',0), ('Exempt',0);

INSERT INTO price_tiers(name, is_default) VALUES
 ('Retail',1), ('Wholesale',0), ('Contractor',0);

INSERT INTO document_sequences(doc_type, prefix, next_number) VALUES
 ('SALE','RCT-',1), ('RETURN','RET-',1), ('QUOTE','QT-',1),
 ('GRN','GRN-',1), ('PAYMENT','PAY-',1);

INSERT INTO settings(key, value) VALUES
 ('allow_negative_stock','0'),
 ('prices_include_tax','1'),
 ('currency','KES'),
 ('shop_timezone','Africa/Nairobi'),
 ('require_pin_for_discount','1');
