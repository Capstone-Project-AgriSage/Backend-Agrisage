# Frozen Business Rules — Agent Context

Use this summary for navigation. For exact schema implications, open the relevant sections in
`docs/reference/DATABASE_DESIGN.md`.

1. Registered tracked customers use Farmer Accounts; Walk-in sales are allowed.
2. Walk-in customers do not receive individual credit/debt.
3. A Farmer belongs to one current Customer Group; assignment history is retained.
4. Store Owner/Sales Staff manually assign Customer Group.
5. Staff may override Order Item price; preserve suggested price, actual price, actor, reason.
6. No sale smaller than allowed base packaging; no partial ml/kg from an opened package.
7. Packaging conversion to base unit is fixed per Product and snapshotted in transactions.
8. Goods Receipt Excel MVP uses the AgriSage standard template.
9. Receipt workflow: Upload → Preview → Validate → Correct → Confirm.
10. Confirmed Goods Receipt is immutable; corrections use reversal/adjustment.
11. Lot/expiry may be nullable generally; products requiring expiry enforce it at receiving.
12. Same Product + normalized Lot Number + Expiry Date represents one logical Inventory Lot.
13. Expired lots cannot be reserved, allocated, or sold; near-expiry is warned/reported.
14. Confirmed eligible orders reserve inventory; FEFO suggests the lot.
15. Staff confirms the actual physical lot; oversell is prevented.
16. Fulfillment supports PICKUP and DELIVERY.
17. Pickup does not require a Delivery record; actual handover triggers stock/financial posting.
18. Partial fulfillment is supported.
19. A Delivery may have multiple attempts and partial success.
20. Delivery incidents record exact affected lot/quantity/reason/resolution.
21. Successful delivery requires at least one proof image.
22. Store Owner/Sales Staff can change credit limit; keep history and audit.
23. Credit Tier may default payment terms; confirmed Credit Order snapshots term days. The debt term follows the customer type: a Customer Group may carry a default Credit Tier, a new Credit Profile takes its tier from the group, and a group change moves the profile to the new group's tier (limit unchanged, history kept; design §35.20). No crop-season terms. **Since 2026-11-03 (rules 67–74) a credit order also joins a crop season and the due date of its debt follows the season; the tier term applies to debt without a season and to deliveries after the season due date.**
24. No interest/penalty in MVP; overdue status/day count/alerts/reports only.
25. Upfront prepayment is consumed against successful fulfillment chronologically.
26. Payment↔Debt is many-to-many through `payment_allocations`.
27. Unspecified debt repayment defaults to oldest due date first; staff may adjust allocation.
28. Debt is created only on the successfully fulfilled unpaid credit portion.
29. Farmer may dispute debt; staff can KEEP / ADJUST / CANCEL according to workflow.
30. Return/Refund supports full/partial post-fulfillment returns with exact source/lot traceability.
31. Resellable returns create RETURN_IN; damaged/expired/unusable goods do not become available stock.
32. Return value reduces attributable unpaid receivable first; already-paid remainder is refunded.
33. No automated payOS refund in MVP; external refund is recorded.
34. Product review requires purchase + successful fulfillment; max one review per Order Item/Farmer.
35. Production AI scope: Rice — Leaf Blast, Bacterial Leaf Blight, Brown Spot, Sheath Blight, Healthy.
36. No Human Review (since 2026-10-21, AI_DIAGNOSIS.md 2.0): the Farmer sees the result of the latest successful AI inference at once, marked CONFIDENT (policy passed) or UNCERTAIN (not passed), never as a probability.
37. Product suggestions for a CONFIDENT, non-Healthy AI result come only from reviewed links: the `APPROVED` `PROTECTION` rows of `disease_products` for the predicted disease, limited to store products the catalog sells and that have a price and stock (at most 5, computed at read time). An active-ingredient match alone never offers a product. An UNCERTAIN result shows no CHEMICAL treatment and no product.
38. Reports include Sales Revenue, COGS and Gross Profit; COGS uses actual fulfilled lot cost.
39. No VAT/e-invoice integration in MVP; supplier invoice reference/file is allowed.
40. Every DELETE API is soft delete or semantic cancel/void/reverse; no business hard delete.
41. One primary role per user; extra permission comes through membership. (`can_review_ai` is kept in the schema but has no effect since 2026-10-21.)
42. Keep `stores`; expect one active Store; never hard-code Store ID.
43. Inventory costing is Weighted Average Cost per logical Inventory Lot.
44. Promotions are removed from scope/database.
45. Unit-cost divisions (numeric(20,6)) round AwayFromZero to 6 decimals; money (numeric(18,2)) inputs beyond 2 decimals are rejected, not rounded.
46. The final issuance that empties an Inventory Lot takes the Lot's exact remaining cost value as COGS, then resets total_cost_value to zero.
47. Inventory/Credit Reservation status is derived from reserved/consumed/released quantities, not set independently; a partial release of the unused remainder does not change an already-PARTIALLY_CONSUMED/CONSUMED status.
48. Order Items may only be added, changed or removed while the Order is PENDING_CONFIRMATION.
49. Order/Order Item/Delivery Item status is derived from fulfilled vs cancelled quantity; PARTIALLY_CANCELLED is terminal once a line has both fulfilled and cancelled quantity, and full Order cancellation is only possible before any fulfillment.
50. A Delivery may be redispatched from PARTIALLY_DELIVERED or RETRY_PENDING; only one Attempt is IN_PROGRESS at a time, and changing a Delivery Item's actual Lot before dispatch releases the old allocation and creates a new one rather than mutating it in place.
51. Debt Entry ADJUST only decreases outstanding amount; increasing a Farmer's receivable requires a new MANUAL_ADJUSTMENT Debt Entry. DISPUTE does not block further payment allocation against the Entry.
52. A Payment Allocation's type is bound to the Payment's context: ORDER_PAYMENT allocates only to an Order, DEBT_REPAYMENT only to a Debt Entry; an Order allocation cannot be reversed once its prepayment has been consumed.
53. Stocktake completion requires every counted line to have a recorded quantity; an uncounted line blocks completion.
54. Cancelling a Delivery with nothing yet delivered ends it CANCELLED; cancelling one that already delivered part of the goods ends it DELIVERED, with the cancelled remainder shown as PARTIALLY_CANCELLED on the affected Delivery Items instead.
55. Sales Return flow: REQUESTED → APPROVED/REJECTED/CANCELLED → RECEIVED → INSPECTED → PARTIALLY_RESOLVED → COMPLETED; lines change only while REQUESTED. COMPLETED requires every RESTOCK line linked to its RETURN_IN and completed Refunds equal to the refund total.
56. Returned quantity ≤ fulfilled − already returned, where "already returned" counts every other non-rejected, non-cancelled return of the same Order Item; each return line references exactly one fulfillment source matching the Order's fulfillment type (DELIVERY → lot allocation, PICKUP → original sale stock movement item).
57. Return value = returned base quantity × original unit price ÷ conversion, rounded AwayFromZero to 2 decimals (the only rounded computed money amount).
58. Inspection fixes the disposition: RESELLABLE → RESTOCK; DAMAGED/EXPIRED/UNUSABLE → WRITE_OFF. Settlement: debt adjustment + refund = total return value; refunds never exceed the refund total.
59. A Diagnosis Case that FAILED (or stayed SUBMITTED after a crash) can be retried by its Farmer; a retry appends a new inference, earlier ones stay. Cases reviewed before 2026-10-21 (VERIFIED / INCONCLUSIVE) keep their review and stay readable; no new review is created.
60. `recommendation_items` are no longer written (they need a review); suggestions are computed, and the suggested store product ids are recorded in a `DIAGNOSIS_AI_RESULT` audit entry for each successful AI run. A Healthy result gets treatment guidance only, no product. `requires_human_review` is stored and ignored.
61. Audit Logs are append-only: never updated or deleted.
62. A product is offered for a problem (disease, pest, weed, nutrition, growth) only through an explicit link in `disease_products` that a person approved. `DRAFT` and `INACTIVE` links are never shown to a Farmer; changing the content of an approved link returns it to `DRAFT`; a crop-protection link names its registration (document and page), a fertilizer link gives its reason.
63. Free text and photos only select a problem. No model or text search names a product; products come from the links.
64. Advice quantity is computed in base units from the area, the dose per hectare and the number of applications, rounded up, priced with the Farmer's price list. The response gives a stock status only, never a stock quantity. The Healthy class gets no advice.
65. Every recommendation is logged in `advisory_sessions` (with its items); the log is for tracing and statistics and is not a cart. Adding to the cart uses the normal cart; a recommendation reserves no stock.
66. The five seeded AI classes are never deactivated and keep their code and healthy flag; a new problem is never a healthy class.
67. Crop seasons (L3): a Store has consecutive crop seasons (no gap, no overlap; the first gives its order window start, each later one opens on the previous harvest start; new ones are added only after the last). Only the owner (ADMIN / STORE_OWNER) creates or edits them. A season's phase (PLANNED, OPEN, HARVESTING, COLLECTING, CLOSED) comes from the Vietnam date; payment due date = harvest end + grace days. A closed season keeps its frozen figures and accepts no debt, rollover or edit.
68. A credit order joins the season whose order window contains the Vietnam day of its confirmation, and keeps it. The debt arises on fulfillment; each debt entry keeps the season it arose in (`origin`, fixed) and the season it is collected in (`collection`, moves on a rollover) and its first due date.
69. Due date of a debt = the season's payment due date (shortened by the store's optional term cap); a delivery after that date is due on the delivery day plus the credit-tier term; without a season the tier term applies. A recorded due date is never silently changed by a later change to a season or a tier; the owner may explicitly apply a season's new later date to the entries being collected (disputed, paid and cancelled entries are skipped).
70. Rollover (extension) moves an unpaid, undisputed entry to a later open season with a later due date, a hardship reason code (MAT_MUA, THIEN_TAI, KHO_KHAN, KHAC) and a note; it never changes the amount, status, origin season or first due date. Sale staff may roll over while the entry has fewer rollovers than the store limit and within the total-extension limit; beyond that only the owner, who may override the extension limit explicitly. Reducing or cancelling debt carries an optional reason code until the team decides to require it.
71. The system never locks or limits a customer's credit by itself because of overdue debt; locking, suspending and reactivating credit are the owner's acts with a reason. Overdue debt still counts against the credit limit.
72. Only ADMIN and STORE_OWNER may reduce or cancel debt, record a manual debt, change a due date directly, confirm or reject a bank repayment, or suspend, block or reactivate credit; these permissions cannot be delegated to staff.
73. Reminders: N1 (harvest start of the collection season), N2 (7 days before the due date), N3 (due date), N4 (+14) and N5 (+30) by the store policy; the latest milestone only, once per Farmer and group, never for disputed, paid or cancelled entries. Nothing is sent until the owner starts reminders; milestones before that day are never sent. Staff and owner get a summary per day and milestone counted in debt groups.
74. Closing a season needs its harvest to have ended, no entry still being collected in it (disputed included) and no unfinished credit order of the season; the figures at that moment are frozen. The debt-by-season report and the credit-limit suggestions (customers on time for the required number of closed seasons) only read; the system never changes a limit by itself.

## Financial / Inventory Invariants

```text
available stock = quantity_on_hand - quantity_reserved

current lot average cost =
total_cost_value / quantity_on_hand
when quantity_on_hand > 0

available credit =
credit_limit
- debt_account.current_balance
- remaining active credit reservations
```

When a lot's physical quantity reaches zero, its `total_cost_value` is explicitly reset to zero.

## Important Distinction

A delivery refusal before successful fulfillment is a Delivery Incident, not a Sales Return.
A Sales Return is a post-fulfillment operation.
