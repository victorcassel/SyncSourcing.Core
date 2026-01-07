# PoC 3: Hybrid Memory-Persistent Sourcing
This project demonstrates a high-performance **Shadow-Persistence** model. It combines the ultra-low latency of PoC 2 with the robust durability of PoC 3.

![Hybrid caching](../../docs/images/poc3-flow.svg)

## The Strategy: L1/L2 Hybrid
In this architecture, the **In-Memory Cache (L1)** acts as the primary gatekeeper for concurrency, while the **SQL Database (L2)** acts as a persistent shadow.

### How it Works:
1. **L1 Read-Through:** The system always checks the memory cache first. On a miss, it hydrates the cache from the SQL "Shadow Cache."
2. **Memory Gatekeeping:** All version checks happen instantly in memory, providing sub-millisecond validation.
3. **Atomic Shadowing:** Upon a valid memory update, the system performs a single-roundtrip batch update to SQL. The DB only accepts the change if its version matches the L1 state, ensuring total consistency.

## Key Benefits
* **Scalability:** Enables sharding across multiple event managers.
* **Low Latency:** 99% of read/validation operations happen in memory.
* **Resilience:** If the memory cache is cleared, it is perfectly reconstructed from the SQL shadow.

<details>
<summary>View Mermaid Source Code</summary>

```mermaid
sequenceDiagram
    autonumber
    participant WA as Worker A (Winner)
    participant WB as Worker B (Loser)
    box rgb(245, 245, 255) Hybrid Event Store
    participant L1 as L1 Memory (Read Cache)
    participant L2 as L2 SQL (Atomic Batch)
    end
    participant Bus as Message Publisher (Side Effect)

    Note over L1, L2: Initial State: Version 1

    %% --- PHASE 1: Concurrent Reads ---
    par Concurrent Reads
        WA->>L1: Get State (Expect v1)
        L1-->>WA: Return State v1
    and
        WB->>L1: Get State (Expect v1)
        L1-->>WB: Return State v1
    end

    WA->>WA: Calculate v2
    WB->>WB: Calculate v2

    Note over WA, Bus: --- THE RACE BEGINS ---

    %% --- PHASE 2: The Winner's Path ---
    rect rgb(225, 255, 235)
    WA->>L2: Execute SQL Batch (v1 to v2)
    
    Note right of L2: SQL ENGINE Success:<br/>1. Version check passed<br/>2. State updated<br/>3. Event Log written

    L2-->>L2: Commit Transaction
    L2-->>WA: Success (Changes = 1)

    WA->>L1: Update L1 Cache (v2)
    WA->>Bus: ▶️ [PUB] Event Broadcast
    end

    %% --- PHASE 3: The Loser's Path ---
    rect rgb(255, 235, 235)
    WB->>L2: Execute SQL Batch (v1 to v2)

    Note right of L2: SQL ENGINE Failure:<br/>1. Version is already v2<br/>2. Check fails<br/>3. No rows affected

    L2-->>L2: No-Op
    L2-->>WB: Conflict (Changes = 0)

    Note left of WB: [CONFLICT] Skip Publish
    end
```
</details>
