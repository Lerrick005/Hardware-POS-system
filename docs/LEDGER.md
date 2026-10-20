# Requirements Ledger

Status values: TODO, IN_PROGRESS, DONE, BLOCKED-HUMAN, DEFERRED-approved.
Evidence is mandatory for DONE: test name + passing run, or command + output, or file + behaviour.
Never delete rows. New requirements get an X- prefix.

## M2 Catalog and Stock

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M2-01 | Product/Unit/Category/PriceTier entities + Dapper repositories | DONE | Pos.Infrastructure.Tests: ProductRepositoryTests 7/7; build green |
| M2-02 | Search matches all tokens in any order, excludes archived | DONE | Search_MatchesAllTokensInAnyOrder, Search_ExcludesArchived pass |
| M2-03 | Barcode lookup and duplicate check | DONE | Barcode_RoundTrips passes |
| M2-04 | Search < 100 ms on 20,000 products | DONE | Measured 29 ms (cement), 43 ms (product 12) — 20k rows, LIKE-based |

## M1 Foundation

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M1-01 | Solution with 9 projects (Domain/Application/Infrastructure/Desktop/LicenseTool + 4 test projects) | DONE | commit 51c4dd6; `dotnet build Pos.slnx` succeeded |
| M1-02 | Dependency direction Domain<-Application<-Infrastructure<-Desktop | DONE | ProjectReference chain; build green |
| M1-03 | Strict build: Nullable, ImplicitUsings, TreatWarningsAsErrors | DONE | Directory.Build.props; build green |
| M1-04 | SQLite schema v1.1 with append-only stock/audit + stock guard | DONE | C:\ProgramData\MyPOS\pos.db; integrity_check=ok; guard test aborted as expected |
| M1-05 | Packages: Sqlite, Dapper, Serilog(+File), ClosedXML, CsvHelper, MVVM, Hosting, DI | DONE | csproj PackageReferences; build green |
| M1-06 | Money + Quantity value objects (integer cents / milli) with unit tests | DONE | Pos.Domain.Tests: 20/20 passed (MoneyTests, QuantityTests) |
| M1-07 | DbConnectionFactory applying PRAGMAs on every connection | DONE | Pos.Infrastructure.Tests: 4/4 passed (DbConnectionFactoryTests) |
| M1-08 | Migration runner (forward-only, backup before migrate) + BackupService | DONE | Pos.Infrastructure.Tests: 9/9 passed (DbConnectionFactoryTests + MigrationRunnerTests) |
| M1-09 | Serilog configuration to rolling file (daily, 14 retained) | DONE | Log at C:\ProgramData\MyPOS\logs\pos-*.log shows "MyPOS starting" and "MyPOS exiting" |
| M1-10 | App shell + DI composition root (migration on startup) | DONE | `dotnet run --project src/Pos.Desktop` boots; log shows Migration UpToDate 1.1->1.1 |

## M2 Stock

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M2-05 | GoodsReceipt / GoodsReceiptLine entities + WeightedAverageCost | DONE | Pos.Domain.Tests 29/29 (9 WeightedAverageCost tests) |
| M2-06 | ReceiveGoods use case: one transaction covering GRN + lines + PURCHASE movements + avg-cost update | DONE | ReceiveGoodsServiceTests 10/10 (happy, weighted avg, opening-zero, missing product rollback, duplicate line, empty, bad date, zero qty, negative cost, sequential GRNs) |
| M2-07 | GRN numbering gap-free on success, rollback-safe | DONE | Execute_GrnNumbersAreSequentialAndGapFree_OnSuccess: GRN-000001/2/3 |
| M2-08 | Dapper param-name must match $tokens exactly (lesson) | DONE | ADR-0009 |

## M2 Add Product (UI)

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M2-09 | Add Product dialog: name required, unit required, optional SKU/barcode/category/reorder | DONE | dotnet run --project src/Pos.Desktop; product "CEMENT" persisted; products query returns it |
| M2-10 | Duplicate-barcode check before insert | DONE | AddProductViewModel.SaveAsync pre-checks BarcodeExistsAsync |
| M2-11 | Product + barcode insert is one transaction | DONE | InsertWithBarcodeAsync; ProductRepositoryTransactionTests 4/4 prove no orphan on barcode/uuid failure |
| M2-12 | Edit-in-place later (update/archive via UI) | TODO | Repository methods exist; no UI yet |

## M2 Add Product (UI)

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M2-09 | Add Product dialog: name required, unit required, optional SKU/barcode/category/reorder | DONE | dotnet run --project src/Pos.Desktop; product "CEMENT" persisted; products query returns it |
| M2-10 | Duplicate-barcode check before insert | DONE | AddProductViewModel.SaveAsync pre-checks BarcodeExistsAsync |
| M2-11 | Product + barcode insert is one transaction | DONE | InsertWithBarcodeAsync; ProductRepositoryTransactionTests 4/4 prove no orphan on barcode/uuid failure |
| M2-12 | Edit-in-place later (update/archive via UI) | TODO | Repository methods exist; no UI yet |

