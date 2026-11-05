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
→ result shown to the Farmer at once (no Human Review since 2026-10-21)
→ CONFIDENT: disease, treatments, approved linked products (at most 5, with price and stock)
→ UNCERTAIN: top guess, non-chemical guidance, no product, retake / ask the store
→ FAILED: the Farmer retries
```

Products are suggested only for a CONFIDENT, non-Healthy result (`api-flows/AI_DIAGNOSIS.md` 2.0, D3, D13).

## Atomicity Principle

If a workflow changes inventory + order + payment/credit/debt together,
one Application use case owns one transaction boundary.

## Advisory: problem to products to cart (2026-11-02)

```text
Farmer types a problem ("dao on", "vàng lá")   or   comes from a CONFIDENT diagnosis
→ GET  /api/me/advisory/problems          (accent-insensitive search)
→ POST /api/me/advisory/recommendations   (problem, area in m², growth stage)
→ approved disease_products links → sellable store products → price list of the Farmer → stock status
→ protection products (registration source, dose, pre-harvest days) + fertilizers (reason)
→ quantity and packagings for the area → POST /api/me/cart/items per line
→ normal checkout, payment, delivery
→ advisory_sessions logged; staff see sessions and boughtWithin7Days
```

Links are created by staff (DRAFT) and approved by a reviewer (APPROVED); a model or free text never names a product.

## Debt by crop season (2026-11-03)

```text
Owner enters the crop seasons (consecutive) and the store debt policy
→ credit order CONFIRMED: joins the season open for orders that Vietnam day (orders.crop_season_id)
→ FULFILLMENT (pickup / delivery): debt entry created, origin season = collection season,
  due date = season payment due date (capped by the policy) — a delivery after it: delivery day + credit-tier term
→ before the due date the Farmer can pay or dispute; N1/N2/N3/N4/N5 reminders (after the owner started reminders)
→ unpaid and late: the owner decides (rollover to a later season, reduce / cancel with a reason, keep, or raise the limit);
  Sale staff may roll over within the store limits
→ season COLLECTING over: closing-check → owner closes it (frozen figures) → debt-by-season report, limit suggestions
```

Rollover keeps the amount, the origin season and the first due date; only the collection season, the due date and the rollover count change. The system never locks credit by itself. Contract: `docs/reference/api-flows/FLOW_3_CREDIT_DEBT.md` §8b–8i.
