# AgriSage - PlantUML State Diagrams

30 standalone diagrams corresponding to the state diagrams discussed in the chat. Inventory/Credit Reservations and Payment Allocation/Debt Transaction are separate diagrams.

Each source includes its own styling: left-to-right layout, rounded white states, thin grey borders, orthogonal connectors, no shadow and no empty description compartment. PlantUML controls exact routing; the result is not guaranteed to match a manually placed drawing pixel for pixel.

Each terminating lifecycle uses one shared final pseudostate within its diagram. Reactivatable administrative objects intentionally have no final node. Composite containers are visual groups, not additional database statuses.

The session and outbox diagrams show derived operational conditions, not stored status enums. Administrative diagrams show representative transitions where the design does not impose a complete graph.

Payment correction: current PayOsPaymentService maps EXPIRED/FAILED provider links to FAILED, and CANCELLED links to CANCELLED. This follows the reference image and current code; the earlier chat description of expiration as CANCELLED was inaccurate.

## 01-user-account - User Account

Source: [puml/01-user-account.puml](puml/01-user-account.puml)

Administrative transitions shown are representative. Application authorization still applies. Status changes invalidate existing sessions.

```plantuml
@startuml 01-user-account
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title User Account

[*] --> ACTIVE : System / Create account
ACTIVE --> INACTIVE : Administrator / Deactivate account
INACTIVE --> ACTIVE : Administrator / Reactivate account
ACTIVE --> SUSPENDED : Administrator / Suspend account
SUSPENDED --> ACTIVE : Administrator / Lift suspension
ACTIVE --> LOCKED : Administrator / Lock account
LOCKED --> ACTIVE : Administrator / Unlock account
@enduml
```

## 02-auth-session - Authentication Session

Source: [puml/02-auth-session.puml](puml/02-auth-session.puml)

These are derived session conditions, not a database status enum. Logging in again creates a new session.

```plantuml
@startuml 02-auth-session
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Authentication Session

[*] --> VALID : User / Log in successfully
VALID --> VALID : User / Use or refresh valid session
VALID --> EXPIRED : System / Session expiry reached
VALID --> REVOKED : User or Administrator / Log out or revoke session
VALID --> INVALIDATED : System / Security version changed
EXPIRED --> [*]
REVOKED --> [*]
INVALIDATED --> [*]
@enduml
```

## 03-cart - Shopping Cart

Source: [puml/03-cart.puml](puml/03-cart.puml)

A converted or abandoned cart is not reactivated; further shopping uses a new active cart.

```plantuml
@startuml 03-cart
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Shopping Cart

[*] --> ACTIVE : Farmer / Create cart
ACTIVE --> ACTIVE : Farmer / Add, update, or remove products
ACTIVE --> ACTIVE : System / Reject invalid checkout
ACTIVE --> CONVERTED : Farmer / Complete checkout\nSystem / Create order atomically
ACTIVE --> ABANDONED : System / Mark cart abandoned
CONVERTED --> [*]
ABANDONED --> [*]
@enduml
```

## 04-order - Sales Order

Source: [puml/04-order.puml](puml/04-order.puml)

FULFILLMENT is a visual group, not a stored status. Farmer cancellation is allowed only while PENDING_CONFIRMATION. Payments and refunds remain separate lifecycles.

```plantuml
@startuml 04-order
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Sales Order

state "Order Fulfillment in Progress" as FULFILLMENT {
    state CONFIRMED
    state PREPARING
    state READY_FOR_FULFILLMENT
    state PARTIALLY_FULFILLED
    CONFIRMED --> PREPARING : Staff / Start preparation
    CONFIRMED --> READY_FOR_FULFILLMENT : Staff / Mark ready
    PREPARING --> READY_FOR_FULFILLMENT : Staff / Finish preparation
    CONFIRMED --> PARTIALLY_FULFILLED : System / Record partial fulfillment
    PREPARING --> PARTIALLY_FULFILLED : System / Record partial fulfillment
    READY_FOR_FULFILLMENT --> PARTIALLY_FULFILLED : System / Record partial fulfillment
    PARTIALLY_FULFILLED --> PARTIALLY_FULFILLED : System / Record more fulfillment\n[remainder still open]
}
[*] --> PENDING_CONFIRMATION : Farmer or Staff / Create order
PENDING_CONFIRMATION --> PENDING_CONFIRMATION : Farmer or Staff / Update pending order
PENDING_CONFIRMATION --> CONFIRMED : Staff / Confirm order\nSystem / Reserve stock and credit if required
PENDING_CONFIRMATION --> CANCELLED : Farmer or Staff / Cancel pending order
FULFILLMENT --> COMPLETED : System / Close fulfillment\n[all quantities fulfilled, none cancelled]
FULFILLMENT --> PARTIALLY_CANCELLED : System / Close fulfillment\n[some fulfilled, remaining quantities cancelled]
FULFILLMENT --> CANCELLED : Staff / Cancel remaining quantities\n[nothing fulfilled and no open remainder]
COMPLETED --> [*]
PARTIALLY_CANCELLED --> [*]
CANCELLED --> [*]
@enduml
```