## M2 Receive Goods (list)

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M2-13 | IReceiveGoodsQuery (list + detail) | DONE | Application interface + ReceiveGoodsQuery impl; build green |
| M2-14 | Receive Goods list view (GRN, date, supplier, lines, total) | DONE | dotnet run confirmed; empty state renders; Stock nav routes correctly |
| M2-15 | New receipt compose dialog | TODO | next slice |
| M2-16 | Supplier directory + picker | TODO | schema has suppliers; no repo/UI yet |

## M3 Customers (CRUD-complete per ADR-0012)

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M3-01 | Customer entity + LedgerEntry domain | DONE | Pos.Domain.Customers; builds green |
| M3-02 | ICustomerRepository + Dapper impl | DONE | CustomerRepositoryTests 8/8; CustomerRepositoryAtomicityTests 5/5 |
| M3-03 | ICustomerAccountService + impl | DONE | CustomerAccountServiceTests 8/8; payments validated, method checked, atomic |
| M3-04 | Customer list with search + debtors filter | DONE | CustomersView renders; real DB query; dots in StatusText show total owed |
| M3-05 | Add customer with opening balance (atomic) | DONE | Mary created via UI; opening balance ledger entry written; atomicity proven by 2 test cases |
| M3-06 | Edit customer | DONE | Mary's phone changed 0722000111 -> 0733000222 in DB |
| M3-07 | Remove customer (archive, with balance guard) | DONE | Mary's balance 0 after payment; Remove -> is_active = 0 confirmed; guard proved earlier (blocked while 12,500 owed) |
| M3-08 | Record payment (cash, methods, M-Pesa reference) | DONE | Ledger shows PAYMENT -1,250,000 CASH; audit CUSTOMER_PAYMENT logged |
| M3-09 | ConfirmDialog reusable component | DONE | Used for archive guard + archive confirm; available to all future pages |
| M3-10 | Statement view (running balance, printable) | TODO | last remaining Customers item |
| M3-11 | Audit entity_type consistency (currently 'customer' vs 'customer_ledger') | TODO | fix in batch with Products audit cleanup |

## M3 Customers (CRUD+ complete)

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M3-12 | Customer statement with running balance | DONE | Mary's statement rendered 2 rows: OPENING +12,500 ? balance 12,500, PAYMENT -12,500 ? balance 0; verified in UI |
| M3-13 | Print statement | TODO | Button disabled with honest tooltip; needs printer service |
| M3-14 | Restore archived customer | TODO | Soft delete exists; restore UI does not; workaround is raw SQL (not acceptable for shop owner) |
| M3-15 | Export statement to PDF / CSV | TODO | Needed for accountant/tax |

## Debt — Category management

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| DEBT-CAT-01 | Seed standard hardware categories on first install (migration 002) | IN_PROGRESS | Seed pending owner approval of category names |
| DEBT-CAT-02 | Category management dialog: add, rename, archive | TODO | Blocks populating Category field from UI; workaround is SQL |
| DEBT-CAT-03 | Category filter on Products page | TODO | Requires DEBT-CAT-02 first |

## M3 Products (CRUD+ complete, per ADR-0012/0013)

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M3-16 | Product list shows Retail price in shillings | DONE | DB shows 68000 cents = KES 680; grid renders via CentsToShillingsConverter |
| M3-17 | Add/Edit product with three tier prices in one transaction | DONE | product_prices has 3 rows for product id=3 (Retail 680, Wholesale 620, Contractor 600) |
| M3-18 | Archive product with confirmation | DONE | ConfirmDialog.CreateDanger wired; archive sets is_active=0 |
| M3-19 | Categories management: add, rename, archive, with product-count guard | DONE | CategoriesView renders; CategoryWithCount DTO; archive blocked when products assigned |
| M3-20 | Category dropdown populated from working data source | DONE | Products form loads categories from repo; seed migration applied |
| M3-21 | Customer credit limit and balance columns show shillings | DONE | CentsToShillingsConverter applied to both columns |

## M4 Sell (in progress)

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| M4-01 | Sale / SaleLine / Payment domain entities | TODO | starting now |
| M4-02 | ISellService (atomic sale completion) | TODO | |
| M4-03 | Sale service with stock movements + credit ledger | TODO | |
| M4-04 | Sell screen with product search, cart, price tier | TODO | |
| M4-05 | Payment: cash with change, M-Pesa code, credit, split | TODO | |
| M4-06 | Receipt view + print stub | TODO | |
| M4-07 | Cash session link (open/close deferred to M4b) | TODO | |
