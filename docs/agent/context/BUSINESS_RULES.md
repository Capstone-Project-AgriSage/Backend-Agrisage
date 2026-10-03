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
23. Credit Tier may default payment terms; confirmed Credit Order snapshots term days. The debt term follows the customer type: a Customer Group may carry a default Credit Tier, a new Credit Profile takes its tier from the group, and a group change moves the profile to the new group's tier (limit unchanged, history kept; design §35.20). No crop-season terms.
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
36. AI inference must be Human Reviewed: CONFIRMED / CORRECTED / INCONCLUSIVE.
37. Only verified diagnosis may create treatment/product recommendations.
38. Reports include Sales Revenue, COGS and Gross Profit; COGS uses actual fulfilled lot cost.
39. No VAT/e-invoice integration in MVP; supplier invoice reference/file is allowed.
40. Every DELETE API is soft delete or semantic cancel/void/reverse; no business hard delete.
41. One primary role per user; extra permission comes through membership (e.g. `can_review_ai`).
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
59. A Diagnosis Case can be Human Reviewed after AI completes, after AI fails (manual diagnosis), or re-reviewed later; a new review supersedes the old one and deactivates its recommendations. A case with no successful inference cannot be CONFIRMED; CONFIRMED with a primary inference must keep the AI's predicted disease.
60. Recommendations require a VERIFIED case (whatever `requires_human_review` says) and target exactly one active treatment of the final disease or one active, sellable store product; a Healthy result gets treatment guidance only, no product.
61. Audit Logs are append-only: never updated or deleted.

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