## 05-payment - Payment

Source: [puml/05-payment.puml](puml/05-payment.puml)

Expired payOS links map to FAILED in the current PayOsPaymentService. Invalid or duplicate webhooks do not create a second receipt of money. Retrying a failed or cancelled payment creates a new Payment.

```plantuml
@startuml 05-payment
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Payment

[*] --> PENDING : Farmer / Sales Staff creates payOS link\nSales Staff records cash or bank transfer
PENDING --> PAID : payOS webhook / status synchronization\nSales Staff / Store Owner receives cash\nStore Owner confirms bank transfer
PENDING --> FAILED : payOS [link expired or failed]\nStore Owner rejects bank transfer
PENDING --> CANCELLED : Farmer cancels payOS link\npayOS [link cancelled]\nSales Staff / Store Owner cancels bank transfer\nSystem [order cancelled]
PAID --> PARTIALLY_REFUNDED : System / Record completed refund\n[0 < total refunded < payment amount]
PAID --> REFUNDED : System / Record completed refund\n[total refunded = payment amount]
PARTIALLY_REFUNDED --> REFUNDED : System / Record completed refund\n[total refunded = payment amount]
PARTIALLY_REFUNDED --> PARTIALLY_REFUNDED : System / Record additional completed refund\n[total refunded < payment amount]
REFUNDED --> [*]
FAILED --> [*]
CANCELLED --> [*]
@enduml
```

## 06-delivery - Delivery

Source: [puml/06-delivery.puml](puml/06-delivery.puml)

OPEN_DELIVERY is a visual group. Delivery has no PARTIALLY_CANCELLED status: after partial delivery, cancelling the remainder ends the Delivery as DELIVERED; cancelled quantities stay on its items.

```plantuml
@startuml 06-delivery
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Delivery

state "Delivery Not Yet Closed" as OPEN_DELIVERY {
    state DRAFT
    state ASSIGNED
    state OUT_FOR_DELIVERY
    state PARTIALLY_DELIVERED
    state RETRY_PENDING
    DRAFT --> ASSIGNED : Staff / Assign delivery staff
    ASSIGNED --> OUT_FOR_DELIVERY : Delivery Staff / Dispatch delivery
    OUT_FOR_DELIVERY --> PARTIALLY_DELIVERED : System / Complete attempt\n[some delivered, remainder still open]
    OUT_FOR_DELIVERY --> RETRY_PENDING : System / Complete attempt\n[attempt delivered nothing, remainder still open]
    PARTIALLY_DELIVERED --> OUT_FOR_DELIVERY : Delivery Staff / Dispatch remaining goods
    RETRY_PENDING --> OUT_FOR_DELIVERY : Delivery Staff / Retry delivery
}
[*] --> DRAFT : Staff / Create delivery
OUT_FOR_DELIVERY --> DELIVERED : System / Complete attempt\n[nothing remains to deliver]
OPEN_DELIVERY --> CANCELLED : Staff / Cancel remainder\n[nothing ever delivered, no attempt in progress]
OPEN_DELIVERY --> DELIVERED : Staff / Cancel remainder\n[some goods already delivered, no attempt in progress]
DELIVERED --> [*]
CANCELLED --> [*]
@enduml
```

## 07-delivery-attempt - Delivery Attempt

Source: [puml/07-delivery-attempt.puml](puml/07-delivery-attempt.puml)

Only one attempt may be IN_PROGRESS for a Delivery at a time. Another attempt is a new object.

