# Requirements Ledger

Status values: TODO, IN_PROGRESS, DONE, BLOCKED-HUMAN, DEFERRED-approved.
Evidence is mandatory for DONE: test name + passing run, or command + output, or file + behaviour.
Never delete rows. New requirements get an X- prefix.

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
| M1-09 | Serilog configuration to rolling file | TODO | |
| M1-10 | App shell + DI composition root + navigation | TODO | |
