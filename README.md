# SyncSourcing Core: High-Performance Architectural Evolution

This repository demonstrates a logical progression from traditional **Event Sourcing** to a high-performance, sharding-ready model called **"Sync-Sourcing."**

## 🏆 Portfolio Summary: The Hybrid Shadow-Persistence Model
This project explores a solution for high-scale systems where "Replay Latency" is a bottleneck. By treating an **In-Memory Cache (L1)** as the primary Gatekeeper for concurrency and a **Database (L2)** as a persistent Shadow, we achieve sub-millisecond validation with 100% data durability.

### Key Innovations:
* **Hybrid Gatekeeping:** Concurrency is managed in L1 memory, while L2 handles persistent "Shadowing."
* **D-Sharding Architecture:** The design supports horizontal scaling by allowing different nodes or threads to own specific ID-shards in memory.
* **Atomic Batch Sourcing:** Minimizes database roundtrips by using conditional SQL logic (`changes()` or `UPSERT`) to sync state and log simultaneously.
* **The Epsilon Strategy:** A pragmatic approach that acknowledges domain maturity as a journey. The cache-first model allows for "Ad-hoc Corrections," treating modeling gaps as an error epsilon that trends toward zero. This creates a feedback loop ideal for future Machine Learning optimization.



---

## 📦 Stage-by-Stage Evolution

### 1. PoC 1: Basic Event Sourcing
* **Strategy:** Replay history to build state ($O(n)$).
* **Focus:** Establishing a lean event schema and separating business data from traceability metadata.

### 2. PoC 2: Synced Cache Sourcing
* **Strategy:** Introduce an In-Memory Gatekeeper.
* **Focus:** Achieving $O(1)$ state access and implementing a "Grand Prix" race simulation to demonstrate optimistic concurrency protection.

### 3. PoC 3: Hybrid Shadow-Persistence (Current)
* **Strategy:** L1 Memory Cache + L2 SQL Shadow.
* **Focus:** Implementing a single-roundtrip persistence model where the database acts as a reliable, passive shadow of the high-speed memory state.



---

## 🏗 System Standards

### Deliverables
* **Client Solutions:** Command-line simulators for concurrent race conditions.
* **Integrations & APIs:** `HybridEventStore` providing atomic L1/L2 synchronization.
* **Internal Tools:** SQL Schema for persistent state (Cache) and Event History (Log).

### Business Data
* **Scope:** Real-time In-memory state, persistent Shadow state, and the immutable Event Log.
* **Persistence:** SQLite (`SyncSourcing.db`) using the "Batch Sourcing" pattern.
* **Source of Truth:** The Event Log (long-term audit) / Memory Cache (real-time concurrency).
* **Retention:** Events are retained indefinitely for legal auditability.

### Events (Business Flows)
> This section describes the atomic sequence of data within the Hybrid Store.

* ◀️ **SUB `CommandReceived`**: The system receives a request. It performs an immediate L1 Memory version check to prevent processing stale data.
* ▶️ **PUB `StateShadowed`**: Upon L1 success, the state is "Shadowed" to the L2 SQL Cache via an atomic batch update.
* ▶️ **PUB `EventArchived`**: The business fact is permanently archived in the Event Log and broadcast to the message bus for downstream consumers.

---

## How to Run
Each PoC is a standalone executable. Use the **VS Code Launch configurations** provided in the `.vscode` folder for one-click execution, or navigate to a folder and run:
```bash
dotnet run