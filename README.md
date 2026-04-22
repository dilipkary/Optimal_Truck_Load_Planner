# SmartLoad Optimization API (.NET 10)

Stateless ASP.NET Core API that selects the optimal compatible set of orders for a truck.

Optimization target:
- Maximize total payout in integer cents.
- Respect truck weight and volume capacity.
- Enforce compatibility constraints (lane, hazmat isolation, and time-window overlap rule).

## Tech Stack

- C# / ASP.NET Core Minimal API
- .NET 10
- xUnit integration tests
- Docker + Docker Compose

## What Is Implemented

### Core requirements

- Stateless REST API (no database).
- POST /api/v1/load-optimizer/optimize implemented.
- Input validation with proper error responses.
- Capacity constraints:
  - total_weight_lbs <= truck.max_weight_lbs
  - total_volume_cuft <= truck.max_volume_cuft
- Compatibility constraints:
  - same lane grouping (origin + destination)
  - hazmat isolation (hazmat and non-hazmat are not mixed)
  - time-window overlap rule: max(pickup_date) <= min(delivery_date)
- Money handled as 64-bit integer cents (long), no floating point money math.

### API and error handling

- 200 on successful optimization requests.
- 400 on invalid payloads (missing/invalid fields, invalid date window, duplicates, etc.).
- 413 for oversized payloads.
- Health endpoint: GET /healthz.

### Performance-oriented implementation

- Exact branch-and-bound search with pruning for optimal payout.
- Request-size guard and in-memory response caching for repeated identical requests.
- Supports up to 22 orders per request via validation.

### Bonus implemented

- Pareto endpoint: POST /api/v1/load-optimizer/pareto
  - returns non-dominated solutions across payout and average utilization
  - sorted by total_payout_cents descending

## Project Layout

- SmartLoad.Api: API and optimizer implementation
- SmartLoad.Tests: API integration tests
- sample-request.json: sample request payload
- Dockerfile and docker-compose.yml: containerized run

## Prerequisites

- Docker Desktop (for containerized run)
- .NET SDK 10.0+ (for local run and tests)

## Run With Docker

From repository root:

```bash
docker compose up --build -d
```

Service URL:

- http://localhost:8080

Stop service:

```bash
docker compose down
```

If port 8080 is already in use, find and stop the conflicting container/process first, then start again:

```bash
docker ps --format 'table {{.ID}}\t{{.Names}}\t{{.Ports}}'
lsof -nP -iTCP:8080 -sTCP:LISTEN
docker compose up --build -d
```

## Run Locally

From repository root:

```bash
dotnet restore SmartLoadNet10.sln
dotnet run --project SmartLoad.Api/SmartLoad.Api.csproj
```

## Run Tests

```bash
dotnet test SmartLoadNet10.sln
```

## API Endpoints

### Health

```bash
curl http://localhost:8080/healthz
```

### Optimize

```bash
curl -X POST http://localhost:8080/api/v1/load-optimizer/optimize \
  -H "Content-Type: application/json" \
  -d @sample-request.json
```

Expected sample result:

- selected_order_ids: ["ord-001", "ord-002"]
- total_payout_cents: 430000

### Pareto

```bash
curl -X POST http://localhost:8080/api/v1/load-optimizer/pareto \
  -H "Content-Type: application/json" \
  -d @sample-request.json
```

Returns all non-dominated solutions across payout and average utilization.

## Input Notes

- Truck capacities must be positive.
- Order fields are validated (id, payout, weight, volume, lane fields, pickup/delivery dates).
- Duplicate order ids are rejected.
- pickup_date must be on or before delivery_date per order.

## Output Notes

- Payout is returned as integer cents (total_payout_cents).
- Utilization percentages are rounded to 2 decimals.
