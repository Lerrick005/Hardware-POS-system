# 🛠️ Hardware-POS-System

> A robust C# desktop application designed to manage the daily retail operations, inventory, sales transactions, and customer accounts of a hardware or general retail store.

![C#](https://img.shields.io/badge/C%23-99.5%25-239120?logo=c-sharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-Desktop-512BD4?logo=dotnet&logoColor=white)
![SQLite](https://img.shields.io/badge/SQLite-Database-003B57?logo=sqlite&logoColor=white)
![Dapper](https://img.shields.io/badge/Dapper-ORM-blue)
![Architecture](https://img.shields.io/badge/Architecture-Clean-green)

## 📖 Overview

The **Hardware-POS-System** is a comprehensive desktop Point of Sale solution tailored for hardware stores. It moves beyond basic checkout by incorporating deep inventory management, customer debt tracking, and detailed reporting.

Built with modern C# practices, the solution employs a strict Clean Architecture approach. It utilizes MVVM for a responsive UI, SQLite for offline-first data reliability, and Dapper for high-performance database interactions.

## ✨ Key Features

*   **Inventory & Stock Management:** Track product catalogs, manage opening stock, and handle goods receiving.
*   **Advanced Reporting:** Tabbed UI for generating critical business insights, including Sales, Stock, Debtors, Profit Summaries, Product Profitability, and Dead Stock Analysis.
*   **Data Export:** Export reports seamlessly to Excel (via `ClosedXML`) and CSV (via `CsvHelper`).
*   **Customer & Account Management:** Dedicated repository and account service to track customer balances and payments.
*   **Licensing:** Includes a standalone `Pos.LicenseTool` for managing software licensing and activation.
*   **Robust Logging:** Integrated with `Serilog` for comprehensive application logging.

## 🏗️ Architecture & Tech Stack

This project utilizes a **Clean Architecture** approach, split across a 9-project solution to maintain a strict dependency chain and separation of concerns.

*   **Language:** C# (.NET)
*   **Database:** SQLite
*   **ORM:** Dapper
*   **UI:** MVVM (Model-View-ViewModel)
*   **Libraries:** `ClosedXML`, `CsvHelper`, `Serilog`, `.NET Generic Host`

### Solution Structure

```text
Hardware-POS-system/
├── src/
│   ├── Pos.Domain/          # Core entities, interfaces, and business rules
│   ├── Pos.Application/     # Use cases, business logic, and service interfaces
│   ├── Pos.Infrastructure/  # Data access (Dapper/SQLite), external services
│   └── Pos.Desktop/         # MVVM Desktop UI application
├── tests/
│   ├── Pos.Domain.Tests/
│   ├── Pos.Application.Tests/
│   ├── Pos.Infrastructure.Tests/
│   └── Pos.UiSmoke.Tests/   # 16+ tests passing across all suites
├── tools/
│   └── Pos.LicenseTool/     # Standalone utility for license generation
├── scripts/                 # Database and deployment scripts
├── docs/                    # Project documentation
├── Directory.Build.props    # Global build properties and package management
├── build.ps1                # PowerShell build automation script
└── pos_schema.sql           # Initial database schema (v1.1)
