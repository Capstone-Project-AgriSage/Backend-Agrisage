# Core Workflow Map — Agent Context

Use this file to locate cross-module dependencies. For exact posting rules, open DATABASE_DESIGN.

## Supplier Receiving

```text
Supplier
→ Goods Receipt DRAFT
→ manual/template input
→ validate Product + Packaging + Lot/Expiry
→ Confirm
→ resolve/create logical Inventory Lot
→ STOCK_IN movement
→ update Lot Balance
→ update Weighted Average Cost
```

Transaction owner: Goods Receipt use case.

## Order / Pricing / Credit

```text
Farmer/Walk-in
→ Cart/Counter Order
→ determine Customer Group / Price List
→ snapshot suggested and actual price
→ FULL_PAYMENT or CREDIT settlement
→ reserve inventory
→ reserve credit when required
→ Confirm
```

Walk-in cannot use CREDIT.
Credit term = tier of the Farmer's credit profile; the tier follows the Customer Group (design §35.20).

## Quick Counter Sale

```text
Counter customer (walk-in or registered)
→ preview: prices + FEFO lots (nothing saved)
→ staff confirm the actual lots
→ one transaction: Order → cash Payment PAID + ORDER allocation
  → confirm (reserve given lots) → SALE Stock Movement → Order COMPLETED
```

Transaction owner: `POST /api/counter-sales` (F1.7); it reuses the single-step shared steps, which never save
or commit (`docs/reference/api-flows/README.md` §3.2).

## Delivery Fulfillment

```text
Confirmed Order
→ FEFO suggestion
→ staff confirms actual Lot
→ Delivery / Attempt
→ successful quantity
→ SALE Stock Movement
→ consume Inventory Reservation
→ consume Order prepayment
→ consume Credit Reservation
→ create Debt Entry for unpaid credit portion
→ create Debt Transaction
→ update Delivery + Order
```

Transaction owner: Delivery/Fulfillment use case.

## Pickup Fulfillment

```text
Ready Pickup
→ staff confirms actual Lot
→ physical handover
→ SALE Stock Movement
→ reservation consumption
→ payment/credit/debt posting
→ update Order
```

No Delivery record is required.

## Debt Collection

```text
Confirmed Payment
→ Payment Allocation(s)
→ Debt Entry allocation
→ Debt Transaction PAYMENT
→ Debt Entry outstanding update
→ Debt Account balance update
```

Default unallocated repayment priority: oldest due date first.

## Return / Refund

```text
Post-fulfillment Return
→ exact original Order Item / Lot source
→ inspection
→ RETURN_IN only if resellable
→ reduce attributable unpaid debt first
→ refund already-paid remainder
→ audit
```

Delivery refusal before fulfillment is handled as Delivery Incident instead.

## AI Diagnosis

```text
Farmer Diagnosis Case
→ upload image(s)
→ AI inference using exact Model + Policy
→ authorized Human Review
→ CONFIRMED / CORRECTED / INCONCLUSIVE
→ verified diagnosis
→ treatment/product recommendation
```

No commercial recommendation from an unverified AI inference.

## Atomicity Principle

If a workflow changes inventory + order + payment/credit/debt together,
one Application use case owns one transaction boundary.
