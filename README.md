# SyncSourcing Core: High-Performance Architectural Evolution

This repository demonstrates a logical progression from traditional **Event Sourcing** to a high-performance, sharding-ready model called **"Sync-Sourcing."**

![Architecture Evolution](docs/images/architecture-main.svg)

## 🏆 Portfolio Summary: The Hybrid Shadow-Persistence Model
This project explores a solution for high-scale systems where "Replay Latency" is a bottleneck. By treating an **In-Memory Cache (L1)** as the primary Gatekeeper for concurrency and a **Database (L2)** as a persistent Shadow, we achieve sub-millisecond validation with 100% data durability.

### Key Innovations:
* **Hybrid Gatekeeping:** Concurrency is managed in L1 memory, while L2 handles persistent "Shadowing."
* **D-Sharding Architecture:** The design supports horizontal scaling by allowing different nodes or threads to own specific ID-shards in memory.
* **Update-First Batch Sourcing:** Minimizes database overhead by prioritizing `UPDATE` operations over `INSERT`. Using the `changes()` logic, we sync state and log simultaneously in a single atomic SQL roundtrip.
* **The Epsilon Strategy:** A pragmatic approach acknowledging that domain maturity is a journey. The cache-first model allows for "Ad-hoc Corrections," treating modeling gaps as an error epsilon that trends toward zero. This creates a feedback loop ideal for future **Machine Learning** optimization.

---

## 📦 The 3 Stages of Evolution

### 1. PoC 1: Basic Event Sourcing
* **Strategy:** Replay history to build state ($O(n)$).
* **Focus:** Establishing a lean event schema and separating business data from traceability metadata.
![PoC 1 Flow](docs/images/poc1-narrow.svg)

### 2. PoC 2: Synced Cache Sourcing
* **Strategy:** Introduce an In-Memory Gatekeeper.
* **Focus:** Achieving $O(1)$ state access and implementing a "Grand Prix" race simulation to demonstrate optimistic concurrency protection in memory.
![PoC 2 Flow](docs/images/poc2-narrow.svg)

### 3. PoC 3: Hybrid Shadow-Persistence (Current)
* **Strategy:** L1 Memory Cache + L2 SQL Shadow.
* **Focus:** Implementing a high-efficiency persistence model where the database acts as a reliable, passive shadow of the high-speed memory state.
![PoC 3 Flow](docs/images/poc3-narrow.svg)

---

## 🏗 System Standards

### Deliverables
> Tangible artifacts and interfaces provided by this architecture.

* **Client Solutions:** Console-based "Race" simulators for each stage.
* **Integrations & APIs:** `HybridEventStore` providing atomic L1/L2 synchronization and a decoupled `IMessagePublisher`.
* **Internal Tools:** SQL Schema for persistent state (Cache) and chronological Event History (Log).

### Business Data
* **Scope:** Real-time In-memory active state, persistent Shadow state, and the immutable Event Log.
* **Persistence Strategy:** Optimized **Update-First** shadowing to minimize database hot-path latency.
* **Source of Truth:** The Event Log (long-term audit) / Memory Cache (real-time concurrency).
* **Retention:** Events are retained indefinitely for legal auditability.

### Events (Business Logic Flow)
> The atomic sequence of data within the Hybrid Store.

* ◀️ **SUB `CommandReceived`**: Request received. System performs an immediate L1 Memory version check.
* ▶️ **PUB `StateShadowed`**: State is "Shadowed" to the L2 SQL Cache via an atomic batch (Update-First logic).
* ▶️ **PUB `EventArchived`**: Upon successful persistence, the fact is archived in the Log and broadcast to the message bus for downstream consumers.



---

## 🛠 Tech Stack
* **Runtime:** .NET 8
* **Database:** SQLite (Persistent Shadow Store)
* **Data Access:** Dapper (High-performance Micro-ORM)
* **Concurrency:** Hybrid Optimistic Concurrency Control (OCC)

![D-Sharding](docs/images/d-sharding.svg)

---

## How to Run
Each PoC is a standalone executable. Use the **VS Code Launch configurations** provided in the `.vscode` folder for one-click execution, or navigate to a folder and run:

dotnet run

<details>
<summary>View Mermaid Source Code</summary>

Architectural Evolution:
```mermaid
graph TD
    subgraph "The Evolution Path"
    A[PoC 1: Event Replay] -->|Too Slow| B[PoC 2: In-Memory Cache]
    B -->|Volatile| C[PoC 3: Hybrid Shadow-Persistence]
    end

    subgraph "The Epsilon Strategy"
    C -->|Gap Found| D[Ad-hoc Correction]
    D -->|Learn| E[Refined Domain Logic]
    E -->|Error Trends to| F((Zero))
    end

    style F fill:#00ff00,stroke:#333,stroke-width:2px
```

D-sharding:
```mermaid
graph LR
    subgraph "Traffic Sharding"
    LB[Load Balancer] --> S1[Node A: IDs 1-100]
    LB --> S2[Node B: IDs 101-200]
    end

    subgraph "Persistence"
    S1 --> DB[(SQL Shadow)]
    S2 --> DB
    end

    style S1 fill:#f9f,stroke:#333
    style S2 fill:#bbf,stroke:#333
```
PoC1 Flow (narrow):
```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant S as Sourcing System
    
    Note over S: [Phase 1: Creation]
    U->>S: CreateCart(Id)
    S-->>S: Log: CartCreated
    Note right of S: State: Initialized

    Note over S: [Phase 2: Identity]
    U->>S: Auth(Name, Email)
    S-->>S: Log: UserAuth
    
    Note over S: [Phase 3: Action]
    U->>S: AddItem("Camera")
    S-->>S: Log: ItemAdded
    Note right of S: State: Dirty (v3)

    Note over S: [Phase 4: Side Effect]
    S-->>S: Logic: Recalculate
    S-->>S: Log: TotalUpdated
    Note right of S: State: v4 (Final)
```

PoC2 Flow (narrow):
```mermaid
sequenceDiagram
    autonumber
    participant W as Workers (v1)
    participant M as Cache Manager
    
    Note over W, M: Concurrent Race Condition
    
    W->>M: Worker 03: Action (Expected v1)
    Note over M: [Gatekeeper Check]
    M-->>M: Compare: v1 == v1? (YES)
    M-->>M: Update Cache (v2)
    M-->>M: Append to Log
    M-->>W: [SUCCESS] Worker 03
    
    W->>M: Worker 07: Action (Expected v1)
    Note over M: [Gatekeeper Check]
    M-->>M: Compare: v1 == v2? (NO)
    Note right of M: State mismatch!
    M-->>W: [CONFLICT] Worker 07
```

PoC3 Flow (narrow):
```mermaid
sequenceDiagram
    autonumber
    participant W as Worker
    participant S as Hybrid Store (L1/L2)

    Note over S: [L1: Memory Gatekeeper]
    W->>S: Cmd (Expected v1)
    
    alt v1 is current
        S-->>S: L1 Logic: New State
        
        Note over S: [L2: Atomic SQL Batch]
        S-->>S: UPDATE Shadow (v2)
        S-->>S: INSERT Event Log
        
        Note over S: [Sync & Broadcast]
        S-->>S: Commit L1 Cache
        S-->>W: Success (v2)
        Note right of S: ▶️ PUB Event
    else v1 != current
        S-->>W: [CONFLICT]
    end
```

</details>