```plantuml
@startuml 07-delivery-attempt
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Delivery Attempt

[*] --> IN_PROGRESS : Delivery Staff / Start attempt
IN_PROGRESS --> SUCCESS : Delivery Staff / Complete attempt\n[all attempted quantities delivered]
IN_PROGRESS --> PARTIAL_SUCCESS : Delivery Staff / Complete attempt\n[some attempted quantities delivered]
IN_PROGRESS --> FAILED : Delivery Staff / Complete attempt\n[no goods delivered]
IN_PROGRESS --> CANCELLED : Delivery Staff / Cancel attempt
SUCCESS --> [*]
PARTIAL_SUCCESS --> [*]
FAILED --> [*]
CANCELLED --> [*]
@enduml
```

## 08-delivery-incident - Delivery Incident

Source: [puml/08-delivery-incident.puml](puml/08-delivery-incident.puml)

Inventory consequences, when applicable, require an appropriate posted stock movement.

```plantuml
@startuml 08-delivery-incident
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Delivery Incident

[*] --> OPEN : Staff / Report incident
OPEN --> OPEN : Staff / Add incident details
OPEN --> RESOLVED : Authorized Staff / Resolve incident\nand record resolution
RESOLVED --> [*]
@enduml
```

## 09-goods-receipt - Goods Receipt

Source: [puml/09-goods-receipt.puml](puml/09-goods-receipt.puml)

Only draft receipts are editable or cancellable. Confirmed receipt corrections require a separate adjustment or reversal operation.

```plantuml
@startuml 09-goods-receipt
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Goods Receipt

[*] --> DRAFT : Staff / Create goods receipt
DRAFT --> DRAFT : Staff / Update receipt and items
DRAFT --> CONFIRMED : Staff / Confirm receipt\nSystem / Post stock-in and update weighted average cost
DRAFT --> CANCELLED : Staff / Cancel draft receipt
CONFIRMED --> [*]
CANCELLED --> [*]
@enduml
```

## 10-inventory-lot - Inventory Lot

Source: [puml/10-inventory-lot.puml](puml/10-inventory-lot.puml)

Representative business transitions. DEPLETED is a declared status, not a claim that current stock-posting code changes status automatically at zero stock. Actual expiry date is checked independently; expired, quarantined, blocked or depleted lots cannot be sold/reserved/allocated.

```plantuml
@startuml 10-inventory-lot
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Inventory Lot

[*] --> ACTIVE : System / Create inventory lot
ACTIVE --> QUARANTINED : Staff / Quarantine lot
QUARANTINED --> ACTIVE : Staff / Release quarantine\n[lot remains eligible]
ACTIVE --> BLOCKED : Staff / Block lot
BLOCKED --> ACTIVE : Staff / Unblock lot\n[lot remains eligible]
ACTIVE --> EXPIRED : System / Mark expired\n[expiry date is before business date]
ACTIVE --> DEPLETED : System / Record depleted status
DEPLETED --> ACTIVE : System / Restore eligibility\n[stock available and lot remains eligible]
@enduml
```

## 11-stock-movement - Stock Movement

Source: [puml/11-stock-movement.puml](puml/11-stock-movement.puml)

STOCK_IN, SALE, RETURN_IN and ADJUSTMENT are movement types, not lifecycle states. POSTED remains eligible for reversal, so it is not connected directly to final.

```plantuml
@startuml 11-stock-movement
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Stock Movement

[*] --> DRAFT : System or Staff / Create stock movement
DRAFT --> DRAFT : Staff / Update draft movement
DRAFT --> POSTED : Owning use case / Post movement\nand update inventory
DRAFT --> CANCELLED : Staff / Cancel draft movement
POSTED --> REVERSED : Owning use case / Post corresponding reversal movement
CANCELLED --> [*]
REVERSED --> [*]
@enduml
```

## 12-stocktake - Stocktake

Source: [puml/12-stocktake.puml](puml/12-stocktake.puml)

Completion requires all active items to be counted; stale lines must be handled according to the snapshot rules.

```plantuml
@startuml 12-stocktake
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Stocktake

[*] --> DRAFT : Staff / Create stocktake
DRAFT --> IN_PROGRESS : Staff / Start stocktake
DRAFT --> CANCELLED : Staff / Cancel draft stocktake
IN_PROGRESS --> IN_PROGRESS : Staff / Record physical count
IN_PROGRESS --> IN_PROGRESS : Staff / Refresh stale snapshot and recount
IN_PROGRESS --> COMPLETED : Staff / Complete stocktake\n[all items counted and validated]
IN_PROGRESS --> CANCELLED : Staff / Cancel stocktake
COMPLETED --> [*]
CANCELLED --> [*]
@enduml
```

