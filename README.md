# Sandwich Orders

Web app for ordering sandwiches at a canteen before a deadline set by the canteen staff.
Customers see menu cards with photo, ingredients and price, choose bread, and order once per day.
No account is needed: a visitor types his name (an account is optional).
Staff see what to prepare, switch sandwiches on/off, set the deadline, and add/edit sandwiches, ingredients and photos.
Payment stays at the canteen. Built for one canteen (Sint-Niklaas); several canteens are a possible future step.

> **Status:** written with AI assistance. GitHub Actions runs on every pull request: build and all tests on SQLite;
> the integration tests again on SQL Server 2022 (database created by the migrations); the Docker image with
> `docker compose` and SQL Server; and a browser check of every page at phone to desktop sizes (layout, live updates,
> accessibility with axe-core). **Not verified:** every screen used by hand on a real phone, a real screen reader.

## Stack
.NET 10, ASP.NET Core MVC (Razor), ASP.NET Core Identity (cookie auth, roles Customer/Staff), EF Core 10
(SQLite by default, SQL Server via configuration), Bootstrap 5, EN/NL localization,
xUnit + NSubstitute + FakeTimeProvider, WebApplicationFactory integration tests, Docker, GitHub Actions.

## Structure
- `src/SandwichOrders.Core` – domain and rules (no EF, no HTTP): `OrderService`, `MenuService`, deadline rules.
- `src/SandwichOrders.Web` – MVC UI, Identity, EF Core repositories, seeding.
- `tests/SandwichOrders.UnitTests` – rules and services.
- `tests/SandwichOrders.IntegrationTests` – real app in memory, temporary SQLite file, controlled clock.

## Run locally
```
dotnet --list-sdks          # needs 10.x
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

## What changed in the stock / announcements update

> **Database:** on SQL Server (Docker, hosting) the schema comes from the EF Core migrations in
> `src/SandwichOrders.Web/Data/Migrations`, so data is kept when the model changes. The local SQLite file is created
> straight from the model: after a model change delete `src/SandwichOrders.Web/sandwich-orders.db*` once.

**Two kinds of sandwiches**
- *Made here* (prepared): stock comes from the ingredients. Each ingredient on a sandwich has a number of
  portions (e.g. 2 slices of cheese). The menu calculates how many can still be made: the scarcest counted
  ingredient decides. Same idea as RecipeMatcher, but with quantities.
- *Supplied* (delivered ready-made): counted per piece.
- An ingredient or supplied sandwich with an **empty** stock field is not counted and never limits ordering.
- Placing an order takes the stock in the same save as the order. A concurrency token (`StockVersion`) makes sure
  two people cannot both take the last portion; the second gets "please try again".
- Sold-out sandwiches are greyed out with "Sold out"; "Only N left" is shown from 5 down.

**Only staff can cancel** — customers have no cancel button and no cancel endpoint.
Staff cancel on *Today's orders*; the stock goes back.

**Order status** — staff press *Ready* and *Collected*; the customer sees "Your sandwich is ready" on *My order*.
This replaces WhatsApp notifications (no paid API, no consent for messages needed).

**Contact** (from ContactManager) — registration has an optional phone number. It is copied into the order and
shown to staff as a `tel:` link. Guests have no phone number.

**Announcements** (*Staff → Announcements*) — one record that changes role by itself:
shown from `ShowFrom`, informs until `PeriodStart`, blocks ordering from `PeriodStart` to `PeriodEnd` (inclusive)
when "Block ordering" is ticked, then disappears. An optional second text is shown during the period;
when it is empty the normal text is used. One language is enough; the other falls back to it.

## Rules implemented
- Orders are accepted strictly before the deadline (09:29:59 open, 09:30:00 closed), Brussels time, DST-aware.
- One active order per customer per day. Only the canteen employee cancels (any time before collection).
- No orders on a day blocked by an announcement.
- Unavailable sandwiches (switched off, an ingredient switched off, or no stock left) cannot be ordered.
- The price is copied into the order line when ordering.

## Not built yet
Several canteens, per-day deadlines, admin user management.
