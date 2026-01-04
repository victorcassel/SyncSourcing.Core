# SyncSourcing Core: An Architectural Evolution

Welcome to the **SyncSourcing Core** project. This repository is a step-by-step architectural journey exploring the evolution of Event Sourcing into a pragmatic, high-performance model called "Sync-Sourcing."

## Project Philosophy: The Epsilon Strategy
The project is built on the belief that domain modeling is an iterative process. By using a **Cache-First** strategy, we allow for "Ad-hoc Corrections" to rescue the state when unforeseen edge cases occur. This creates a "mathematical epsilon" of error that gradually trends toward zero as the domain model matures and captures all system events.



## The Roadmap: 3 Stages of Sourcing

| Stage | Focus | State Management | Performance |
| :--- | :--- | :--- | :--- |
| **PoC 1** | Basic Event Sourcing | Replay from Log | $O(n)$ |
| **PoC 2** | Synced Cache Sourcing | In-Memory Synced Cache | $O(1)$ |
| **PoC 3** | Persistent Batch Sourcing | SQL Atomic Batching | $O(1)$ + Persistent |

---

### 📦 PoC 1: Basic Event Sourcing
The foundation. Demonstrates how to reconstruct state by replaying a chronological log of business facts. 
* **Key Concept:** Separation of Business Data from Traceability Metadata.

### 📦 PoC 2: Synced Cache Sourcing
Introduces the **Gatekeeper Pattern**. By maintaining a thread-safe in-memory cache, we eliminate the need for costly replays while adding optimistic concurrency protection.
* **Key Concept:** Version-based conflict resolution.

### 📦 PoC 3: Persistent Batch Sourcing
The production blueprint. Moves the logic to a physical SQL store using a **Single Roundtrip** approach. It leverages database atomicity to ensure the Cache and the Log are never out of sync.
* **Key Concept:** SQL-level conditional execution (`changes()` logic).

---

## Getting Started
To explore the evolution, it is recommended to run the projects in order. Each folder contains its own `Program.cs` and a dedicated README with specific technical details.

```bash
# Example: Running the Persistent Grand Prix
cd src/PoC3.SqlBackedSourcing
dotnet run