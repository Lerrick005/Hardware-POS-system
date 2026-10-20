# Architecture Decisions

Format: ADR-lite. Newest at top. Never delete; supersede instead.

## ADR-0001: SQLite, not PostgreSQL (R1)
- Date: 2026-10-19
- Status: Accepted
- Context: Playbook 3.1/3.5 target single-PC offline POS. PostgreSQL adds install, service, credentials and backup burden on shop PCs.
- Decision: SQLite (Microsoft.Data.Sqlite) with WAL, synchronous=FULL, busy_timeout=5000, foreign_keys=ON on every connection.
- Consequences: Multi-till requires R3 local-server mode or PostgreSQL; Application layer sits behind interfaces to keep that change contained.

## ADR-0002: Integer money and milli-units only
- Date: 2026-10-19
- Status: Accepted
- Decision: Money stored as integer cents; quantities as integer thousandths. No decimal/float in storage or calculations.
- Consequences: Rounding rule defined once in Domain and unit-tested.

## ADR-0003: Append-only stock and audit
- Date: 2026-10-19
- Status: Accepted
- Decision: stock_movements and audit_log are append-only (DB triggers). products.stock_milli is maintained only by trigger from movements; a guard trigger blocks direct updates.
- Consequences: Corrections are new reversing movements, never edits.

## ADR-0004: business_date is shop-local
- Date: 2026-10-19
- Status: Accepted
- Decision: Every reporting-relevant row carries business_date ('YYYY-MM-DD', shop-local from shop_timezone setting). Reports group by business_date, never by UTC date().
- Consequences: App must compute business_date at insert time, not DB defaults.

## ADR-0005: Returns are a separate document
- Date: 2026-10-19
- Status: Accepted
- Decision: Partial customer returns are recorded in returns/return_items linked to the original sale_item, not as negative sales. Voids remain a whole-sale reversal on sales.
- Consequences: Stock effects via RETURN_IN movements; refund_method recorded per return.

## ADR-0006: Cash reconciliation covers ledger payments
- Date: 2026-10-19
- Status: Accepted
- Decision: EOD cash = CASH payments on sales + customer_ledger PAYMENT rows with method=CASH + cash_movements. customer_ledger carries cash_session_id.
- Consequences: Report joins three sources; covered by integration tests.

## ADR-0007: Gap-free numbering pattern
- Date: 2026-10-19
- Status: Accepted
- Decision: Number allocation runs inside the same transaction as the document insert: BEGIN IMMEDIATE; UPDATE document_sequences SET next_number = next_number + 1 WHERE doc_type=?; SELECT next_number - 1; INSERT ...; COMMIT.
- Consequences: Rolled-back documents do not consume numbers.

## ADR-0008: Search strategy (open)
- Date: 2026-10-19
- Status: Proposed
- Decision: Evaluate FTS5 virtual table on products(name, sku) with sync triggers vs. LIKE-based search. Must meet <100 ms on 20,000 products.
- Consequences: To be resolved during M2 with measured evidence.

## ADR-0009: Dapper parameter names must match $tokens exactly
- Date: 2026-10-19
- Status: Accepted (lesson)
- Context: Anonymous-type property names must equal the `$name` tokens in SQL (case-sensitive as written). Projected declarators like `new { Id = line.ProductId }` do not satisfy `$ProductId`. Missing bindings throw at execution, not compile time.
- Decision: Spell anon-object members to match every `$name` exactly; never reuse a property for a differently named token. Test coverage catches regressions because transaction tests execute the SQL for real.
- Consequences: Anonymous objects are verbose but explicit; do not shorten at the cost of mismatch.

## ADR-0010: Shop-language labels only
- Date: 2026-10-20
- Status: Accepted
- Context: First-render of Products page used "SKU", "Base unit", "Reorder level", "Avg cost", "Business date", "GRN", "Supplier ref". These are developer terms. Primary users are shop workers and owners, many of whom are not fluent in English business jargon. UI must use words used at the counter.
- Decision: Apply the following translation across all UI text. Database column names stay as-is (internal).
- Locked dictionary:
  | Dev term | UI term |
  |---|---|
  | SKU | Code |
  | Base unit | Sold by |
  | Reorder level | Low stock alert |
  | Avg cost | Cost price |
  | Last cost | Last cost |
  | Price tier | Price type |
  | Tax class | VAT |
  | Business date | Date |
  | Supplier ref | Supplier invoice # |
  | Transaction | Sale |
  | Void | Cancel sale |
  | Archive | Remove from list |
  | Customer ledger | Account |
  | Proforma invoice | Proforma |
  | Diagnostics | System check |
- Preserved terms (shop owner rejected changes; keep as-is):
  | Term |
  |---|
  | Goods received |
  | Opening balance |
  | Quotation |
  | Audit log |
  | Dashboard |
  | POS / Point of Sale |
  | User / Role |
  | GRN (abbreviation of Goods received) |
- Consequences: Any new UI text added later must go through the dictionary. If a term is missing from this table, default to whatever the shop owner would say at the counter, not the schema field name.