## 13-inventory-reservation - Inventory Reservation

Source: [puml/13-inventory-reservation.puml](puml/13-inventory-reservation.puml)

Statuses are derived from reserved, consumed and released quantities. After any consumption, release the unused remainder instead of cancelling the reservation.

```plantuml
@startuml 13-inventory-reservation
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Inventory Reservation

[*] --> ACTIVE : System / Reserve inventory for order
ACTIVE --> ACTIVE : System / Release part\n[nothing consumed, remainder still reserved]
ACTIVE --> PARTIALLY_CONSUMED : Fulfillment / Consume part\n[remaining quantity > 0]
ACTIVE --> CONSUMED : Fulfillment / Consume all remaining quantity
ACTIVE --> RELEASED : System / Release all\n[nothing ever consumed]
ACTIVE --> CANCELLED : System / Cancel order\n[nothing ever consumed]
PARTIALLY_CONSUMED --> PARTIALLY_CONSUMED : System / Consume or release more\n[remaining quantity > 0]
PARTIALLY_CONSUMED --> CONSUMED : System / Consume or release remainder\n[remaining quantity = 0]
CONSUMED --> [*]
RELEASED --> [*]
CANCELLED --> [*]
@enduml
```

## 14-credit-reservation - Credit Reservation

Source: [puml/14-credit-reservation.puml](puml/14-credit-reservation.puml)

CONSUMED means some amount was consumed and no reservation remains; unused amounts may have been released. Credit exposure includes receivables plus active remaining reservations.

```plantuml
@startuml 14-credit-reservation
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Credit Reservation

[*] --> ACTIVE : System / Reserve credit for order
ACTIVE --> ACTIVE : System / Release part\n[nothing consumed, remainder still reserved]
ACTIVE --> PARTIALLY_CONSUMED : Fulfillment / Consume part\n[remaining amount > 0]
ACTIVE --> CONSUMED : Fulfillment / Consume all remaining amount
ACTIVE --> RELEASED : System / Release all\n[nothing ever consumed]
ACTIVE --> CANCELLED : System / Cancel order\n[nothing ever consumed]
PARTIALLY_CONSUMED --> PARTIALLY_CONSUMED : System / Consume or release more\n[remaining amount > 0]
PARTIALLY_CONSUMED --> CONSUMED : System / Consume or release remainder\n[remaining amount = 0]
CONSUMED --> [*]
RELEASED --> [*]
CANCELLED --> [*]
@enduml
```

## 15-farmer-credit-profile - Farmer Credit Profile

Source: [puml/15-farmer-credit-profile.puml](puml/15-farmer-credit-profile.puml)

Available credit = credit limit - outstanding receivables - remaining reserved credit. ACTIVE alone is insufficient: customer, debt account and other eligibility rules also apply.

```plantuml
@startuml 15-farmer-credit-profile
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Farmer Credit Profile

[*] --> ACTIVE : Staff / Create credit profile
ACTIVE --> SUSPENDED : Authorized Manager / Suspend credit access
SUSPENDED --> ACTIVE : Authorized Manager / Restore credit access
ACTIVE --> BLOCKED : Authorized Manager / Block credit access
SUSPENDED --> BLOCKED : Authorized Manager / Block credit access
BLOCKED --> ACTIVE : Authorized Manager / Unblock credit access
BLOCKED --> SUSPENDED : Authorized Manager / Change to suspended
@enduml
```

## 16-debt-account - Debt Account

Source: [puml/16-debt-account.puml](puml/16-debt-account.puml)

Administrative status changes are shown. The Domain status setter does not make CLOSED irreversible; authorization and business restrictions belong to the use case.

```plantuml
@startuml 16-debt-account
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Debt Account

[*] --> ACTIVE : System / Create debt account
ACTIVE --> BLOCKED : Authorized Staff / Block account
BLOCKED --> ACTIVE : Authorized Staff / Unblock account
ACTIVE --> CLOSED : Authorized Staff / Close account\n[business conditions satisfied]
BLOCKED --> CLOSED : Authorized Staff / Close account\n[business conditions satisfied]
CLOSED --> ACTIVE : Authorized Staff / Reopen account\n[permitted by application policy]
@enduml
```

## 17-debt-entry - Debt Entry

Source: [puml/17-debt-entry.puml](puml/17-debt-entry.puml)

