# PoC 3: Persistent Batch Sourcing

This project implements a production-ready **Sync-Sourcing** engine. It evolves the architecture from the previous in-memory versions to a persistent SQL-backed system, optimized for high-performance and data integrity.

## Architectural Overview
The core objective of PoC 3 is to minimize database latency while maintaining a strict 1:1 relationship between the **Application State (Cache)** and the **Audit Log (Events)**.

### Key Innovation: The Atomic Batch
Unlike traditional implementations that use heavy application-level transactions, this engine utilizes **SQL Batching**. In a single roundtrip, the system performs a conditional update:
1. **Conditional Update:** It attempts to update the `Carts` table only if the current version matches the `ExpectedVersion`.
2. **Conditional Insert:** It inserts the event into the `CartEvents` table only if the previous update affected exactly one row (using the `changes()` function in SQLite).



## Technical Stack
* **Runtime:** .NET 8
* **Database:** SQLite (File-based persistence)
* **Micro-ORM:** Dapper
* **Logic:** Optimistic Concurrency Control (OCC)

## How to Run
1. Navigate to the project folder: `src/PoC3.SqlBackedSourcing`
2. Ensure dependencies are restored: `dotnet restore`
3. Execute the demo: `dotnet run`

## Demo Logic: The Grand Prix
The application simulates a race condition where 10 concurrent workers attempt to modify a single record at the same time. 
* **The Winner:** The first worker to reach the database succeeds in both the state update and the event log insertion.
* **The Followers:** Subsequent workers fail the version check at the database level, resulting in zero state changes and zero logged events for their requests.