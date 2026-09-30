# 🎮 GameRune Backend

> ASP.NET Core 10 minimal-API backend for GameRune — game discovery, details aggregation, and pricing. Proxies and normalizes data from [RAWG](https://rawg.io/apidocs) and [Steam Store](https://store.steampowered.com/) for the GameRune frontend.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-Minimal_API-512BD4?style=flat-square)](https://learn.microsoft.com/aspnet/core/)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?style=flat-square&logo=docker)](./Dockerfile)
[![Render](https://img.shields.io/badge/Render-deploy-46E3B7?style=flat-square&logo=render)](./render.yaml)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](./LICENSE)

**Live API:** `https://gamerune-backend.onrender.com`
**Frontend:** `https://gamerune.vercel.app`
**Swagger (dev):** `http://localhost:5054/swagger`

---

## 📑 Table of Contents

- [Features](#-features)
- [Architecture](#-architecture)
- [Tech Stack](#-tech-stack)
- [API Reference](#-api-reference)
- [Pagination](#-pagination)
- [Database](#️-database)
- [Getting Started](#-getting-started)
- [Configuration](#-configuration)
- [Running with Docker](#-running-with-docker)
- [Deployment](#️-deployment-render)
- [Project Structure](#-project-structure)
- [Rate Limiting & CORS](#️-rate-limiting--cors)
- [Contributing](#-contributing)

---

## ✨ Features

- 📃 **Game listing** — paginated `GET /games` backed by RAWG, normalized to a lean `GameDto`.
- 🔍 **Search + hydrate** — `GET /games/search/details` searches RAWG then hydrates each hit in parallel into a full detail object (description, screenshots, Steam price).
- 📄 **Game details aggregation** — `GET /games/{id}` merges in one response:
  - RAWG detail (`description`, `released`, `rating`)
  - Up to 3 images (cover + screenshots)
  - Steam `appId` resolved via RAWG `/stores` endpoint
  - Live Steam price via `store.steampowered.com/api/appdetails` with `cc={countryCode}`
  - Free-to-play handling (`FinalFormatted: "Free"`)
- 📄 **Pagination** — `page` / `pageSize` forwarded to RAWG as `page` / `page_size`, with `totalPages`, `hasNext` / `hasPrevious`, and backend-relative `next` / `previous` links (no API-key leakage).
- 👤 **Auth + users** — `POST /auth/register`, `POST /auth/login` (PBKDF2 password hashing, JWT Bearer).
- ⭐ **Favorites library** — `GET /me/favorites`, `POST /me/favorites`, `DELETE /me/favorites/{rawgId}` with `wishlist|favorite|owned|playing|completed` statuses.
- 📝 **Reviews** — `GET /games/{id}/reviews`, `POST /games/{id}/reviews` (score 1–5, one per user per game).
- 💾 **RAWG cache** — `GET /games/{id}` auto-upserts into Postgres (`Games` table); `POST /games/{id}/cache` for explicit caching.
- ⏰ **Price history + alerts** — every detail view records Steam prices; `GET /games/{id}/price-history`, `POST /games/{id}/alerts`, background `PriceSyncWorker` refreshes prices and flips alerts when the target is hit.
- 🛡️ **Rate limiting** — fixed-window `GameApi` policy (default 60 req/min, configurable).
- 🌐 **CORS** — allow-listed for local dev ports + `https://gamerune.vercel.app` + `https://gamerune.janbaas.me`.
- 📘 **OpenAPI / Swagger** — Swashbuckle + built-in OpenAPI in Development.
- 🔑 **Flexible API-key loading** — `RAWG_API_KEY` env var → `.env` file → `appsettings.json` (`Rawg:ApiKey`).
- 🐳 **Docker + Render ready** — multi-stage Dockerfile, `PORT`-aware startup, `render.yaml` blueprint.

---

## 🏗️ Architecture

```text
                    ┌──────────────────┐
                    │ GameRune Frontend│
                    │  (Vercel / SPA)  │
                    └────────┬─────────┘
                             │ HTTPS / CORS
                             ▼
                    ┌──────────────────┐
                    │ GameRune Backend │  ← this repo (ASP.NET Core 10)
                    │  Minimal API     │
                    │  Rate Limiter    │
                    └────┬───────┬─────┘
                         │       │
              ┌──────────┘       └──────────┐
              ▼                             ▼
    ┌──────────────────┐          ┌──────────────────┐
    │   RAWG API       │          │  Steam Store API │
    │  games, details, │          │  appdetails +    │
    │  screenshots,    │          │  price_overview  │
    │  stores          │          │                  │
    └──────────────────┘          └──────────────────┘
```

The API proxies RAWG + Steam live and persists users, library, reviews, game cache, and price data in Postgres. RAWG-facing reads stay usable without a DB configured, but auth/library/review/price endpoints require it.

Request flow for `GET /games/{id}`:

1. Validate `id` → look up `RAWG_API_KEY`.
2. `GET https://api.rawg.io/api/games/{id}?key=...` → base detail.
3. Parallel: `GET .../games/{id}/stores` → extract Steam `appId` from `store.steampowered.com/app/{appId}/...` URL.
4. Parallel: `GET .../games/{id}/screenshots` → take cover + screenshots, `Distinct().Take(3)`.
5. If `appId` found: `GET https://store.steampowered.com/api/appdetails?appids={id}&cc={countryCode}&filters=basic,price_overview`.
6. Return unified `GameDetailDto`.

---

## 🧰 Tech Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 / ASP.NET Core Minimal APIs |
| Docs | Swashbuckle + Microsoft.AspNetCore.OpenApi |
| HTTP | `IHttpClientFactory` named clients (`Rawg`, `Steam`) |
| DB | PostgreSQL + Entity Framework Core (`Npgsql.EntityFrameworkCore.PostgreSQL`, auto-migrate on startup) |
| Auth | PBKDF2 password hashing + JWT Bearer |
| Hosting | Docker (multi-stage) → Render Web Service |
| Frontend | Separate repo, deployed on Vercel |

---

## 📡 API Reference

Base URL (local): `http://localhost:5054`

| Method | Endpoint | Description | Rate limited |
|---|---|---|---|
| `GET` | `/games?page=1&pageSize=20&search=` | Paginated game list | ✅ `GameApi` |
| `GET` | `/games/search/details?query=elden+ring&page=1&pageSize=5&countryCode=US` | Search + hydrated details | ✅ `GameApi` |
| `GET` | `/games/{id}?countryCode=US` | Full detail by RAWG id or slug | ✅ `GameApi` |
| `POST` | `/auth/register` | Register `{username, email, password}` → `{token, user}` | ❌ |
| `POST` | `/auth/login` | Login `{usernameOrEmail, password}` → `{token, user}` | ❌ |
| `GET` | `/me` | Current profile (Bearer) | ❌ |
| `GET` | `/me/favorites?status=` | Library (Bearer) | ❌ |
| `POST` | `/me/favorites` | Upsert `{rawgId, status}` (Bearer) | ❌ |
| `DELETE` | `/me/favorites/{rawgId}` | Remove (Bearer) | ❌ |
| `GET` | `/games/{id}/reviews` | List reviews | ❌ |
| `POST` | `/games/{id}/reviews` | Upsert `{score 1-5, body}` (Bearer) | ❌ |
| `GET` | `/games/{id}/price-history?countryCode=US` | Price points, newest first | ❌ |
| `POST` | `/games/{id}/alerts` | Create `{targetCents}` alert (Bearer) | ❌ |
| `POST` | `/games/{id}/cache` | Force RAWG → Postgres caching | ❌ |

### `GET /games`

```http
GET /games?page=1&pageSize=20&search=witcher HTTP/1.1
Host: localhost:5054
```

```json
{
  "count": 10234,
  "page": 1,
  "pageSize": 20,
  "totalPages": 512,
  "hasNext": true,
  "hasPrevious": false,
  "next": "/games?page=2&pageSize=20&search=witcher",
  "previous": null,
  "results": [
    { "id": 3328, "name": "The Witcher 3: Wild Hunt", "imageUrl": "https://media.rawg.io/..." }
  ]
}
```

Query params: `page >= 1` (default `1`), `pageSize 1–40` (default `20`), `search` optional.

### `GET /games/search/details`

```http
GET /games/search/details?query=elden%20ring&page=1&pageSize=5&countryCode=US HTTP/1.1
Host: localhost:5054
```

```json
{
  "count": 42,
  "page": 1,
  "pageSize": 5,
  "totalPages": 9,
  "hasNext": true,
  "hasPrevious": false,
  "next": "/games/search/details?query=elden%20ring&page=2&pageSize=5&countryCode=US",
  "previous": null,
  "results": [
    {
      "id": 3498,
      "name": "Elden Ring",
      "imageUrl": "https://media.rawg.io/...",
      "imageUrls": ["https://media.rawg.io/...", "...", "..."],
      "description": "...",
      "released": "2022-02-25",
      "rating": 4.65,
      "steamAppId": 1245620,
      "steamPrice": { "currency": "USD", "initial": 5999, "final": 3599, "discountPercent": 40, "initialFormatted": "$59.99", "finalFormatted": "$35.99" }
    }
  ]
}
```

Query params: `query` (required), `page >= 1` (default `1`), `pageSize 1–10` (default `5`, capped because each hit fans out to detail + stores + screenshots + Steam calls hydrated in parallel), `countryCode` (default `US`).

### `GET /games/{id}`

```http
GET /games/3328?countryCode=US HTTP/1.1
Host: localhost:5054
```

```json
{
  "id": 3328,
  "name": "The Witcher 3: Wild Hunt",
  "imageUrl": "https://media.rawg.io/...",
  "imageUrls": [
    "https://media.rawg.io/...",
    "https://media.rawg.io/...",
    "https://media.rawg.io/..."
  ],
  "description": "You are Geralt of Rivia...",
  "released": "2015-05-18",
  "rating": 4.67,
  "steamAppId": 292030,
  "steamPrice": {
    "currency": "USD",
    "initial": 3999,
    "final": 999,
    "discountPercent": 75,
    "initialFormatted": "$39.99",
    "finalFormatted": "$9.99"
  }
}
```

- `id` accepts a RAWG numeric id (`3328`) or slug (`the-witcher-3-wild-hunt`).
- `countryCode` is an ISO-2 Steam store code (`US`, `GB`, `DE`, …), default `US`.
- `steamAppId` / `steamPrice` are `null` when no Steam store link exists or Steam returns `success: false`.
- Free games return `"finalFormatted": "Free"` with `initial: 0, final: 0`.

### Error shapes

| Status | When |
|---|---|
| `400` | missing `query` / `id` → `{ "message": "Search query is required." }` |
| `401` | missing/invalid JWT on `/me/*`, review POST, alert POST |
| `404` | RAWG game not found → `{ "message": "Game was not found." }` |
| `409` | duplicate username/email on register |
| `429` | `GameApi` fixed-window exceeded |
| `500` | `RAWG_API_KEY` not configured |
| `502` | upstream RAWG request failed |

---

## 🗄️ Database

PostgreSQL (Render Postgres in production, local Docker for dev) via EF Core. Schema lives in `Data/` (`GameRuneDbContext` + `Data/Migrations`); the app runs `Database.Migrate()` on startup so Render deploys need no manual step.

Tables: `Users`, `Games` (RAWG cache: `RawgId` PK, `Slug` unique, cover + `ImageUrls[]`, `SteamAppId`, payload, `CachedAt/UpdatedAt`), `Favorites` (composite PK, `wishlist|favorite|owned|playing|completed` check), `Reviews` (one per user per game, `Score 1–5` check), `PriceHistory` (`GameId, CapturedAt` index), `PriceAlerts` (flipped to `Triggered` by the detail path and the worker).

Connection resolution (`Configuration/Database.cs`): `DATABASE_URL` (Render) → `ConnectionStrings:Default` (`ConnectionStrings__Default` env). Local default: `Host=localhost;Database=gamerune;Username=postgres;Password=postgres`.

```bash
# local Postgres
docker run --rm -p 5432:5432 -e POSTGRES_DB=gamerune -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres postgres:16

# new migration after entity changes
dotnet ef migrations add <Name> --project "..csproj" --output-dir Data/Migrations
```

Auth: `Jwt:Key` (or `JWT_KEY`, min 32 chars) + `Jwt:Issuer/Audience/ExpiryMinutes`. Dev has a dummy key in `appsettings.Development.json`; production must set `JWT_KEY` (see `render.yaml`, `sync: false`).

Price sync: `PriceSync:Enabled` (default `true`, `false` in Development) + `PriceSync:IntervalHours` (default `6`). `PriceSyncWorker` re-fetches Steam prices for cached games and triggers alerts.

---

## 📄 Pagination

Both list endpoints forward pagination to RAWG and return a uniform envelope:

| Field | Meaning |
|---|---|
| `count` | Total matches reported by RAWG |
| `page` | Current 1-based page (clamped to `>= 1`) |
| `pageSize` | Applied page size (`/games`: `1–40`, `/games/search/details`: `1–10`) |
| `totalPages` | `ceil(count / pageSize)`, `0` when `count` is `0` |
| `hasNext` / `hasPrevious` | Booleans for rendering pager controls |
| `next` / `previous` | Backend-relative links (`/games?...`), `null` at the bounds — never RAWG URLs, so the RAWG key is never exposed |
| `results` | Current page items |

RAWG mapping:

```text
/games?page=2&pageSize=20  →  GET https://api.rawg.io/api/games?key=...&page=2&page_size=20
/games/search/details?query=x&page=1&pageSize=5 → GET .../games?key=...&search=x&page=1&page_size=5
```

Frontend usage:

```text
1. Render `totalPages` pages, disable Prev when `!hasPrevious`, Next when `!hasNext`.
2. Follow `next` / `previous` as-is, or build `?page=N&pageSize=M` yourself.
3. Keep `pageSize` stable while paging so `totalPages` stays consistent.
```

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` → `10.x`)
- A free [RAWG API key](https://rawg.io/apidocs)
- Optional: Docker Desktop

### 1. Clone

```bash
git clone git@github.com:Penquinz01/gamerune-backend.git
cd gamerune-backend
```

### 2. Configure key

```bash
cp .env.example .env
# then edit .env:
# RAWG_API_KEY=your_key_here
```

Priority order (see `Configuration/ApiKeys.cs`): **environment variable** `RAWG_API_KEY` → `.env` file (`Configuration/EnvFile.cs`) → `appsettings.json` (`Rawg:ApiKey`).

### 3. Run

```bash
dotnet restore
dotnet run
# Swagger: http://localhost:5054/swagger  (dev)
```

Quick smoke test:

```bash
curl "http://localhost:5054/games?page=1&pageSize=5"
curl "http://localhost:5054/games/3328?countryCode=US"
curl "http://localhost:5054/games/search/details?query=elden%20ring&pageSize=2"
```

---

## ⚙️ Configuration

| Key | Source | Default | Description |
|---|---|---|---|
| `RAWG_API_KEY` | env / `.env` / `Rawg:ApiKey` | — (required) | RAWG API key |
| `ConnectionStrings__Default` / `DATABASE_URL` | env / `.env` | local Postgres default | Postgres connection (Render injects it) |
| `JWT_KEY` / `Jwt:Key` | env / `.env` | dev dummy only | JWT signing key, min 32 chars (required in prod) |
| `Cors:AllowedOrigins` | `appsettings.json` | `localhost:3000,5173,4200` | Frontend origins |
| `RateLimiting:GameApi:PermitLimit` | `appsettings.json` | `60` | Requests per window |
| `RateLimiting:GameApi:WindowSeconds` | `appsettings.json` | `60` | Window size (s) |
| `RateLimiting:GameApi:QueueLimit` | `appsettings.json` | `0` | Queued requests |
| `PORT` | Render / env | `5054` (dev) | Listening port (`Program.cs` binds `0.0.0.0:$PORT`) |

Production overrides use environment variables with `__` nesting, e.g. `RateLimiting__GameApi__PermitLimit=120`.

---

## 🐳 Running with Docker

```bash
# Build
docker build -t gamerune-backend .

# Run (passes RAWG key through)
docker run --rm -p 10000:10000 \
  -e PORT=10000 \
  -e RAWG_API_KEY=your_key_here \
  gamerune-backend

curl "http://localhost:10000/games?pageSize=3"
```

The image is multi-stage (`sdk:10.0` → `aspnet:10.0`), exposes `10000`, and runs `GameListerBackend.dll` with `ASPNETCORE_ENVIRONMENT=Production`.

---

## ☁️ Deployment (Render)

`render.yaml` defines a Docker web service:

```yaml
databases:
  - name: gamerune-db
    plan: free
    databaseName: gamerune
    user: gamerune

services:
  - type: web
    name: game-lister-backend
    runtime: docker
    plan: free
    envVars:
      - key: ASPNETCORE_ENVIRONMENT
        value: Production
      - key: RAWG_API_KEY
        sync: false   # set in Render dashboard
      - key: JWT_KEY
        sync: false   # set in Render dashboard (min 32 chars)
      - key: ConnectionStrings__Default
        fromDatabase:
          name: gamerune-db
          property: connectionString
```

Steps:

1. Push to GitHub.
2. Render → **New → Blueprint** → select repo (picks up `render.yaml` — provisions web service + Postgres).
3. Set `RAWG_API_KEY` and `JWT_KEY` in the Render dashboard (sync: false means manual).
4. Deploy. Migrations run automatically on startup.

---

## 📁 Project Structure

```text
gamerune-backend/
├── Program.cs                  # Minimal API: /games, /games/search/details, /games/{id} + helpers
├── Configuration/
│   ├── ApiKeys.cs              # Env → appsettings key resolution (RAWG_API_KEY)
│   └── EnvFile.cs              # Lightweight .env loader (dev convenience)
├── Models/
│   ├── GameDto.cs              # List item { id, name, imageUrl }
│   ├── GamesResponse.cs        # Paginated list envelope
│   ├── GameDetailDto.cs        # Aggregated detail + Steam price
│   ├── GameDetailSearchResponse.cs
│   ├── RawgGamesResponse.cs    # RAWG list shape
│   ├── RawgGameDetail.cs       # RAWG detail shape
│   ├── RawgGameStoresResponse.cs
│   ├── RawgScreenshotsResponse.cs
│   └── SteamAppDetailsResponse.cs
├── Data/                       # EF Core: entities + GameRuneDbContext + Migrations
├── Services/
│   ├── GameRuneAuth.cs         # PBKDF2 hashing + JWT issuance
│   └── PriceSyncWorker.cs      # Background Steam price refresh + alert triggers
├── Features/
│   └── DbEndpoints.cs          # Auth, favorites, reviews, price-history, alerts, cache
├── Dockerfile                  # Multi-stage .NET 10 build
├── render.yaml                 # Render blueprint
├── appsettings.json
├── appsettings.Development.json
├── .env.example
└── README.md
```

---

## ⏱️ Rate Limiting & CORS

- **Rate limiting:** `AddFixedWindowLimiter("GameApi")` — 60 req / 60 s by default, `RejectionStatusCode: 429`, no queue. Tune via `RateLimiting:GameApi` in config.
- **CORS:** named policy `Frontend`, `AllowAnyHeader + AllowAnyMethod`, origins from `Cors:AllowedOrigins`. Add your production Vercel URL there or via env: `Cors__AllowedOrigins__3=https://gamerune.vercel.app`.

---

## 🤝 Contributing

1. Fork → branch (`feat/…`, `fix/…`).
2. `dotnet format` + `dotnet build`.
3. Open a PR describing the endpoint / schema change with a `curl` example.

---

## 📄 License

MIT — see [LICENSE](./LICENSE) *(add one if missing: `dotnet new gitignore` already covers build artifacts)*.

## 🙏 Credits

- Game metadata: [RAWG](https://rawg.io/)
- Prices: [Steam Store API](https://store.steampowered.com/)
- Built with [ASP.NET Core](https://learn.microsoft.com/aspnet/core/) 10