OPEN_DEBT is a visual group. A disputed entry may receive payments and stays DISPUTED until resolved. Overdue is derived from due date and balance, not an enum state. Receivable changes require DebtTransactions.

```plantuml
@startuml 17-debt-entry
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Debt Entry

state "Debt Entry Under Processing" as OPEN_DEBT {
    state OPEN
    state PARTIALLY_PAID
    state ADJUSTED
    state DISPUTED
    OPEN --> PARTIALLY_PAID : System / Apply partial payment
    PARTIALLY_PAID --> PARTIALLY_PAID : System / Apply additional payment\n[balance remains]
    OPEN --> DISPUTED : Staff / DISPUTE
    PARTIALLY_PAID --> DISPUTED : Staff / DISPUTE
    ADJUSTED --> DISPUTED : Staff / DISPUTE
    DISPUTED --> DISPUTED : System / Apply payment\n[dispute remains unresolved]
    OPEN --> ADJUSTED : Manager / ADJUST\n[positive balance remains]
    PARTIALLY_PAID --> ADJUSTED : Manager / ADJUST\n[positive balance remains]
    DISPUTED --> ADJUSTED : Manager / ADJUST\n[positive balance remains]
    ADJUSTED --> ADJUSTED : Manager / ADJUST\n[positive balance remains]
    ADJUSTED --> PARTIALLY_PAID : System / Apply payment\n[positive balance remains]
    DISPUTED --> OPEN : Staff / KEEP\n[outstanding = original amount]
    DISPUTED --> PARTIALLY_PAID : Staff / KEEP\n[0 < outstanding < original amount]
}
[*] --> OPEN : Fulfillment or adjustment use case / Record receivable
OPEN --> PAID : System / Apply payment\n[outstanding becomes zero]
PARTIALLY_PAID --> PAID : System / Apply payment\n[outstanding becomes zero]
ADJUSTED --> PAID : System / Apply payment\n[outstanding becomes zero]
OPEN_DEBT --> PAID : Manager / ADJUST\n[outstanding becomes zero]
DISPUTED --> PAID : Staff / KEEP\n[outstanding = 0]
OPEN_DEBT --> CANCELLED : Manager / CANCEL\n[post reduction of full outstanding balance]
PAID --> [*]
CANCELLED --> [*]
@enduml
```

## 18-sales-return - Sales Return

Source: [puml/18-sales-return.puml](puml/18-sales-return.puml)

Only resellable goods may be restocked. Reduce attributable unpaid debt first, then refund the remaining eligible value. Only REQUESTED return items are editable.

```plantuml
@startuml 18-sales-return
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Sales Return

[*] --> REQUESTED : Farmer or Staff / Submit return request
REQUESTED --> REQUESTED : Staff / Update return items
REQUESTED --> APPROVED : Staff / Approve request
REQUESTED --> REJECTED : Staff / Reject request
REQUESTED --> CANCELLED : Staff / Cancel request
APPROVED --> RECEIVED : Staff / Receive returned goods
APPROVED --> CANCELLED : Staff / Cancel before goods received
RECEIVED --> RECEIVED : Staff / Inspect individual items
RECEIVED --> INSPECTED : Staff / Complete all inspections\nSystem / Determine debt reduction and refund
INSPECTED --> PARTIALLY_RESOLVED : System / Record partial resolution
INSPECTED --> COMPLETED : Return use case / Complete all required resolution
PARTIALLY_RESOLVED --> PARTIALLY_RESOLVED : System / Continue restocking or settlement
PARTIALLY_RESOLVED --> COMPLETED : Return use case / Complete all required resolution
REJECTED --> [*]
CANCELLED --> [*]
COMPLETED --> [*]
@enduml
```

## 19-refund - Refund

Source: [puml/19-refund.puml](puml/19-refund.puml)

Applies to sales-return and cancelled-order refunds. A failed refund is retried with a new Refund, never by reopening the failed object.

```plantuml
@startuml 19-refund
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Refund

[*] --> PENDING : Staff / Create refund request
PENDING --> PENDING : Staff / Update processing details
PENDING --> COMPLETED : Staff / Confirm successful refund
PENDING --> FAILED : Staff / Record refund failure
PENDING --> CANCELLED : Staff / Cancel refund request
COMPLETED --> [*]
FAILED --> [*]
CANCELLED --> [*]
@enduml
```

## 20-diagnosis-case - AI Diagnosis Case

