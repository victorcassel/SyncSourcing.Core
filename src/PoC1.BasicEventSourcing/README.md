# PoC 1: Advanced Event Sourced Shopping Cart

### The Concept: Complex Domain & Data Privacy
This stage establishes a production-grade domain model. We move beyond simple state changes to handle complex lists and **GDPR-sensitive data classification**.

### 🛡️ Data Classification & GDPR
To ensure global compliance, the events are categorized by their data sensitivity:

1.  **Identity Domain:** Contains PII (Personally Identifiable Information) like Names and Social Security Numbers.
2.  **Transactional Domain:** Contains business facts like order items and totals.

In a future "Surgical Pruning" scenario, the Identity events can be scrubbed or anonymized without breaking the transactional history of the cart.

### 🏗 Layered Architecture
| Layer | Responsibility |
| :--- | :--- |
| **Domain (Events)** | Categorized into Identity and Transactional streams. |
| **Domain (State)** | Tracks `TotalNeedsRecalculation` to decouple item logic from financial logic. |
| **Infrastructure** | Performs the Replay. Reconstructs state from the full event history. |
| **Business Logic** | Orchestrates events, ensuring that sensitive info is captured early in the flow. |

---

### 🔄 Business Flow: Decoupled Calculations
Instead of updating the total price automatically inside an "AddItem" event, we follow the best practice of emitting a separate `TotalAmountUpdated` event. This allows for complex tax/shipping logic to occur outside the core event storage.

```mermaid
sequenceDiagram
    autonumber
    participant U as User / Identity Provider
    participant S as Cart Service
    participant M as Event Store Manager
    participant St as ShoppingCart (State)

    Note over U, St: Phase 1: Identity & GDPR (Sensitive)
    U->>S: Authenticate(Name, Email)
    S->>M: AddEvent(UserAuthenticated)
    U->>S: CollectInfo(ID Number, Address)
    S->>M: AddEvent(UserInfoCollected)
    Note right of St: CustomerName & ID stored in State

    Note over U, St: Phase 2: Transactional Flow
    U->>S: AddItem("Laptop")
    S->>M: AddEvent(ItemAdded)
    Note right of St: TotalNeedsRecalculation = true
    
    Note over S: Internal Logic triggers Recalculation
    S->>M: AddEvent(TotalAmountUpdated)
    Note right of St: TotalNeedsRecalculation = false

    Note over U, St: Phase 3: Modifications
    U->>S: RemoveItem(Guid)
    S->>M: AddEvent(ItemRemoved)
    Note right of St: Item removed from list via Replay
```