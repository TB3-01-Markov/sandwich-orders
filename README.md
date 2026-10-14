# Sandwich Orders

Web app for ordering sandwiches at a canteen before a deadline set by the canteen staff.
Customers see menu cards with photo, ingredients and price, choose bread, and order once per day.
No account is needed: a visitor types his name (an account is optional).
Staff see what to prepare, switch sandwiches on/off, set the deadline, and add/edit sandwiches, ingredients and photos.
Payment stays at the canteen. Built for one canteen (Sint-Niklaas); several canteens are a possible future step.

> **Status: written with AI assistance, NOT yet compiled or run.**
> The code and tests were produced in an environment without the .NET SDK. Run the commands below and fix what fails
> before relying on anything in this README.

## Stack
.NET 8, ASP.NET Core MVC (Razor), ASP.NET Core Identity (cookie auth, roles Customer/Staff), EF Core 8
(SQLite by default, SQL Server via configuration), Bootstrap 5, EN/NL localization,
xUnit + NSubstitute + FakeTimeProvider, WebApplicationFactory integration tests, Docker, GitHub Actions.

## Structure
- `src/SandwichOrders.Core` – domain and rules (no EF, no HTTP): `OrderService`, `MenuService`, deadline rules.
- `src/SandwichOrders.Web` – MVC UI, Identity, EF Core repositories, seeding.
- `tests/SandwichOrders.UnitTests` – rules and services.
- `tests/SandwichOrders.IntegrationTests` – real app in memory, temporary SQLite file, controlled clock.

## Run locally
```
dotnet --list-sdks          # needs 8.x
dotnet build
dotnet test
dotnet run --project src/SandwichOrders.Web
```
Open http://localhost:5080. In Development, demo data is seeded and a staff account exists:
`staff@example.test` / `Staff-Demo-1234` (development only, never use it elsewhere).

## Docker (SQL Server)
```
cp .env.example .env     # then edit the passwords
docker compose up --build
```
Open http://localhost:8080.

## Staff (canteen employee)
Sign in (Development: `staff@example.test` / `Staff-Demo-1234`). Menu: Today's orders, Manage menu, Deadline.
Photos (JPG/PNG/WebP, max 2 MB, checked by file signature) are stored in `Uploads:Path`
(default `src/SandwichOrders.Web/uploads`; in Docker a volume at `/data/uploads`).

## Rules implemented
- Orders are accepted strictly before the deadline (09:29:59 open, 09:30:00 closed), Brussels time, DST-aware.
- One active order per customer per day; cancelling is possible until the deadline.
- Unavailable sandwiches (switched off, or with an unavailable ingredient) cannot be ordered.
- The price is copied into the order line when ordering.

## Not built yet
Several canteens, per-day deadlines, EF migrations (uses `EnsureCreated`), order history, admin user management.