Source: [puml/20-diagnosis-case.puml](puml/20-diagnosis-case.puml)

Review requires can_review_ai. VERIFIED and INCONCLUSIVE allow re-review and are not final states. A new review supersedes the previous review and deactivates its recommendations. Recommendations require VERIFIED; Healthy cases receive treatment guidance only, no product recommendation.

```plantuml
@startuml 20-diagnosis-case
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title AI Diagnosis Case

state "Human Review Not Yet Completed" as BEFORE_REVIEW {
    state SUBMITTED
    state PROCESSING
    state AI_COMPLETED
    state UNDER_REVIEW
    state FAILED
    SUBMITTED --> PROCESSING : System / Start AI analysis\n[at least one active image]
    PROCESSING --> AI_COMPLETED : System / Complete AI analysis\n[at least one successful inference]
    PROCESSING --> FAILED : System / Record AI failure
    FAILED --> PROCESSING : System / Retry AI analysis
    AI_COMPLETED --> UNDER_REVIEW : Authorized Reviewer / Start review
}
[*] --> SUBMITTED : Farmer / Submit diagnosis request
AI_COMPLETED --> VERIFIED : Authorized Reviewer / CONFIRMED or CORRECTED
UNDER_REVIEW --> VERIFIED : Authorized Reviewer / CONFIRMED or CORRECTED
FAILED --> VERIFIED : Authorized Reviewer / Manual diagnosis\n[CORRECTED]
AI_COMPLETED --> INCONCLUSIVE : Authorized Reviewer / INCONCLUSIVE
UNDER_REVIEW --> INCONCLUSIVE : Authorized Reviewer / INCONCLUSIVE
FAILED --> INCONCLUSIVE : Authorized Reviewer / INCONCLUSIVE
VERIFIED --> VERIFIED : Authorized Reviewer / Review again\n[definitive diagnosis]
VERIFIED --> INCONCLUSIVE : Authorized Reviewer / Review again\n[insufficient evidence]
INCONCLUSIVE --> VERIFIED : Authorized Reviewer / Review again\n[definitive diagnosis]
INCONCLUSIVE --> INCONCLUSIVE : Authorized Reviewer / Review again\n[still insufficient evidence]
VERIFIED --> VERIFIED : Authorized Reviewer / Approve recommendations
BEFORE_REVIEW --> CANCELLED : Farmer or Staff / Cancel before review completion
CANCELLED --> [*]
@enduml
```

## 21-ai-model - AI Model

Source: [puml/21-ai-model.puml](puml/21-ai-model.puml)

A retired model version is not reactivated by the current Domain lifecycle.

```plantuml
@startuml 21-ai-model
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title AI Model

[*] --> DRAFT : Authorized Staff / Register model version
DRAFT --> ACTIVE : Authorized Staff / Activate deployment
ACTIVE --> RETIRED : Authorized Staff / Retire model version
RETIRED --> [*]
@enduml
```

## 22-ai-policy-config - AI Policy Config

Source: [puml/22-ai-policy-config.puml](puml/22-ai-policy-config.puml)

Inference requires an active policy of the selected active model, effective at inference time. Policies never bypass mandatory Human Review for recommendations.

```plantuml
@startuml 22-ai-policy-config
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title AI Policy Config

[*] --> DRAFT : Authorized Staff / Create policy
DRAFT --> ACTIVE : Authorized Staff / Activate policy
ACTIVE --> INACTIVE : Authorized Staff / Deactivate policy
INACTIVE --> ACTIVE : Authorized Staff / Reactivate policy
@enduml
```

## 23-product - Product

Source: [puml/23-product.puml](puml/23-product.puml)

Representative administrative transitions. Product, StoreProduct, packaging, prices and eligible inventory jointly determine sellability. DISCONTINUED is not enforced as an irreversible terminal state by the Domain setter.

```plantuml
@startuml 23-product
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Product

[*] --> ACTIVE : Staff / Create active product
ACTIVE --> INACTIVE : Staff / Temporarily stop selling
INACTIVE --> ACTIVE : Staff / Resume selling
ACTIVE --> DISCONTINUED : Staff / Discontinue product
INACTIVE --> DISCONTINUED : Staff / Discontinue product
DISCONTINUED --> ACTIVE : Staff / Restore as active\n[permitted]
DISCONTINUED --> INACTIVE : Staff / Restore as inactive\n[permitted]
@enduml
```

## 24-price-list - Price List

