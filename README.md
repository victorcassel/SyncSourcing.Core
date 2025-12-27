# SyncSourcing.Core

---

<center>
  <table>
    <tr>
      <td style="background-color: #f0f7ff; padding: 20px; border-radius: 10px; border: 1px solid #0056b3;">
        <h2 style="margin-top: 0; color: #0056b3;">🚀 Welcome to Sync-Sourcing</h2>
        <p><strong>The Pitch:</strong> SyncSourcing eliminates the "asynchronous tax" of traditional Event Sourcing. By synchronizing your domain model's cache in the same roundtrip as the event persistence, we achieve extreme performance without sacrificing the integrity of the event log.</p>
        <p><strong>The Mission:</strong> To provide an architecture where the read model (State) is always in lockstep with the write model (Events) – in real-time.</p>
      </td>
    </tr>
  </table>
</center>

---

## Why Sync-Sourcing?

Traditional Event Sourcing often forces developers to manage "Eventual Consistency." You persist an event and hope that the read model updates fast enough. **SyncSourcing changes this paradigm.**

### Key Benefits
* **$O(1)$ Read Performance:** The domain object (cache) is always ready in memory or on disk. No heavy replays are required to determine the current state.
* **Zero Latency:** Your cache is updated within the same transaction as your events. Once the API call returns, the "truth" is updated everywhere.
* **Simplified Architecture:** No need for complex message brokers or asynchronous projection engines to get started.

## The Engine: Reverse Event Sourcing
Beyond the synchronized cache, SyncSourcing utilizes a unique pattern where we persist the **negative delta** of an event (Reverse Events). 

1. **Forward:** The event is applied to the domain object.
2. **Reverse:** The object's previous state is captured and persisted as a delta in the log.

This allows us to offer **Surgical Rollbacks** and **History Pruning** (e.g., for GDPR compliance) without breaking the cryptographic integrity of the event chain. It gives you the best of both worlds: the speed of a SQL database and the provable audit trail of Event Sourcing.

## Project Status
This project is under active development. Our vision is to set a new standard for how business-critical systems are modernized and scaled without losing their historical context.

---
*Created by Victor Cassel – Bridging the Gap between Legacy and Future.*
