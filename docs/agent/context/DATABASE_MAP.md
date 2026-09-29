# Database Map — Agent Context

Authoritative source: `docs/reference/DATABASE_DESIGN.md`.

Do not implement fields/FK/constraints from this summary alone.
Use it to locate the correct domain, then open the exact authoritative table section.

## 67 Tables

### Authentication & Store — 1–6
1. roles
2. users
3. farmer_profiles
4. user_addresses
5. stores
6. store_members

### Customer — 7–8
7. customer_groups
8. customer_group_assignments

### Catalog — 9–17
9. categories
10. brands
11. products
12. units
13. product_packagings
14. active_ingredients
15. product_active_ingredients
16. store_products
17. product_reviews

### Pricing — 18–20
18. price_lists
19. price_list_items
20. customer_group_price_lists

### Receiving — 21–23
21. suppliers
22. goods_receipts
23. goods_receipt_items

### Inventory — 24–31
24. inventory_lots
25. inventory_lot_balances
26. stock_movements
27. stock_movement_items
28. stocktakes
29. stocktake_items
30. inventory_reservations
31. inventory_reservation_items

### Sales / Payment — 32–37
32. carts
33. cart_items
34. orders
35. order_items
36. payments
37. payment_allocations

### Delivery — 38–43
38. deliveries
39. delivery_items
40. delivery_item_lot_allocations
41. delivery_attempts
42. delivery_attempt_items
43. delivery_incidents

### Credit — 44–47
44. credit_tiers
45. farmer_credit_profiles
46. credit_limit_histories
47. credit_reservations

### Accounts Receivable / Debt — 48–51
48. debt_accounts
49. debt_entries
50. debt_entry_actions
51. debt_transactions

### Return / Refund — 52–54
52. sales_returns
53. sales_return_items
54. refunds

### AI Diagnosis — 55–63
55. diseases
56. disease_treatments
57. ai_models
58. ai_policy_configs
59. diagnosis_cases
60. diagnosis_images
61. ai_inferences
62. agent_reviews
63. recommendation_items

### Content / System — 64–67
64. articles
65. contact_requests
66. notifications
67. audit_logs

## Global DB Conventions

```text
PK                   → UUID
business timestamps  → timestamptz
business dates       → date
money                 → numeric(18,2)
internal unit cost    → numeric(20,6)
inventory quantities → bigint
status                → varchar + app enum/check
soft delete           → deleted_at + deleted_by where applicable
```

## Hot Sections in DATABASE_DESIGN.md

Use heading search rather than reading the full file:

```text
GLOBAL DATABASE CONVENTIONS
INVENTORY POSTING RULES
WEIGHTED AVERAGE COST RULE
END-TO-END ORDER → CREDIT → DELIVERY → DEBT FLOW
DELIVERY + INVENTORY ATOMIC POSTING
PICKUP AT STORE
CREDIT EXPOSURE RULE
RETURN / REFUND POSTING FLOW
FINAL CORE BUSINESS FLOWS
IMPORTANT DATABASE INVARIANTS
MIGRATION-CANDIDATE RELATIONSHIP / FK POLICY
MIGRATION-CANDIDATE INDEX PLAN
RULES ENFORCED IN APPLICATION / DOMAIN LAYER
```

## Schema Implementation Rule

```text
Design
→ Entity
→ IEntityTypeConfiguration
→ Migration
→ review
→ Supabase
```

Never infer a column/FK/index from a stale summary when exact DB design is available.