Source: [puml/24-price-list.puml](puml/24-price-list.puml)

Actual price applicability also requires valid effective dates and customer-group assignments.

```plantuml
@startuml 24-price-list
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Price List

[*] --> DRAFT : Staff / Create price list
DRAFT --> ACTIVE : Staff / Activate price list
ACTIVE --> INACTIVE : Staff / Deactivate price list
INACTIVE --> ACTIVE : Staff / Reactivate price list
@enduml
```

## 25-article - Article

Source: [puml/25-article.puml](puml/25-article.puml)

The current design allows moving articles to draft, publishing and archiving without imposing an irreversible final state.

```plantuml
@startuml 25-article
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Article

[*] --> DRAFT : Staff / Create article
DRAFT --> PUBLISHED : Staff / Publish article
PUBLISHED --> DRAFT : Staff / Move to draft
DRAFT --> ARCHIVED : Staff / Archive article
PUBLISHED --> ARCHIVED : Staff / Archive article
ARCHIVED --> DRAFT : Staff / Restore as draft
ARCHIVED --> PUBLISHED : Staff / Publish again
@enduml
```

## 26-contact-request - Contact Request

Source: [puml/26-contact-request.puml](puml/26-contact-request.puml)

This is the usual handling flow, not an exhaustive transition restriction. The design does not impose a fixed state graph; resolving records resolved_at.

```plantuml
@startuml 26-contact-request
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Contact Request

[*] --> OPEN : User / Submit contact request
OPEN --> IN_PROGRESS : Staff / Start processing
OPEN --> RESOLVED : Staff / Resolve directly
OPEN --> CLOSED : Staff / Close request
IN_PROGRESS --> RESOLVED : Staff / Complete resolution
IN_PROGRESS --> CLOSED : Staff / Close request
RESOLVED --> CLOSED : Staff / Close after resolution
CLOSED --> OPEN : Staff / Reopen if permitted
@enduml
```

## 27-notification - Notification

Source: [puml/27-notification.puml](puml/27-notification.puml)

Reading records read_at. Archiving is a semantic lifecycle operation, not physical deletion.

```plantuml
@startuml 27-notification
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Notification

[*] --> UNREAD : System / Create notification
UNREAD --> READ : User / Read notification
UNREAD --> ARCHIVED : User / Archive notification
READ --> ARCHIVED : User / Archive notification
ARCHIVED --> [*]
@enduml
```

## 28-notification-outbox - Notification Outbox

Source: [puml/28-notification-outbox.puml](puml/28-notification-outbox.puml)

Operational conditions derived from ProcessedAt, AvailableAt and attempt count, not a stored status enum. Retries use increasing delays.

```plantuml
@startuml 28-notification-outbox
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Notification Outbox

[*] --> WAITING : System / Write event to outbox
WAITING --> PROCESSING : Worker / Pick up event\n[available time reached]
PROCESSING --> PROCESSED : Worker / Complete processing
PROCESSING --> RETRY_WAIT : Worker / Record failure\nand increment attempt count
RETRY_WAIT --> PROCESSING : Worker / Retry\n[next available time reached]
PROCESSED --> [*]
@enduml
```

## 29-payment-allocation - Payment Allocation

Source: [puml/29-payment-allocation.puml](puml/29-payment-allocation.puml)

An ORDER allocation cannot be reversed once prepayment has been consumed. ORDER_PAYMENT permits ORDER allocations; DEBT_REPAYMENT permits DEBT allocations.

```plantuml
@startuml 29-payment-allocation
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Payment Allocation

[*] --> ACTIVE : System / Allocate funds\n[payment is PAID]
ACTIVE --> ACTIVE : Owning use case / Consume or release prepayment\n[operation is valid]
ACTIVE --> REVERSED : Owning use case / Reverse allocation\n[reversal conditions satisfied]
REVERSED --> [*]
@enduml
```

## 30-debt-transaction - Debt Transaction

Source: [puml/30-debt-transaction.puml](puml/30-debt-transaction.puml)

Posted financial history is preserved; reversal is a semantic operation with corresponding ledger effects, never physical deletion.

```plantuml
@startuml 30-debt-transaction
!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}

title Debt Transaction

[*] --> POSTED : Owning use case / Post receivable transaction
POSTED --> REVERSED : Owning use case / Reverse transaction\n[corresponding reversal posting recorded]
REVERSED --> [*]
@enduml
```
