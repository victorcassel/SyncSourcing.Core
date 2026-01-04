# SyncSourcing Core: High-Performance Architectural Evolution

This repository demonstrates the evolution of state management from traditional **Event Sourcing** to a high-performance, persistent model called **"Sync-Sourcing."**

## 🏆 Portfolio Summary: The Hybrid Shadow-Persistence Model
In modern high-scale systems (like retail or finance), traditional Event Sourcing often suffers from "Replay Latency." This project explores a solution where we treat an **In-Memory Cache (L1)** as the primary Gatekeeper for concurrency and a **Database (L2)** as a persistent Shadow.

### Why Sync-Sourcing?
* **D-Sharding Ready:** By moving the version-check to memory, we enable horizontal scaling where different nodes/threads own specific ID-shards.
* **The Epsilon Strategy:** We acknowledge that domain models are iterative. Our "Cache-First" approach allows for pragmatic "Ad-hoc Corrections," treating modeling gaps as a mathematical epsilon that trends toward zero as the system matures.
* **Single-Roundtrip Persistence:** We minimize database overhead by batching the State-Update and Event-Log into a single atomic SQL operation.



---

## 📦 The 3 Stages of Evolution

### 1. PoC 1: Basic Event Sourcing
**Focus:** The Foundation.
* **Strategy:** Replay history to build state ($O(n)$).
* **Innovation:** Separation of pure Domain Data from Traceability Metadata. Establishing a standardized event schema.

### 2. PoC 2: Synced Cache Sourcing
**Focus:** Performance & Concurrency.
* **Strategy:** Introduce an In-Memory Gatekeeper.
* **Innovation:** $O(1)$ state access. Introduction of a "Grand Prix" race simulation to prove optimistic concurrency via version-tracking.

### 3. PoC 3: Hybrid Shadow-Persistence (Current)
**Focus:** Production-Grade Scaling.
* **Strategy:** L1 Memory Cache + L2 SQL Shadow.
* **Innovation:** Uses SQL Batching (`changes()` logic) to sync the persistent cache and the event log in a single roundtrip. Memory stays the "Hot" truth; SQL stays the "Cold" persistent truth.



---

## 🛠 Tech Stack
* **Runtime:** .NET 8
* **Database:** SQLite (Persistent Shadow Store)
* **Data Access:** Dapper (High-performance Micro-ORM)
* **Concurrency:** Hybrid Optimistic Concurrency Control (OCC)

---

## 🏗 System Standards

### Deliverables
> The tangible services provided by this architecture.

* **Client Solutions:** Console-based "Race" simulators for each stage.
* **Integrations & APIs:** `HybridEventStore` providing atomic persistence.
* **Internal Tools:** SQL Schema for State Cache and Event Log.

### Business Data
* **Primary Data:** Current State (Synced Cache) + Business Event History (Source of Truth).
* **Storage:** SQL Server / SQLite (`SyncSourcing.db`).
* **Retention:** Events are kept indefinitely (Audit/Legal); Cache is persistent but rebuildable.

### Events (Business Flows)
◀️ **SUB** `CommandReceived` -> L1 Memory Version Check.  
▶️ **PUB** `StateShadowed` -> Batch SQL Update to L2.  
▶️ **PUB** `EventArchived` -> Written to Event Log.

---

## How to Run
Each PoC is a standalone executable. Navigate to the desired folder and run:
```bash
dotnet run