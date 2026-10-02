# AgriSage – Detailed Database Design v4 – Migration Candidate

> **Database:** PostgreSQL (Supabase)  
> **ORM:** Entity Framework Core + Npgsql  
> **Design approach:** Detailed Database Design First → EF Core Code First → Migrations → Supabase  
> **Current baseline:** 67 tables (`promotions` removed; `delivery_attempt_items` added)  
> **Review status:** Relationship + FK + Constraint + Index review applied.  
> **Candidate status:** Ready for final approval before EF Core entities / Initial Migration.  
> **This document currently details tables 1–31.**  
> **Confirmed inventory costing method:** Weighted Average Cost **per logical Inventory Lot**.

---

# 0. GLOBAL DATABASE CONVENTIONS

## 0.1 Primary keys

All main tables use:

```sql
id uuid PRIMARY KEY DEFAULT gen_random_uuid()
```

## 0.2 Date and time

Use `timestamptz` for timestamps and `date` for business dates such as manufacturing, expiry, and due dates.

## 0.3 Money and cost

Normal monetary values:

```sql
numeric(18,2)
```

Internal base-unit costs:

```sql
numeric(20,6)
```

## 0.4 Quantity

The system does **not** sell fractions smaller than the base unit.

Use:

```sql
bigint
```

for all quantity/base-quantity fields.

## 0.5 Status values

Use `varchar(...)` with application enums and appropriate `CHECK` constraints.

## 0.6 Soft delete

All API DELETE operations use **soft delete**.

Typical fields:

```text
deleted_at      timestamptz NULL
deleted_by      uuid NULL FK users.id
```

For transaction records, use business states such as:

```text
CANCELLED
VOIDED
REVERSED
```

instead of physical deletion.

## 0.7 Audit fields

Most mutable tables include:

```text
created_at
updated_at
deleted_at
deleted_by
```

Transaction records may additionally include business actors such as:

```text
created_by
confirmed_by
cancelled_by
completed_by
```

## 0.8 Inventory concurrency

Inventory confirmation, reservation, stock-out and stocktake adjustment must execute inside database transactions.

`inventory_lot_balances` includes:

```text
version bigint NOT NULL DEFAULT 0
```

to support optimistic concurrency and prevent overselling / double reservation.

---

# I. AUTHENTICATION & STORE

## 1. `roles`

```text
roles
-----
id                  uuid PK
code                varchar(30) NOT NULL
name                varchar(100) NOT NULL
description         varchar(500) NULL
is_active           boolean NOT NULL DEFAULT true

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Allowed codes:

```text
FARMER
STORE_OWNER
SALES_STAFF
DELIVERY_STAFF
ADMIN
```

Constraint:

```text
UNIQUE(code) for active records
```

---

## 2. `users`

```text
users
-----
id                  uuid PK
role_id             uuid NOT NULL FK roles.id

email               varchar(255) NULL
phone_number        varchar(20) NULL
password_hash       varchar(500) NOT NULL

full_name           varchar(150) NOT NULL
avatar_url          varchar(1000) NULL

status              varchar(30) NOT NULL
email_verified      boolean NOT NULL DEFAULT false
phone_verified      boolean NOT NULL DEFAULT false

last_login_at       timestamptz NULL

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
INACTIVE
SUSPENDED
LOCKED
```

Rules:

```text
At least one of email / phone_number must exist.
Each user has exactly one primary role.
```

Recommended indexes:

```text
UNIQUE LOWER(email)
WHERE deleted_at IS NULL AND email IS NOT NULL

UNIQUE phone_number
WHERE deleted_at IS NULL AND phone_number IS NOT NULL
```

---

## 3. `farmer_profiles`

```text
farmer_profiles
---------------
id                  uuid PK
user_id             uuid NOT NULL FK users.id

date_of_birth       date NULL
gender              varchar(20) NULL
notes               varchar(1000) NULL

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Constraint:

```text
UNIQUE(user_id)
```

Application rule:

```text
users.role = FARMER
```

Walk-in customers do not require Farmer Profile records.

---

## 4. `user_addresses`

```text
user_addresses
--------------
id                  uuid PK
user_id             uuid NOT NULL FK users.id

recipient_name      varchar(150) NOT NULL
recipient_phone     varchar(20) NOT NULL

address_line        varchar(500) NOT NULL
ward                varchar(150) NULL
district            varchar(150) NULL
province            varchar(150) NOT NULL

latitude            numeric(10,7) NULL
longitude           numeric(10,7) NULL

address_type        varchar(20) NOT NULL
is_default          boolean NOT NULL DEFAULT false

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Address types:

```text
HOME
FARM
OTHER
```

Rule:

```text
Maximum one active default address per user.
```

---

## 5. `stores`

```text
stores
------
id                  uuid PK
code                varchar(30) NOT NULL
name                varchar(200) NOT NULL

phone_number        varchar(20) NULL
email               varchar(255) NULL
tax_code            varchar(50) NULL

address_line        varchar(500) NOT NULL
ward                varchar(150) NULL
district            varchar(150) NULL
province            varchar(150) NOT NULL

status              varchar(20) NOT NULL

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
INACTIVE
```

Constraint:

```text
UNIQUE(code)
```

Current application rule:

```text
Only one Store may be ACTIVE.
```

---

## 6. `store_members`

```text
store_members
-------------
id                  uuid PK
store_id            uuid NOT NULL FK stores.id
user_id             uuid NOT NULL FK users.id

employee_code       varchar(50) NULL
joined_at           date NULL
left_at             date NULL

status              varchar(20) NOT NULL
can_review_ai       boolean NOT NULL DEFAULT false

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
INACTIVE
LEFT
```

Constraint:

```text
UNIQUE(store_id, user_id)
```

Applicable roles:

```text
STORE_OWNER
SALES_STAFF
DELIVERY_STAFF
```

---

# II. CUSTOMER MANAGEMENT

## 7. `customer_groups`

```text
customer_groups
---------------
id                  uuid PK
store_id            uuid NOT NULL FK stores.id

code                varchar(30) NOT NULL
name                varchar(100) NOT NULL
description         varchar(500) NULL

priority            integer NOT NULL DEFAULT 0
is_default          boolean NOT NULL DEFAULT false
is_active           boolean NOT NULL DEFAULT true

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Example:

```text
NEW
REGULAR
LOYAL
VIP
```

Constraint:

```text
UNIQUE(store_id, code)
```

Only one active default group per Store.

---

## 8. `customer_group_assignments`

```text
customer_group_assignments
--------------------------
id                      uuid PK
farmer_profile_id       uuid NOT NULL FK farmer_profiles.id
customer_group_id       uuid NOT NULL FK customer_groups.id

effective_from          timestamptz NOT NULL
effective_to            timestamptz NULL

assigned_by             uuid NOT NULL FK users.id
reason                  varchar(500) NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Rules:

```text
One Farmer may have only one active Customer Group at a time.
effective_to IS NULL means current assignment.
```

---

# III. CATALOG & PRODUCT PACKAGING

## 9. `categories`

```text
categories
----------
id                  uuid PK
parent_id           uuid NULL FK categories.id

code                varchar(50) NOT NULL
name                varchar(150) NOT NULL
description         varchar(500) NULL

display_order       integer NOT NULL DEFAULT 0
is_active           boolean NOT NULL DEFAULT true

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

---

## 10. `brands`

```text
brands
------
id                  uuid PK

code                varchar(50) NULL
name                varchar(150) NOT NULL
description         varchar(500) NULL
logo_url            varchar(1000) NULL

is_active           boolean NOT NULL DEFAULT true

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Recommended unique active brand name.

---

## 11. `products`

```text
products
--------
id                      uuid PK
category_id             uuid NOT NULL FK categories.id
brand_id                uuid NULL FK brands.id

sku                     varchar(50) NOT NULL
name                    varchar(255) NOT NULL

description             text NULL
usage_instructions      text NULL

requires_lot_tracking   boolean NOT NULL DEFAULT true
requires_expiry_date    boolean NOT NULL DEFAULT true

image_url               varchar(1000) NULL
status                  varchar(20) NOT NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
INACTIVE
DISCONTINUED
```

Constraint:

```text
UNIQUE(sku)
```

---

## 12. `units`

```text
units
-----
id                  uuid PK
code                varchar(30) NOT NULL
name                varchar(100) NOT NULL
symbol              varchar(20) NULL

is_active           boolean NOT NULL DEFAULT true

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Examples:

```text
BOTTLE
BOX
CARTON
BAG
PACK
CAN
PIECE
```

Constraint:

```text
UNIQUE(code)
```

---

## 13. `product_packagings`

```text
product_packagings
------------------
id                      uuid PK
product_id              uuid NOT NULL FK products.id
unit_id                 uuid NOT NULL FK units.id

packaging_name          varchar(150) NULL
conversion_to_base      bigint NOT NULL

is_base_unit            boolean NOT NULL DEFAULT false
is_purchase_unit        boolean NOT NULL DEFAULT false
is_sale_unit            boolean NOT NULL DEFAULT false

barcode                 varchar(100) NULL
status                  varchar(20) NOT NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Rules:

```text
conversion_to_base > 0
One active base packaging per Product.
Base packaging must have conversion_to_base = 1.
```

Example:

```text
Bottle → 1
Box    → 6
Carton → 24
```

All conversions point directly to the base unit.

---

## 14. `active_ingredients`

```text
active_ingredients
------------------
id                  uuid PK

code                varchar(50) NULL
name                varchar(200) NOT NULL
description         text NULL

is_active           boolean NOT NULL DEFAULT true

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

---

## 15. `product_active_ingredients`

```text
product_active_ingredients
--------------------------
id                      uuid PK
product_id              uuid NOT NULL FK products.id
active_ingredient_id    uuid NOT NULL FK active_ingredients.id

concentration           varchar(100) NULL
note                    varchar(500) NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Constraint:

```text
UNIQUE(product_id, active_ingredient_id)
```

---

## 16. `store_products`

```text
store_products
--------------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id
product_id              uuid NOT NULL FK products.id

store_sku               varchar(50) NULL
min_stock_level_base    bigint NULL

is_sellable             boolean NOT NULL DEFAULT true
is_active               boolean NOT NULL DEFAULT true

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Constraint:

```text
UNIQUE(store_id, product_id)
```

---

## 17. `product_reviews`

```text
product_reviews
---------------
id                  uuid PK
farmer_profile_id   uuid NOT NULL FK farmer_profiles.id
store_product_id    uuid NOT NULL FK store_products.id
order_item_id       uuid NOT NULL FK order_items.id

rating              smallint NOT NULL
comment             text NULL
status              varchar(20) NOT NULL

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Constraints:

```text
rating BETWEEN 1 AND 5
UNIQUE(farmer_profile_id, order_item_id)
```

Review requires fulfilled quantity > 0.

---

# IV. PRICING

## 18. `price_lists`

```text
price_lists
-----------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id

code                    varchar(50) NOT NULL
name                    varchar(150) NOT NULL
description             varchar(500) NULL

effective_from          timestamptz NOT NULL
effective_to            timestamptz NULL

is_walk_in_default      boolean NOT NULL DEFAULT false
status                  varchar(20) NOT NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
DRAFT
ACTIVE
INACTIVE
```

---

## 19. `price_list_items`

```text
price_list_items
----------------
id                      uuid PK
price_list_id           uuid NOT NULL FK price_lists.id
store_product_id        uuid NOT NULL FK store_products.id
product_packaging_id    uuid NOT NULL FK product_packagings.id

selling_price           numeric(18,2) NOT NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Constraints:

```text
selling_price >= 0

UNIQUE(
    price_list_id,
    store_product_id,
    product_packaging_id
)
```

---

## 20. `customer_group_price_lists`

```text
customer_group_price_lists
--------------------------
id                      uuid PK
customer_group_id       uuid NOT NULL FK customer_groups.id
price_list_id           uuid NOT NULL FK price_lists.id

effective_from          timestamptz NOT NULL
effective_to            timestamptz NULL

assigned_by             uuid NOT NULL FK users.id

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Rule:

```text
One Customer Group has only one active Price List at a time.
```

---

# V. SUPPLIER & STOCK RECEIVING

## 21. `suppliers`

```text
suppliers
---------
id                  uuid PK
store_id            uuid NOT NULL FK stores.id

code                varchar(50) NULL
name                varchar(255) NOT NULL

tax_code            varchar(50) NULL
phone_number        varchar(20) NULL
email               varchar(255) NULL

contact_person      varchar(150) NULL

address_line        varchar(500) NULL
ward                varchar(150) NULL
district            varchar(150) NULL
province            varchar(150) NULL

note                varchar(1000) NULL
is_active           boolean NOT NULL DEFAULT true

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Recommended constraint:

```text
UNIQUE(store_id, code)
WHERE code IS NOT NULL AND deleted_at IS NULL
```

Rules:

```text
Supplier != Brand
A Product may historically be received from multiple Suppliers.
No Purchase Order workflow is managed by AgriSage.
```

---

## 22. `goods_receipts`

```text
goods_receipts
--------------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id
supplier_id             uuid NOT NULL FK suppliers.id

receipt_number          varchar(50) NOT NULL

supplier_invoice_number varchar(100) NULL
supplier_invoice_date   date NULL

received_at             timestamptz NOT NULL
received_by             uuid NOT NULL FK users.id

source_type             varchar(20) NOT NULL
source_file_name        varchar(255) NULL
source_file_url         varchar(1000) NULL

status                  varchar(20) NOT NULL

subtotal_amount         numeric(18,2) NOT NULL DEFAULT 0
total_amount            numeric(18,2) NOT NULL DEFAULT 0

note                    varchar(1000) NULL

confirmed_at            timestamptz NULL
confirmed_by            uuid NULL FK users.id

cancelled_at            timestamptz NULL
cancelled_by            uuid NULL FK users.id
cancel_reason           varchar(1000) NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

`source_type`:

```text
MANUAL
EXCEL_TEMPLATE
```

Statuses:

```text
DRAFT
CONFIRMED
CANCELLED
```

Constraints:

```text
UNIQUE(store_id, receipt_number)
subtotal_amount >= 0
total_amount >= 0
```

Business rules:

```text
Only DRAFT receipts may be directly edited.

CONFIRMED receipts are immutable with respect to inventory-affecting fields.

Errors after confirmation are corrected through reversal / adjustment.
```

Excel workflow:

```text
AgriSage Excel Template
→ Upload
→ Parse
→ Populate DRAFT Receipt
→ Preview
→ Validate
→ Correct
→ Confirm
```

---

## 23. `goods_receipt_items`

```text
goods_receipt_items
-------------------
id                              uuid PK
goods_receipt_id                uuid NOT NULL FK goods_receipts.id
store_product_id                uuid NOT NULL FK store_products.id
product_packaging_id            uuid NOT NULL FK product_packagings.id

received_quantity               bigint NOT NULL

conversion_to_base_snapshot     bigint NOT NULL
base_quantity                   bigint NOT NULL

purchase_unit_cost              numeric(18,2) NOT NULL
base_unit_cost                  numeric(20,6) NOT NULL
line_total_amount               numeric(18,2) NOT NULL

supplier_lot_number             varchar(100) NULL
manufacturing_date              date NULL
expiry_date                     date NULL

inventory_lot_id                uuid NULL FK inventory_lots.id

note                            varchar(500) NULL

created_at                      timestamptz NOT NULL
updated_at                      timestamptz NOT NULL
deleted_at                      timestamptz NULL
deleted_by                      uuid NULL FK users.id
```

Constraints:

```text
received_quantity > 0
conversion_to_base_snapshot > 0
purchase_unit_cost >= 0
base_unit_cost >= 0
line_total_amount >= 0
```

Calculations:

```text
base_quantity
=
received_quantity × conversion_to_base_snapshot
```

```text
base_unit_cost
=
purchase_unit_cost / conversion_to_base_snapshot
```

Lot validation:

```text
If products.requires_lot_tracking = true
→ supplier_lot_number required

If products.requires_expiry_date = true
→ expiry_date required
```

`inventory_lot_id` remains NULL while DRAFT and is resolved when Receipt is confirmed.

---

# VI. INVENTORY & LOT MANAGEMENT

## 24. `inventory_lots`


Represents one logical inventory lot/batch for a Store Product.

```text
inventory_lots
--------------
id                      uuid PK
store_product_id        uuid NOT NULL FK store_products.id

lot_number              varchar(100) NULL
manufacturing_date      date NULL
expiry_date             date NULL

status                  varchar(30) NOT NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
QUARANTINED
EXPIRED
BLOCKED
DEPLETED
```

Logical-lot rule for lot-tracked products:

```text
same store_product
+
same normalized lot_number
+
same expiry_date
=
same logical Inventory Lot
```

PostgreSQL uniqueness must handle `NULL expiry_date` correctly.

Recommended PostgreSQL 15+ unique index:

```sql
CREATE UNIQUE INDEX ux_inventory_lots_logical_lot
ON inventory_lots (
    store_product_id,
    lower(lot_number),
    expiry_date
) NULLS NOT DISTINCT
WHERE deleted_at IS NULL
  AND lot_number IS NOT NULL;
```

For products that do not require lot tracking, allow exactly one active no-lot bucket per Store Product:

```sql
CREATE UNIQUE INDEX ux_inventory_lots_no_lot_bucket
ON inventory_lots(store_product_id)
WHERE lot_number IS NULL
  AND deleted_at IS NULL;
```

Rules:

```text
expiry_date < current date
→ cannot reserve / allocate / sell

QUARANTINED / BLOCKED / EXPIRED
→ cannot reserve / allocate / sell
```

Near-expiry windows such as 30/60/90 days are calculated by query/report and are not stored as permanent flags.

Recommended indexes:

```text
(store_product_id, expiry_date)
(status, expiry_date)
```

## 25. `inventory_lot_balances`


Stores current quantity and carrying cost for each logical Inventory Lot.

```text
inventory_lot_balances
----------------------
id                      uuid PK
inventory_lot_id        uuid NOT NULL FK inventory_lots.id

quantity_on_hand        bigint NOT NULL DEFAULT 0
quantity_reserved       bigint NOT NULL DEFAULT 0

total_cost_value        numeric(20,6) NOT NULL DEFAULT 0

version                 bigint NOT NULL DEFAULT 0

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Constraints:

```text
UNIQUE(inventory_lot_id)

quantity_on_hand >= 0
quantity_reserved >= 0
quantity_reserved <= quantity_on_hand
total_cost_value >= 0
```

Derived availability:

```text
available_quantity
=
quantity_on_hand - quantity_reserved
```

Derived current Weighted Average Cost:

```text
average_unit_cost
=
total_cost_value / quantity_on_hand
```

when `quantity_on_hand > 0`.

Important invariant:

```text
quantity_on_hand = 0
→ total_cost_value = 0
```

Because decimal arithmetic may leave a tiny remainder, Backend must explicitly set:

```text
total_cost_value = 0
```

when the final physical unit leaves the Lot.

### Confirmed Weighted Average Cost

```text
Existing Lot:
100 Bottles
Total Cost = 10,000,000

Incoming:
100 Bottles × 110,000
Incoming Cost = 11,000,000

New Quantity = 200
New Total Cost = 21,000,000

Weighted Average Cost
= 105,000 / Bottle
```

`version` is incremented on every balance mutation for optimistic concurrency.

## 26. `stock_movements`


Header of every auditable physical inventory movement.

```text
stock_movements
---------------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id

movement_number         varchar(50) NOT NULL
movement_type           varchar(30) NOT NULL
status                  varchar(20) NOT NULL

occurred_at             timestamptz NOT NULL

goods_receipt_id        uuid NULL FK goods_receipts.id
order_id                uuid NULL FK orders.id
delivery_id             uuid NULL FK deliveries.id
stocktake_id            uuid NULL FK stocktakes.id
sales_return_id         uuid NULL FK sales_returns.id

reversal_of_movement_id uuid NULL FK stock_movements.id

reason_code             varchar(50) NULL
reason                  varchar(1000) NULL

created_by              uuid NOT NULL FK users.id

posted_at               timestamptz NULL
posted_by               uuid NULL FK users.id

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Movement types:

```text
STOCK_IN
SALE
RETURN_IN
ADJUSTMENT_IN
ADJUSTMENT_OUT
REVERSAL
```

Statuses:

```text
DRAFT
POSTED
REVERSED
CANCELLED
```

Rules:

```text
Only POSTED movements affect physical inventory.

POSTED movements and items are immutable.

Errors are corrected through REVERSAL / compensating movements.
```

Relationship note:

```text
Stocktake 1 → 0..N Stock Movements
```

`stock_movements.stocktake_id` is the only Stocktake↔Movement FK.  
There is no reverse `stocktakes.adjustment_movement_id`.

Recommended indexes:

```text
(store_id, occurred_at DESC)
(movement_type, occurred_at DESC)
(goods_receipt_id)
(order_id)
(delivery_id)
(stocktake_id)
(sales_return_id)
(reversal_of_movement_id)
```

## 27. `stock_movement_items`


Stores each lot-level quantity and cost movement.

```text
stock_movement_items
--------------------
id                      uuid PK
stock_movement_id       uuid NOT NULL FK stock_movements.id
inventory_lot_id        uuid NOT NULL FK inventory_lots.id

quantity_delta_base     bigint NOT NULL

unit_cost_snapshot      numeric(20,6) NOT NULL
total_cost_snapshot     numeric(20,6) NOT NULL

quantity_on_hand_after  bigint NULL
total_cost_value_after  numeric(20,6) NULL

note                    varchar(500) NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Constraints:

```text
quantity_delta_base <> 0
unit_cost_snapshot >= 0
total_cost_snapshot >= 0
```

Sign convention:

```text
positive quantity_delta_base → stock increase
negative quantity_delta_base → stock decrease
```

Cost rules:

```text
STOCK_IN
→ unit_cost_snapshot = goods_receipt_items.base_unit_cost

SALE
→ unit_cost_snapshot = current Weighted Average Cost of the actual Lot at posting time

RETURN_IN
→ preferably restore using original sale COGS unit cost
```

Recommended indexes:

```text
(stock_movement_id)
(inventory_lot_id)
```

## 28. `stocktakes`


Represents one inventory-count session.

```text
stocktakes
----------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id

stocktake_number        varchar(50) NOT NULL
status                  varchar(30) NOT NULL

started_at              timestamptz NULL
started_by              uuid NULL FK users.id

completed_at            timestamptz NULL
completed_by            uuid NULL FK users.id

note                    varchar(1000) NULL
created_by              uuid NOT NULL FK users.id

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
DRAFT
IN_PROGRESS
COMPLETED
CANCELLED
```

Constraints:

```text
UNIQUE(store_id, stocktake_number)
```

Flow:

```text
Create Stocktake
→ snapshot expected inventory
→ physical count
→ review discrepancies
→ Complete
→ create one or more ADJUSTMENT Stock Movements
```

Relationship:

```text
stocktakes.id
← stock_movements.stocktake_id
```

No redundant reverse movement FK is stored in `stocktakes`.

Recommended indexes:

```text
(store_id, status)
(store_id, created_at DESC)
```

## 29. `stocktake_items`

```text
stocktake_items
---------------
id                          uuid PK
stocktake_id                uuid NOT NULL FK stocktakes.id
inventory_lot_id            uuid NOT NULL FK inventory_lots.id

system_quantity_snapshot    bigint NOT NULL
counted_quantity            bigint NULL
difference_quantity         bigint NULL

unit_cost_snapshot          numeric(20,6) NULL
difference_cost_value       numeric(20,6) NULL

counted_by                  uuid NULL FK users.id
counted_at                  timestamptz NULL

reason_code                 varchar(50) NULL
note                        varchar(500) NULL

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Constraints:

```text
UNIQUE(stocktake_id, inventory_lot_id)
system_quantity_snapshot >= 0
counted_quantity >= 0 when not NULL
```

Calculation:

```text
difference_quantity
=
counted_quantity - system_quantity_snapshot
```

Reason examples:

```text
DAMAGED
EXPIRED
LOST
STOCKTAKE_DIFFERENCE
MANUAL_CORRECTION
OTHER
```

---

## 30. `inventory_reservations`

```text
inventory_reservations
----------------------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id
order_id                uuid NOT NULL FK orders.id

status                  varchar(30) NOT NULL

reserved_at             timestamptz NOT NULL
reserved_by             uuid NOT NULL FK users.id

released_at             timestamptz NULL
released_by             uuid NULL FK users.id
release_reason           varchar(500) NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
PARTIALLY_CONSUMED
CONSUMED
RELEASED
CANCELLED
```

Rule:

```text
One active Inventory Reservation header per Order.
```

Reservation is created only after:

```text
Order confirmed
+
Payment/Credit validation passed
```

Reservation increases `quantity_reserved`, not decrease `quantity_on_hand`.

---

## 31. `inventory_reservation_items`


Lot-level reserved quantities for an Order Item.

```text
inventory_reservation_items
---------------------------
id                          uuid PK
inventory_reservation_id    uuid NOT NULL FK inventory_reservations.id
order_item_id               uuid NOT NULL FK order_items.id
inventory_lot_id            uuid NOT NULL FK inventory_lots.id

base_quantity_reserved      bigint NOT NULL
base_quantity_consumed      bigint NOT NULL DEFAULT 0
base_quantity_released      bigint NOT NULL DEFAULT 0

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Constraints:

```text
base_quantity_reserved > 0
base_quantity_consumed >= 0
base_quantity_released >= 0

base_quantity_consumed
+
base_quantity_released
<=
base_quantity_reserved
```

Recommended unique index:

```sql
CREATE UNIQUE INDEX ux_inventory_reservation_item_lot
ON inventory_reservation_items(
    inventory_reservation_id,
    order_item_id,
    inventory_lot_id
)
WHERE deleted_at IS NULL;
```

Remaining reserved amount:

```text
remaining_reserved
=
base_quantity_reserved
-
base_quantity_consumed
-
base_quantity_released
```

Cross-table rule enforced by Backend:

```text
order_item_id must belong to inventory_reservations.order_id
```

Recommended indexes:

```text
(inventory_lot_id)
(order_item_id)
```

# VII. INVENTORY POSTING RULES

## 7.1 Confirm Goods Receipt

Atomic transaction:

```text
Lock related Lot Balance rows
→ Validate Receipt = DRAFT
→ Resolve/Create Inventory Lots
→ Create STOCK_IN Movement
→ Post Stock Movement Items
→ Increase quantity_on_hand
→ Increase total_cost_value
→ Increment version
→ Mark Receipt CONFIRMED
```

Weighted-average update:

```text
new_quantity
=
old_quantity + incoming_quantity
```

```text
new_total_cost_value
=
old_total_cost_value
+
(incoming_quantity × incoming_unit_cost)
```

---

## 7.2 Reserve Inventory

Atomic transaction:

```text
Validate Order
→ Select FEFO Lots
→ Lock Lot Balance rows
→ Check available quantity
→ Create Reservation Items
→ Increase quantity_reserved
→ Increment version
```

No Stock Movement is created because reservation does not change physical on-hand quantity.

---

## 7.3 Successful Fulfillment / Delivery

Atomic transaction:

```text
Confirm actual Lots
→ Lock Lot Balance rows
→ Calculate weighted-average cost
→ Create SALE Stock Movement
→ Snapshot COGS
→ Decrease quantity_on_hand
→ Decrease quantity_reserved
→ Mark reserved quantity consumed
→ Increment version
```

---

## 7.4 Release Reservation

```text
Order cancelled
or
remaining fulfillment no longer required

→ decrease quantity_reserved
→ mark reservation quantity released
```

No physical Stock Movement is created.

---

## 7.5 Stocktake Adjustment

```text
Complete physical count
→ calculate discrepancies
→ create ADJUSTMENT_IN / ADJUSTMENT_OUT
→ post movement
→ update Lot Balance
```

---

# VIII. WEIGHTED AVERAGE COST RULE

Costing is maintained **per logical Inventory Lot**, not globally per Product.

```text
Current Average Cost
=
Current Total Cost Value
/
Current Quantity On Hand
```

Incoming goods:

```text
New Total Cost Value
=
Old Total Cost Value
+
Incoming Cost
```

Sale:

```text
COGS
=
Quantity Sold
×
Current Average Cost
```

After sale:

```text
New Total Cost Value
=
Old Total Cost Value
-
COGS
```

`stock_movement_items.unit_cost_snapshot` preserves the exact COGS used at transaction time.

This supports:

```text
Sales Revenue
COGS
Gross Profit
Gross Margin
```

---

# IX. RELATIONSHIPS – TABLES 21–31

```text
suppliers
   ↓ 1:N
goods_receipts
   ↓ 1:N
goods_receipt_items
   ├──────────────→ store_products
   ├──────────────→ product_packagings
   └──────────────→ inventory_lots
                         ↓ 1:1
                  inventory_lot_balances
                         ↓
                  stock_movement_items
                         ↑
                  stock_movements
```

Stocktake:

```text
stocktakes
   ↓ 1:N
stocktake_items
   ↓
inventory_lots

stocktakes
   ↓
Adjustment Stock Movement
```

Reservation:

```text
orders
   ↓
inventory_reservations
   ↓ 1:N
inventory_reservation_items
   ├──→ order_items
   └──→ inventory_lots
```

---

# X. LOCKED DESIGN DECISIONS

1. No Purchase Order workflow.
2. Supplier is retained for receiving traceability.
3. Goods receiving supports Manual Entry and AgriSage Excel Template import.
4. Receipt requires Preview / Validate / Confirm.
5. Confirmed Receipt cannot be silently edited.
6. Inventory is stored in Base Units.
7. Packaging conversion is snapshotted in transactions.
8. One logical Lot may be received multiple times.
9. Inventory Cost uses **Weighted Average Cost per logical Lot**.
10. Inventory availability = `on_hand - reserved`.
11. FEFO is used for lot suggestion/reservation.
12. Expired, blocked, quarantined Lots cannot be sold.
13. Every physical stock change creates a Stock Movement.
14. Reservation does not create a physical Stock Movement.
15. Inventory balance changes require concurrency protection.
16. API DELETE uses Soft Delete only.

---

# XI. SALES

## 32. `carts`

Stores the active online shopping cart of a registered Farmer.

```text
carts
-----
id                  uuid PK
store_id            uuid NOT NULL FK stores.id
farmer_profile_id   uuid NOT NULL FK farmer_profiles.id

status              varchar(20) NOT NULL

converted_order_id  uuid NULL FK orders.id
converted_at        timestamptz NULL

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
CONVERTED
ABANDONED
```

Rules:

```text
Only registered Farmers have carts.
Walk-in customers do not have Cart records.
Only one ACTIVE Cart per Farmer per Store.
```

Recommended partial unique index:

```text
UNIQUE(store_id, farmer_profile_id)
WHERE status = 'ACTIVE'
  AND deleted_at IS NULL
```

When checkout succeeds:

```text
Cart
→ Order created
→ Cart status = CONVERTED
→ converted_order_id assigned
```

---

## 33. `cart_items`

Stores product packaging and quantity selected in a Farmer Cart.

```text
cart_items
----------
id                      uuid PK
cart_id                 uuid NOT NULL FK carts.id
store_product_id        uuid NOT NULL FK store_products.id
product_packaging_id    uuid NOT NULL FK product_packagings.id

quantity                bigint NOT NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Constraints:

```text
quantity > 0

UNIQUE(
    cart_id,
    store_product_id,
    product_packaging_id
)
for active records
```

Important pricing rule:

```text
Cart Item does NOT become the financial price snapshot.
```

Current customer-group price is recalculated when displaying the cart and again during checkout.

The authoritative selling-price snapshot is created only in:

```text
order_items
```

This prevents stale Cart prices from becoming historical transaction prices.

---

## 34. `orders`


Represents online Farmer orders and Store counter orders.

```text
orders
------
id                          uuid PK
store_id                    uuid NOT NULL FK stores.id

order_number                varchar(50) NOT NULL

source                      varchar(30) NOT NULL
customer_type               varchar(20) NOT NULL
farmer_profile_id           uuid NULL FK farmer_profiles.id

created_by                  uuid NOT NULL FK users.id

customer_group_id_snapshot  uuid NULL FK customer_groups.id
price_list_id_snapshot      uuid NULL FK price_lists.id

customer_name_snapshot      varchar(150) NOT NULL
customer_phone_snapshot     varchar(20) NULL

settlement_type             varchar(20) NOT NULL
credit_term_days_snapshot   integer NULL

fulfillment_type            varchar(20) NOT NULL

source_address_id           uuid NULL FK user_addresses.id

recipient_name_snapshot     varchar(150) NULL
recipient_phone_snapshot    varchar(20) NULL
delivery_address_line       varchar(500) NULL
delivery_ward               varchar(150) NULL
delivery_district           varchar(150) NULL
delivery_province           varchar(150) NULL
delivery_latitude           numeric(10,7) NULL
delivery_longitude          numeric(10,7) NULL

status                      varchar(40) NOT NULL

subtotal_amount             numeric(18,2) NOT NULL DEFAULT 0
total_amount                numeric(18,2) NOT NULL DEFAULT 0

note                        varchar(1000) NULL

confirmed_at                timestamptz NULL
confirmed_by                uuid NULL FK users.id

pickup_completed_at         timestamptz NULL
pickup_completed_by         uuid NULL FK users.id

completed_at                timestamptz NULL

cancelled_at                timestamptz NULL
cancelled_by                uuid NULL FK users.id
cancel_reason               varchar(1000) NULL

version                     bigint NOT NULL DEFAULT 0

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

`source`:

```text
FARMER_WEB
FARMER_MOBILE
COUNTER
```

`customer_type`:

```text
REGISTERED
WALK_IN
```

`settlement_type`:

```text
FULL_PAYMENT
CREDIT
```

`fulfillment_type`:

```text
PICKUP
DELIVERY
```

Recommended statuses:

```text
PENDING_CONFIRMATION
CONFIRMED
PREPARING
READY_FOR_FULFILLMENT
PARTIALLY_FULFILLED
COMPLETED
CANCELLED
PARTIALLY_CANCELLED
```

Constraints:

```text
UNIQUE(store_id, order_number)

subtotal_amount >= 0
total_amount >= 0

credit_term_days_snapshot >= 0 when not NULL
```

Customer/settlement rules:

```text
REGISTERED
→ farmer_profile_id IS NOT NULL

WALK_IN
→ farmer_profile_id IS NULL
→ settlement_type = FULL_PAYMENT
→ no individual Credit Profile / Debt Account

settlement_type = CREDIT
→ customer_type = REGISTERED
→ ACTIVE Farmer Credit Profile required
→ credit_term_days_snapshot required
```

`credit_term_days_snapshot` is captured when the Credit Order is approved/confirmed.  
Future Credit Tier changes do not alter this Order's payment terms.

Debt due date later uses:

```text
fulfillment date
+
orders.credit_term_days_snapshot
```

Registered Farmer orders snapshot:

```text
Customer Group
Price List
Customer Name
Customer Phone
```

Delivery orders snapshot the destination address so later edits to `user_addresses` do not rewrite history.

Pickup:

```text
PICKUP
→ no Delivery record
→ physical handover triggers SALE stock posting and financial posting
```

Farmer self-cancellation:

```text
only while PENDING_CONFIRMATION
```

Recommended indexes:

```text
(store_id, created_at DESC)
(farmer_profile_id, created_at DESC)
(status, created_at DESC)
(fulfillment_type, status)
(settlement_type, status)
```

## 35. `order_items`


Authoritative commercial snapshot of each ordered Product Packaging.

```text
order_items
-----------
id                              uuid PK
order_id                        uuid NOT NULL FK orders.id
store_product_id                uuid NOT NULL FK store_products.id
product_packaging_id            uuid NOT NULL FK product_packagings.id

product_sku_snapshot            varchar(50) NOT NULL
product_name_snapshot           varchar(255) NOT NULL
packaging_name_snapshot         varchar(150) NOT NULL

quantity                        bigint NOT NULL
conversion_to_base_snapshot     bigint NOT NULL
base_quantity                   bigint NOT NULL

suggested_unit_price            numeric(18,2) NOT NULL
unit_price                      numeric(18,2) NOT NULL
line_total_amount               numeric(18,2) NOT NULL

price_overridden                boolean NOT NULL DEFAULT false
override_reason                 varchar(500) NULL
overridden_by                   uuid NULL FK users.id

fulfilled_base_quantity         bigint NOT NULL DEFAULT 0
cancelled_base_quantity         bigint NOT NULL DEFAULT 0

status                          varchar(30) NOT NULL

created_at                      timestamptz NOT NULL
updated_at                      timestamptz NOT NULL
deleted_at                      timestamptz NULL
deleted_by                      uuid NULL FK users.id
```

Statuses:

```text
PENDING
PARTIALLY_FULFILLED
FULFILLED
CANCELLED
PARTIALLY_CANCELLED
```

Constraints:

```text
quantity > 0
conversion_to_base_snapshot > 0

base_quantity =
quantity × conversion_to_base_snapshot

suggested_unit_price >= 0
unit_price >= 0
line_total_amount >= 0

fulfilled_base_quantity >= 0
cancelled_base_quantity >= 0

fulfilled_base_quantity
+
cancelled_base_quantity
<=
base_quantity
```

Pricing semantics:

```text
suggested_unit_price
=
system suggested price for the selected Packaging from the applicable Price List

unit_price
=
actual final selling price
```

If:

```text
price_overridden = true
```

then:

```text
override_reason required
overridden_by required
```

The name `suggested_unit_price` intentionally avoids confusion with the Inventory term **Base Unit**.

COGS is not stored here.  
COGS is determined when actual Lots are fulfilled.

Recommended indexes:

```text
(order_id)
(store_product_id)
(status)
```

# XII. PAYMENT

## 36. `payments`

Stores every incoming payment transaction.

The same table is used for:

```text
Order upfront/full payment
Debt repayment
Cash payment
payOS online payment
```

```text
payments
--------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id

payment_number          varchar(50) NOT NULL

payer_farmer_profile_id uuid NULL FK farmer_profiles.id

payment_context         varchar(30) NOT NULL
payment_method          varchar(20) NOT NULL

amount                  numeric(18,2) NOT NULL
currency                char(3) NOT NULL DEFAULT 'VND'

status                  varchar(30) NOT NULL

provider                varchar(30) NULL
provider_order_code     bigint NULL
provider_payment_link_id varchar(150) NULL
provider_transaction_id varchar(150) NULL
checkout_url            varchar(1500) NULL
provider_metadata       jsonb NULL

confirmation_source     varchar(30) NULL

initiated_at            timestamptz NOT NULL
confirmed_at            timestamptz NULL
confirmed_by            uuid NULL FK users.id

failed_at               timestamptz NULL
cancelled_at            timestamptz NULL

created_by              uuid NULL FK users.id
note                    varchar(1000) NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

`payment_context`:

```text
ORDER_PAYMENT
DEBT_REPAYMENT
```

`payment_method`:

```text
CASH
PAYOS
```

Statuses:

```text
PENDING
PAID
FAILED
CANCELLED
PARTIALLY_REFUNDED
REFUNDED
```

`confirmation_source`:

```text
STAFF
PAYOS_WEBHOOK
```

Constraints:

```text
UNIQUE(store_id, payment_number)

amount > 0
```

Recommended unique provider index:

```text
UNIQUE(provider_order_code)
WHERE provider_order_code IS NOT NULL
```

Cash flow:

```text
Cash received
→ Store Owner / Sales Staff confirms
→ status = PAID
→ confirmation_source = STAFF
```

payOS flow:

```text
Backend creates Payment = PENDING
→ Backend creates payOS payment link
→ Farmer opens checkoutUrl
→ payOS webhook reaches backend
→ verify signature + amount + order code
→ idempotently mark Payment = PAID
→ confirmation_source = PAYOS_WEBHOOK
```

The return URL from payOS is not the source of truth.

Provider secrets remain only in Backend configuration.

---

## 37. `payment_allocations`


Allocates one confirmed Payment either to an Order prepayment pool or to one Debt Entry.

```text
payment_allocations
-------------------
id                              uuid PK
payment_id                      uuid NOT NULL FK payments.id

allocation_type                 varchar(20) NOT NULL

order_id                        uuid NULL FK orders.id
debt_entry_id                   uuid NULL FK debt_entries.id

allocated_amount                numeric(18,2) NOT NULL

prepayment_consumed_amount      numeric(18,2) NOT NULL DEFAULT 0

status                          varchar(20) NOT NULL

allocated_at                    timestamptz NOT NULL
allocated_by                    uuid NULL FK users.id

reversed_at                     timestamptz NULL
reversed_by                     uuid NULL FK users.id
reversal_reason                 varchar(500) NULL

created_at                      timestamptz NOT NULL
updated_at                      timestamptz NOT NULL
deleted_at                      timestamptz NULL
deleted_by                      uuid NULL FK users.id
```

`allocation_type`:

```text
ORDER
DEBT
```

Statuses:

```text
ACTIVE
REVERSED
```

Constraints:

```text
allocated_amount > 0

prepayment_consumed_amount >= 0
prepayment_consumed_amount <= allocated_amount
```

Exactly one target:

```text
ORDER
→ order_id IS NOT NULL
→ debt_entry_id IS NULL

DEBT
→ debt_entry_id IS NOT NULL
→ order_id IS NULL
→ prepayment_consumed_amount = 0
```

Total active allocations must satisfy:

```text
SUM(allocated_amount)
<=
payments.amount
```

### ORDER allocation

Represents confirmed money paid against the Order.

Available unapplied prepayment:

```text
allocated_amount - prepayment_consumed_amount
```

### DEBT allocation

Represents the exact share of a Payment applied to one Debt Entry.

Ledger trace:

```text
Payment
→ Payment Allocation
→ Debt Transaction
```

`debt_transactions.payment_allocation_id` references this exact allocation.

Recommended indexes:

```text
(payment_id)
(order_id)
(debt_entry_id)
(status)
```

# XIII. DELIVERY

## 38. `deliveries`

One Order may have multiple Delivery Notes.

```text
deliveries
----------
id                          uuid PK
store_id                    uuid NOT NULL FK stores.id
order_id                    uuid NOT NULL FK orders.id

delivery_number             varchar(50) NOT NULL

assigned_to_member_id       uuid NULL FK store_members.id

recipient_name_snapshot     varchar(150) NOT NULL
recipient_phone_snapshot    varchar(20) NOT NULL

address_line_snapshot       varchar(500) NOT NULL
ward_snapshot               varchar(150) NULL
district_snapshot           varchar(150) NULL
province_snapshot           varchar(150) NOT NULL

latitude_snapshot           numeric(10,7) NULL
longitude_snapshot          numeric(10,7) NULL

scheduled_at                timestamptz NULL
dispatched_at               timestamptz NULL
completed_at                timestamptz NULL

status                      varchar(30) NOT NULL
note                        varchar(1000) NULL

created_by                  uuid NOT NULL FK users.id

cancelled_at                timestamptz NULL
cancelled_by                uuid NULL FK users.id
cancel_reason               varchar(1000) NULL

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Statuses:

```text
DRAFT
ASSIGNED
OUT_FOR_DELIVERY
PARTIALLY_DELIVERED
RETRY_PENDING
DELIVERED
CANCELLED
```

Constraint:

```text
UNIQUE(store_id, delivery_number)
```

Rules:

```text
orders.fulfillment_type must be DELIVERY.
One Order may have N Deliveries.
A failed attempt does not automatically cancel a Delivery.
```

Address is snapshotted again at Delivery creation because a later delivery round may intentionally use a changed destination.

---

## 39. `delivery_items`

Stores the Order Item quantity planned for a specific Delivery Note.

```text
delivery_items
--------------
id                          uuid PK
delivery_id                 uuid NOT NULL FK deliveries.id
order_item_id               uuid NOT NULL FK order_items.id

planned_quantity            bigint NOT NULL
conversion_to_base_snapshot bigint NOT NULL
planned_base_quantity       bigint NOT NULL

delivered_base_quantity     bigint NOT NULL DEFAULT 0
cancelled_base_quantity     bigint NOT NULL DEFAULT 0

status                      varchar(30) NOT NULL

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Statuses:

```text
PENDING
PARTIALLY_DELIVERED
DELIVERED
CANCELLED
PARTIALLY_CANCELLED
```

Constraints:

```text
planned_quantity > 0
conversion_to_base_snapshot > 0

planned_base_quantity
=
planned_quantity × conversion_to_base_snapshot

delivered_base_quantity >= 0
cancelled_base_quantity >= 0

delivered_base_quantity
+
cancelled_base_quantity
<=
planned_base_quantity
```

Across all active Delivery Items for the same Order Item:

```text
planned quantities must not exceed
Order Item remaining fulfillable quantity.
```

---

## 40. `delivery_item_lot_allocations`

Defines which Inventory Lots are physically prepared for each Delivery Item.

```text
delivery_item_lot_allocations
-----------------------------
id                              uuid PK
delivery_item_id                uuid NOT NULL FK delivery_items.id

inventory_lot_id                uuid NOT NULL FK inventory_lots.id
inventory_reservation_item_id   uuid NULL FK inventory_reservation_items.id

allocated_base_quantity         bigint NOT NULL
delivered_base_quantity         bigint NOT NULL DEFAULT 0
released_base_quantity          bigint NOT NULL DEFAULT 0

status                          varchar(30) NOT NULL

created_at                      timestamptz NOT NULL
updated_at                      timestamptz NOT NULL
deleted_at                      timestamptz NULL
deleted_by                      uuid NULL FK users.id
```

Statuses:

```text
ALLOCATED
PARTIALLY_DELIVERED
DELIVERED
RELEASED
CANCELLED
```

Constraints:

```text
allocated_base_quantity > 0

delivered_base_quantity >= 0
released_base_quantity >= 0

delivered_base_quantity
+
released_base_quantity
<=
allocated_base_quantity
```

Normal flow:

```text
Inventory Reservation Item
→ Delivery Lot Allocation
```

If staff changes the actual Lot before dispatch:

```text
release old reserved Lot
→ reserve replacement Lot
→ update Delivery Lot Allocation
```

so inventory reservation and physical preparation remain consistent.

---

## 41. `delivery_attempts`

Stores each actual delivery attempt.

```text
delivery_attempts
-----------------
id                      uuid PK
delivery_id             uuid NOT NULL FK deliveries.id

attempt_number          integer NOT NULL
attempted_by_member_id  uuid NOT NULL FK store_members.id

status                  varchar(30) NOT NULL

started_at              timestamptz NOT NULL
completed_at            timestamptz NULL

failure_reason_code     varchar(50) NULL
note                    varchar(1000) NULL

receiver_name           varchar(150) NULL
proof_image_url         varchar(1000) NULL

sale_stock_movement_id  uuid NULL FK stock_movements.id

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
IN_PROGRESS
SUCCESS
PARTIAL_SUCCESS
FAILED
CANCELLED
```

Constraint:

```text
UNIQUE(delivery_id, attempt_number)
```

Rules:

```text
SUCCESS / PARTIAL_SUCCESS with delivered quantity
→ proof_image_url required
```

For each attempt that successfully transfers goods:

```text
Delivery Attempt
→ SALE Stock Movement
```

The stock movement records the exact Lots physically issued.

A failed attempt without delivered goods:

```text
does NOT create SALE Stock Movement
does NOT create Debt
```

---

## 42. `delivery_attempt_items` — NEW TABLE ADDED DURING DETAILED DESIGN


This table is required because one Delivery may have multiple Attempts and each Attempt may partially deliver specific Lot quantities.

```text
delivery_attempt_items
----------------------
id                              uuid PK
delivery_attempt_id             uuid NOT NULL FK delivery_attempts.id
delivery_item_lot_allocation_id uuid NOT NULL FK delivery_item_lot_allocations.id

attempted_base_quantity         bigint NOT NULL
delivered_base_quantity         bigint NOT NULL DEFAULT 0
failed_base_quantity            bigint NOT NULL DEFAULT 0

note                            varchar(500) NULL

created_at                      timestamptz NOT NULL
updated_at                      timestamptz NOT NULL
deleted_at                      timestamptz NULL
deleted_by                      uuid NULL FK users.id
```

Constraints:

```text
attempted_base_quantity > 0

delivered_base_quantity >= 0
failed_base_quantity >= 0

delivered_base_quantity
+
failed_base_quantity
=
attempted_base_quantity
```

Recommended unique index:

```sql
CREATE UNIQUE INDEX ux_delivery_attempt_allocation
ON delivery_attempt_items(
    delivery_attempt_id,
    delivery_item_lot_allocation_id
)
WHERE deleted_at IS NULL;
```

Across all attempts:

```text
cumulative delivered quantity
<=
delivery_item_lot_allocations.allocated_base_quantity
```

Example:

```text
Allocation Lot A = 20 Bags

Attempt 1:
attempted = 20
delivered = 18
failed = 2

Attempt 2:
attempted = 2
delivered = 2
failed = 0
```

Recommended indexes:

```text
(delivery_attempt_id)
(delivery_item_lot_allocation_id)
```

## 43. `delivery_incidents`

Stores delivery problems and their resolution.

```text
delivery_incidents
------------------
id                              uuid PK
delivery_id                     uuid NOT NULL FK deliveries.id
delivery_attempt_id             uuid NULL FK delivery_attempts.id
delivery_item_lot_allocation_id uuid NULL FK delivery_item_lot_allocations.id

incident_type                   varchar(40) NOT NULL

affected_base_quantity          bigint NULL

description                     varchar(1000) NOT NULL

status                          varchar(20) NOT NULL
resolution_type                 varchar(40) NULL
resolution_note                 varchar(1000) NULL

evidence_image_url              varchar(1000) NULL

related_stock_movement_id       uuid NULL FK stock_movements.id

reported_by                     uuid NOT NULL FK users.id
reported_at                     timestamptz NOT NULL

resolved_by                     uuid NULL FK users.id
resolved_at                     timestamptz NULL

created_at                      timestamptz NOT NULL
updated_at                      timestamptz NOT NULL
deleted_at                      timestamptz NULL
deleted_by                      uuid NULL FK users.id
```

Incident types:

```text
CUSTOMER_ABSENT
UNREACHABLE
CUSTOMER_REFUSED
DAMAGED
WEATHER
VEHICLE_ISSUE
ADDRESS_ISSUE
OTHER
```

Statuses:

```text
OPEN
RESOLVED
```

Resolution types:

```text
RETRY_DELIVERY
REPLACE_GOODS
RETURN_TO_STORE
WRITE_OFF
CANCEL_REMAINDER
NO_ACTION
OTHER
```

Rules:

For damaged goods:

```text
Incident
→ return/quarantine if physically returned
→ adjustment/write-off if unusable
→ optional replacement through later Delivery
```

Any inventory-affecting resolution must create an auditable Stock Movement.

---

# XIV. CREDIT MANAGEMENT

## 44. `credit_tiers`

Stores default credit-policy tiers.

```text
credit_tiers
------------
id                          uuid PK
store_id                    uuid NOT NULL FK stores.id

code                        varchar(30) NOT NULL
name                        varchar(100) NOT NULL
description                 varchar(500) NULL

default_credit_limit        numeric(18,2) NOT NULL DEFAULT 0
default_payment_term_days   integer NOT NULL DEFAULT 0

is_active                   boolean NOT NULL DEFAULT true

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Examples:

```text
STANDARD
SILVER
GOLD
VIP
```

Constraints:

```text
UNIQUE(store_id, code)

default_credit_limit >= 0
default_payment_term_days >= 0
```

Credit Tier provides defaults.

The individual Farmer Credit Profile remains authoritative.

No overdue interest/penalty is calculated in MVP.

---

## 45. `farmer_credit_profiles`

Stores the current individual credit policy for a registered Farmer.

```text
farmer_credit_profiles
----------------------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id
farmer_profile_id       uuid NOT NULL FK farmer_profiles.id

credit_tier_id          uuid NULL FK credit_tiers.id

credit_limit            numeric(18,2) NOT NULL DEFAULT 0
status                  varchar(20) NOT NULL

approved_by             uuid NOT NULL FK users.id
approved_at             timestamptz NOT NULL

note                    varchar(1000) NULL

version                 bigint NOT NULL DEFAULT 0

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
SUSPENDED
BLOCKED
```

Constraints:

```text
UNIQUE(store_id, farmer_profile_id)

credit_limit >= 0
```

Do **not** store duplicated:

```text
outstanding_receivable
reserved_credit
available_credit
```

as independent authoritative fields.

They are determined from:

```text
debt_accounts.current_balance
+
active credit_reservations
```

Available credit:

```text
credit_limit
-
outstanding_receivable
-
remaining_reserved_credit
```

Credit is available only when Profile status is:

```text
ACTIVE
```

---

## 46. `credit_limit_histories`

Stores every Credit Tier / Credit Limit change.

```text
credit_limit_histories
----------------------
id                          uuid PK
farmer_credit_profile_id    uuid NOT NULL FK farmer_credit_profiles.id

old_credit_tier_id          uuid NULL FK credit_tiers.id
new_credit_tier_id          uuid NULL FK credit_tiers.id

old_credit_limit            numeric(18,2) NOT NULL
new_credit_limit            numeric(18,2) NOT NULL

reason                      varchar(1000) NOT NULL

changed_by                  uuid NOT NULL FK users.id
changed_at                  timestamptz NOT NULL

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Constraints:

```text
old_credit_limit >= 0
new_credit_limit >= 0
```

Store Owner and Sales Staff may change credit limits.

Every change must also be represented in `audit_logs`.

---

## 47. `credit_reservations`

Reserves credit exposure for confirmed credit Orders that are not yet fully fulfilled.

```text
credit_reservations
-------------------
id                          uuid PK
store_id                    uuid NOT NULL FK stores.id
farmer_credit_profile_id    uuid NOT NULL FK farmer_credit_profiles.id
order_id                    uuid NOT NULL FK orders.id

amount_reserved             numeric(18,2) NOT NULL
amount_consumed             numeric(18,2) NOT NULL DEFAULT 0
amount_released             numeric(18,2) NOT NULL DEFAULT 0

status                      varchar(30) NOT NULL

reserved_at                 timestamptz NOT NULL
reserved_by                 uuid NOT NULL FK users.id

released_at                 timestamptz NULL
released_by                 uuid NULL FK users.id
release_reason              varchar(500) NULL

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
PARTIALLY_CONSUMED
CONSUMED
RELEASED
CANCELLED
```

Constraints:

```text
amount_reserved >= 0
amount_consumed >= 0
amount_released >= 0

amount_consumed
+
amount_released
<=
amount_reserved
```

Recommended rule:

```text
One active Credit Reservation per Order.
```

Initial reservation:

```text
Required Credit
=
Order Total
-
Confirmed Upfront Payment
```

Before creating reservation:

```text
Required Credit
<=
Available Credit
```

If Farmer makes additional confirmed upfront payment before fulfillment:

```text
reduce/release equivalent unused Credit Reservation
```

At fulfillment:

```text
Fulfilled Value
-
Applicable Remaining Prepayment
=
New Receivable
```

That amount:

```text
Credit Reservation
→ amount_consumed
```

and becomes:

```text
Debt Entry
```

Order cancellation:

```text
unused reservation
→ amount_released
```

---

# XV. ACCOUNTS RECEIVABLE / DEBT

## 48. `debt_accounts`

One Accounts Receivable account per registered Farmer per Store.

```text
debt_accounts
-------------
id                      uuid PK
store_id                uuid NOT NULL FK stores.id
farmer_profile_id       uuid NOT NULL FK farmer_profiles.id

current_balance         numeric(18,2) NOT NULL DEFAULT 0

status                  varchar(20) NOT NULL
last_transaction_at     timestamptz NULL

version                 bigint NOT NULL DEFAULT 0

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
ACTIVE
BLOCKED
CLOSED
```

Constraints:

```text
UNIQUE(store_id, farmer_profile_id)

current_balance >= 0
```

`current_balance` is a transactionally maintained aggregate/cache.

The detailed ledger remains:

```text
debt_transactions
```

Whenever a Debt Transaction is posted:

```text
insert Debt Transaction
+
update Debt Account current_balance
+
increment version
```

inside the same database transaction.

This makes credit-limit checks efficient without losing ledger traceability.

---

## 49. `debt_entries`


Represents one concrete Accounts Receivable obligation.

Normally one Debt Entry is created per successful credit fulfillment event.

```text
debt_entries
------------
id                          uuid PK
debt_account_id             uuid NOT NULL FK debt_accounts.id

entry_number                varchar(50) NOT NULL

order_id                    uuid NULL FK orders.id
delivery_id                 uuid NULL FK deliveries.id
delivery_attempt_id         uuid NULL FK delivery_attempts.id
source_stock_movement_id    uuid NULL FK stock_movements.id

source_type                 varchar(30) NOT NULL

fulfillment_value           numeric(18,2) NOT NULL
prepayment_applied_amount   numeric(18,2) NOT NULL DEFAULT 0

original_amount             numeric(18,2) NOT NULL
outstanding_amount          numeric(18,2) NOT NULL

due_date                    date NOT NULL

status                      varchar(30) NOT NULL

created_by                  uuid NOT NULL FK users.id

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

`source_type`:

```text
DELIVERY
PICKUP
MANUAL_ADJUSTMENT
```

Statuses:

```text
OPEN
PARTIALLY_PAID
PAID
DISPUTED
ADJUSTED
CANCELLED
```

Constraints:

```text
UNIQUE(entry_number)

fulfillment_value >= 0
prepayment_applied_amount >= 0
original_amount >= 0
outstanding_amount >= 0

prepayment_applied_amount <= fulfillment_value

original_amount
=
fulfillment_value - prepayment_applied_amount

outstanding_amount <= original_amount
```

Source requirements:

```text
DELIVERY
→ order_id NOT NULL
→ delivery_id NOT NULL
→ source_stock_movement_id NOT NULL

PICKUP
→ order_id NOT NULL
→ source_stock_movement_id NOT NULL

MANUAL_ADJUSTMENT
→ source_stock_movement_id may be NULL
```

Debt is never created at Order confirmation.

For normal Credit fulfillment:

```text
Successful fulfillment
→ calculate delivered value
→ apply remaining confirmed Order prepayment
→ create Debt Entry for unpaid amount
→ create CREDIT_SALE Debt Transaction
→ consume matching Credit Reservation
```

Due date:

```text
fulfillment date
+
orders.credit_term_days_snapshot
```

Recommended indexes:

```text
(debt_account_id, status, due_date)
(status, due_date)
(order_id)
(delivery_id)
(source_stock_movement_id)
```

## 50. `debt_entry_actions`


Stores disputes and staff decisions affecting a Debt Entry.

```text
debt_entry_actions
------------------
id                           uuid PK
debt_entry_id                uuid NOT NULL FK debt_entries.id

action_type                  varchar(30) NOT NULL

previous_outstanding_amount  numeric(18,2) NULL
adjustment_amount            numeric(18,2) NULL
resulting_outstanding_amount numeric(18,2) NULL

old_due_date                 date NULL
new_due_date                 date NULL

reason                       varchar(1000) NOT NULL

created_by                   uuid NOT NULL FK users.id
created_at                   timestamptz NOT NULL

updated_at                   timestamptz NOT NULL
deleted_at                   timestamptz NULL
deleted_by                   uuid NULL FK users.id
```

Action types:

```text
DISPUTE
KEEP
ADJUST
CANCEL
CHANGE_DUE_DATE
```

Rules:

```text
DISPUTE
→ does not automatically change receivable amount

KEEP
→ amount unchanged

ADJUST
→ one or more Debt Transactions reference this Action

CANCEL
→ negative Debt Transaction removes the remaining valid receivable

CHANGE_DUE_DATE
→ old_due_date + new_due_date recorded
```

Relationship:

```text
Debt Entry Action
1
→ 0..N Debt Transactions
```

The relationship is stored only through:

```text
debt_transactions.debt_entry_action_id
```

There is no reverse `related_debt_transaction_id`, avoiding a circular redundant FK.

Recommended index:

```text
(debt_entry_id, created_at DESC)
```

## 51. `debt_transactions`


Immutable Accounts Receivable ledger.

```text
debt_transactions
-----------------
id                          uuid PK
debt_account_id             uuid NOT NULL FK debt_accounts.id
debt_entry_id               uuid NULL FK debt_entries.id

payment_allocation_id       uuid NULL FK payment_allocations.id
sales_return_id             uuid NULL FK sales_returns.id
debt_entry_action_id        uuid NULL FK debt_entry_actions.id

transaction_type            varchar(30) NOT NULL

amount_delta                numeric(18,2) NOT NULL
balance_after               numeric(18,2) NOT NULL

occurred_at                 timestamptz NOT NULL
status                      varchar(20) NOT NULL

reversal_of_transaction_id  uuid NULL FK debt_transactions.id

created_by                  uuid NULL FK users.id
note                        varchar(1000) NULL

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Transaction types:

```text
CREDIT_SALE
PAYMENT
ADJUSTMENT_IN
ADJUSTMENT_OUT
RETURN
REVERSAL
```

Statuses:

```text
POSTED
REVERSED
```

Sign convention:

```text
positive amount_delta → receivable increases
negative amount_delta → receivable decreases
```

Examples:

```text
Credit Sale       +40M
Debt Payment      -10M
Return Adjustment  -5M
Manual Increase    +2M
```

Constraints:

```text
amount_delta <> 0
balance_after >= 0
```

Source rules:

```text
transaction_type = PAYMENT
→ payment_allocation_id NOT NULL
→ referenced allocation_type = DEBT

transaction_type = RETURN
→ sales_return_id normally NOT NULL

ADJUSTMENT_IN / ADJUSTMENT_OUT
→ debt_entry_action_id normally NOT NULL
```

Posting:

```text
Lock Debt Account
→ validate source
→ calculate new balance
→ insert Debt Transaction
→ update debt_accounts.current_balance
→ update Debt Entry outstanding amount when applicable
→ increment version
→ commit
```

No posted Debt Transaction is physically deleted or silently edited.

Recommended indexes:

```text
(debt_account_id, occurred_at DESC)
(debt_entry_id, occurred_at DESC)
(payment_allocation_id)
(sales_return_id)
(debt_entry_action_id)
(reversal_of_transaction_id)
```

# XVI. END-TO-END ORDER → CREDIT → DELIVERY → DEBT FLOW

## A. Registered Farmer with full upfront payment

```text
Order Total = 50M
Confirmed Payment = 50M

Required Credit = 0

→ no Credit Reservation required
→ Inventory Reservation created
→ goods fulfilled
→ SALE Stock Movement
→ consume Payment prepayment
→ no Debt Entry
→ Order completes
```

---

## B. Registered Farmer with no upfront payment

```text
Order Total = 50M
Confirmed Payment = 0

Required Credit = 50M

→ check Available Credit
→ reserve 50M Credit
→ reserve Inventory
→ Delivery succeeds for 20M
→ consume 20M Credit Reservation
→ create Debt Entry 20M
→ remaining Credit Reservation = 30M
```

Second delivery:

```text
30M delivered
→ consume remaining 30M Credit Reservation
→ create Debt Entry 30M
→ reservation CONSUMED
```

Total outstanding:

```text
50M
```

---

## C. Registered Farmer with partial upfront payment

```text
Order Total = 50M
Confirmed Prepayment = 10M

Required Credit = 40M
```

Delivery 1:

```text
Delivered Value = 20M
Available Prepayment = 10M

Apply Prepayment = 10M
New Debt Entry = 10M

Credit Reservation consumed = 10M
Remaining Credit Reservation = 30M
```

Delivery 2:

```text
Delivered Value = 30M
Available Prepayment = 0

New Debt Entry = 30M

Credit Reservation consumed = 30M
Reservation = CONSUMED
```

Final outstanding:

```text
40M
```

---

## D. Additional payment before delivery

Example:

```text
Order = 50M
Initially prepaid = 0
Credit Reservation = 50M
```

Before delivery Farmer pays:

```text
20M
```

After Payment is confirmed:

```text
Order Prepayment = 20M
Unused Credit Reservation reduced by 20M
Remaining Credit Reservation = 30M
```

This prevents the system from reserving more customer credit than necessary.

---

## E. Debt repayment

```text
Farmer Payment = 30M
```

If Farmer does not select entries:

```text
sort open Debt Entries by due_date ascending
```

Example:

```text
Debt A = 10M
Debt B = 25M
```

Allocation:

```text
10M → Debt A
20M → Debt B
```

Effects:

```text
Debt A → PAID
Debt B outstanding → 5M
Debt Account -30M
```

Each allocation creates its own Debt Transaction.

---

# XVII. DELIVERY + INVENTORY ATOMIC POSTING

For a successful/partially successful Delivery Attempt:

```text
Start DB transaction

→ Validate Delivery Attempt
→ Validate Attempt Item quantities
→ Lock selected Inventory Lot Balances
→ Confirm no expired/blocked/quarantined Lot
→ Calculate current Weighted Average Cost
→ Create SALE Stock Movement
→ Create Stock Movement Items per Lot
→ decrease quantity_on_hand
→ decrease quantity_reserved
→ update Inventory Reservation Items consumed quantities
→ update Delivery Lot Allocation delivered quantities
→ update Delivery Item delivered quantities
→ update Order Item fulfilled quantities
→ apply available Order prepayment
→ consume Credit Reservation for unpaid portion
→ create Debt Entry if unpaid amount > 0
→ create CREDIT_SALE Debt Transaction
→ update Debt Account
→ update Order / Delivery statuses
→ increment concurrency versions

Commit
```

If any required step fails:

```text
ROLLBACK
```

This prevents situations such as:

```text
stock decreased but debt not created
debt created but delivery failed
reservation consumed but stock not decreased
```

---

# XVIII. PICKUP AT STORE

Pickup does not require a Delivery record.

When staff confirms physical handover:

```text
Order fulfillment_type = PICKUP
→ select/confirm actual Lots
→ SALE Stock Movement
→ consume Inventory Reservation
→ update Order Items
→ apply Order Prepayment
→ consume Credit Reservation
→ create Debt Entry if unpaid
→ update Order status
```

The same inventory and financial posting principles used by Delivery must be reused by Pickup.

---

# XIX. CREDIT EXPOSURE RULE

For an ACTIVE Farmer Credit Profile:

```text
Outstanding Receivable
=
debt_accounts.current_balance
```

Remaining Reserved Credit:

```text
SUM(
    amount_reserved
    -
    amount_consumed
    -
    amount_released
)
for active Credit Reservations
```

Available Credit:

```text
Credit Limit
-
Outstanding Receivable
-
Remaining Reserved Credit
```

A new confirmed Credit Order must satisfy:

```text
Required Credit <= Available Credit
```

Check and reservation must occur inside one transaction to prevent concurrent Orders from overcommitting the same Credit Limit.

---

# XX. NEW TABLE DISCOVERED DURING DETAILED DESIGN

The original baseline after removing `promotions` contained:

```text
66 tables
```

Detailed analysis of the already-confirmed partial-delivery rule identified one missing normalized entity:

```text
delivery_attempt_items
```

Reason:

```text
Delivery 1:N Attempts
Attempt may partially deliver specific Lot quantities
```

Without this table, item/lot quantities delivered by each attempt cannot be reconstructed reliably.

Therefore the current detailed-design baseline becomes:

```text
67 tables
```

This is **not** the old `promotions` table returning.

It is a new Delivery audit table required by the confirmed business rules.

---

# XXI. SALES RETURN & REFUND

> Return/Refund is part of the official database scope and is designed fully now, even though API/UI implementation can be deferred to a later development phase.

## 52. `sales_returns`


Represents a post-fulfillment return case.

A customer refusing goods during delivery **before successful fulfillment** is handled by `delivery_incidents`, not Sales Return.

```text
sales_returns
-------------
id                          uuid PK
store_id                    uuid NOT NULL FK stores.id

return_number               varchar(50) NOT NULL

order_id                    uuid NOT NULL FK orders.id
farmer_profile_id           uuid NULL FK farmer_profiles.id

status                      varchar(30) NOT NULL

requested_at                timestamptz NOT NULL
requested_by                uuid NOT NULL FK users.id

reason_summary              varchar(1000) NULL

approved_at                 timestamptz NULL
approved_by                 uuid NULL FK users.id

received_at                 timestamptz NULL
received_by                 uuid NULL FK users.id

inspected_at                timestamptz NULL
inspected_by                uuid NULL FK users.id

completed_at                timestamptz NULL

cancelled_at                timestamptz NULL
cancelled_by                uuid NULL FK users.id
cancel_reason               varchar(1000) NULL

total_return_amount         numeric(18,2) NOT NULL DEFAULT 0
total_refund_amount         numeric(18,2) NOT NULL DEFAULT 0
total_debt_adjustment       numeric(18,2) NOT NULL DEFAULT 0

note                        varchar(1000) NULL

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Statuses:

```text
REQUESTED
APPROVED
REJECTED
RECEIVED
INSPECTED
PARTIALLY_RESOLVED
COMPLETED
CANCELLED
```

Constraints:

```text
UNIQUE(store_id, return_number)

total_return_amount >= 0
total_refund_amount >= 0
total_debt_adjustment >= 0
```

Rules:

```text
Return only references quantities already successfully fulfilled.

returned quantity
<=
fulfilled quantity
-
quantity already successfully returned
```

Registered Farmer:

```text
farmer_profile_id = original Order Farmer
```

Walk-in counter sale:

```text
farmer_profile_id may be NULL
```

Original Order is never rewritten by the return process.

Recommended indexes:

```text
(order_id, requested_at DESC)
(status, requested_at DESC)
(farmer_profile_id, requested_at DESC)
```

## 53. `sales_return_items`

Stores each returned quantity and traces it back to the exact sold lot where possible.

```text
sales_return_items
------------------
id                              uuid PK
sales_return_id                 uuid NOT NULL FK sales_returns.id

order_item_id                   uuid NOT NULL FK order_items.id
delivery_item_id                uuid NULL FK delivery_items.id
delivery_item_lot_allocation_id uuid NULL FK delivery_item_lot_allocations.id
original_stock_movement_item_id uuid NULL FK stock_movement_items.id

inventory_lot_id                uuid NOT NULL FK inventory_lots.id

returned_base_quantity          bigint NOT NULL

selling_unit_price_snapshot     numeric(18,2) NOT NULL
conversion_to_base_snapshot     bigint NOT NULL

return_value                    numeric(18,2) NOT NULL

original_cogs_unit_cost         numeric(20,6) NULL
return_inventory_cost_value     numeric(20,6) NULL

reason_code                     varchar(50) NOT NULL
condition_status                varchar(30) NOT NULL
inventory_disposition           varchar(30) NOT NULL

inspection_note                 varchar(1000) NULL

return_stock_movement_id        uuid NULL FK stock_movements.id
debt_adjustment_transaction_id  uuid NULL FK debt_transactions.id

created_at                      timestamptz NOT NULL
updated_at                      timestamptz NOT NULL
deleted_at                      timestamptz NULL
deleted_by                      uuid NULL FK users.id
```

Suggested `reason_code` values:

```text
WRONG_PRODUCT
DAMAGED_PRODUCT
QUALITY_ISSUE
EXPIRED_PRODUCT
DELIVERY_DAMAGE
CUSTOMER_REJECTION
OTHER
```

`condition_status`:

```text
PENDING_INSPECTION
RESELLABLE
DAMAGED
EXPIRED
UNUSABLE
```

`inventory_disposition`:

```text
NONE
RESTOCK
WRITE_OFF
```

Constraints:

```text
returned_base_quantity > 0

conversion_to_base_snapshot > 0

selling_unit_price_snapshot >= 0
return_value >= 0

original_cogs_unit_cost >= 0 when not NULL
return_inventory_cost_value >= 0 when not NULL
```

### Quantity traceability

Preferred source:

```text
delivery_item_lot_allocation_id
```

because it identifies the exact Lot delivered.

For Store Pickup, where no Delivery exists:

```text
original_stock_movement_item_id
```

identifies the exact sold Lot.

`inventory_lot_id` is still snapshotted directly for simple reporting and validation.

### Financial value

The return value is based on the original actual selling price, not the current Price List.

For an Order Item:

```text
base selling price per base unit
=
order_items.unit_price
/
order_items.conversion_to_base_snapshot
```

Then:

```text
return_value
=
returned_base_quantity
×
base selling price per base unit
```

Any rounding policy must be deterministic and implemented consistently in Backend.

### Inventory return

If:

```text
condition_status = RESELLABLE
inventory_disposition = RESTOCK
```

then create:

```text
RETURN_IN Stock Movement
```

and restore quantity to the original logical Inventory Lot.

Cost basis:

```text
Prefer original COGS unit cost
from original_stock_movement_item_id
```

This prevents a return from distorting historical inventory valuation.

If goods are:

```text
DAMAGED
EXPIRED
UNUSABLE
```

then:

```text
inventory_disposition = WRITE_OFF
```

and the goods do not re-enter available inventory.

If inspection has not completed:

```text
inventory_disposition = NONE
```

No inventory change occurs yet.

---

## 54. `refunds`

Stores money returned to a customer after an approved return or other approved financial correction.

```text
refunds
-------
id                          uuid PK
store_id                    uuid NOT NULL FK stores.id

refund_number               varchar(50) NOT NULL

sales_return_id             uuid NOT NULL FK sales_returns.id
original_payment_id         uuid NULL FK payments.id

refund_method               varchar(30) NOT NULL
amount                      numeric(18,2) NOT NULL
currency                    char(3) NOT NULL DEFAULT 'VND'

status                      varchar(30) NOT NULL

external_reference          varchar(200) NULL
proof_file_url              varchar(1000) NULL

requested_at                timestamptz NOT NULL
requested_by                uuid NOT NULL FK users.id

completed_at                timestamptz NULL
completed_by                uuid NULL FK users.id

cancelled_at                timestamptz NULL
cancelled_by                uuid NULL FK users.id
cancel_reason               varchar(1000) NULL

note                        varchar(1000) NULL

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

`refund_method`:

```text
CASH
BANK_TRANSFER
OTHER_EXTERNAL
```

Statuses:

```text
PENDING
COMPLETED
FAILED
CANCELLED
```

Constraints:

```text
UNIQUE(store_id, refund_number)

amount > 0
```

Current scope rule:

```text
AgriSage does NOT automatically execute payOS refunds.
```

Staff performs the real refund externally and records the result.

`original_payment_id` is optional because:

```text
one Return may involve several original Payments
or
the refund may be handled independently outside payOS.
```

### Paid vs credit portion

For each approved return amount:

```text
1. Reduce outstanding receivable first when the returned value is still unpaid.
2. Create actual Refund only for the portion that had already been paid.
```

Debt reduction is recorded through:

```text
debt_transactions
transaction_type = RETURN
```

Cash/bank repayment is recorded through:

```text
refunds
```

This prevents double compensation.

---

# XXII. RETURN / REFUND POSTING FLOW

Example:

```text
Original sale:
Delivered value = 20M
Prepayment applied = 10M
Debt created = 10M
```

Customer later returns goods worth:

```text
8M
```

If outstanding Debt is still 10M:

```text
Debt reduction = 8M
Cash refund = 0
```

Result:

```text
Outstanding Debt = 2M
```

If return value is:

```text
15M
```

then:

```text
Debt reduction = 10M
Refund = 5M
```

Inventory side:

```text
Returned goods
→ inspect

RESELLABLE
→ RETURN_IN
→ restore original Lot quantity using original COGS

DAMAGED / EXPIRED / UNUSABLE
→ no available-stock increase
```

All inventory and financial actions are executed transactionally.

---

# XXIII. AI DIAGNOSIS

> Official production AI scope: **Rice Disease Diagnosis**.
>
> Supported classes:
>
> ```text
> Leaf Blast
> Bacterial Leaf Blight
> Brown Spot
> Sheath Blight
> Healthy
> ```
>
> Cucumber/Anthracnose research is not the production AI scope of this system.

## 55. `diseases`

Stores Rice disease/health classes and knowledge content.

```text
diseases
--------
id                  uuid PK

code                varchar(50) NOT NULL
name                varchar(200) NOT NULL
scientific_name     varchar(255) NULL

crop_type           varchar(50) NOT NULL DEFAULT 'RICE'

description         text NULL
symptoms            text NULL
causes              text NULL
prevention          text NULL

is_healthy_class    boolean NOT NULL DEFAULT false
is_active           boolean NOT NULL DEFAULT true

created_at          timestamptz NOT NULL
updated_at          timestamptz NOT NULL
deleted_at          timestamptz NULL
deleted_by          uuid NULL FK users.id
```

Initial records:

```text
LEAF_BLAST
BACTERIAL_LEAF_BLIGHT
BROWN_SPOT
SHEATH_BLIGHT
HEALTHY
```

Constraint:

```text
UNIQUE(code)
```

Exactly one supported Healthy class should have:

```text
is_healthy_class = true
```

for the Rice production model.

---

## 56. `disease_treatments`

Stores treatment guidance and suitable Active Ingredients for a verified disease.

```text
disease_treatments
------------------
id                      uuid PK
disease_id              uuid NOT NULL FK diseases.id
active_ingredient_id    uuid NULL FK active_ingredients.id

treatment_type          varchar(30) NOT NULL

title                   varchar(255) NOT NULL
instructions            text NOT NULL
precautions             text NULL

priority                integer NOT NULL DEFAULT 0

is_active               boolean NOT NULL DEFAULT true

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

`treatment_type`:

```text
CULTURAL
CHEMICAL
PREVENTIVE
OTHER
```

A treatment may have no Active Ingredient, for example:

```text
cultural / preventive guidance
```

Chemical treatment may reference an Active Ingredient.

---

## 57. `ai_models`

Stores AI model versions and deployment metadata.

```text
ai_models
---------
id                      uuid PK

name                    varchar(150) NOT NULL
version                 varchar(50) NOT NULL

architecture            varchar(100) NULL
framework               varchar(50) NOT NULL

model_storage_url       varchar(1000) NOT NULL

input_width             integer NULL
input_height            integer NULL

class_labels            jsonb NOT NULL
metrics                 jsonb NULL

status                  varchar(30) NOT NULL

deployed_at             timestamptz NULL
retired_at              timestamptz NULL

created_by              uuid NOT NULL FK users.id

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
DRAFT
ACTIVE
RETIRED
```

Constraint:

```text
UNIQUE(name, version)
```

For the current production deployment:

```text
class_labels
```

must contain the five approved Rice classes.

Only explicitly activated models are used for production inference.

---

## 58. `ai_policy_configs`


Stores the interpretation/rejection policy for a specific Model version.

```text
ai_policy_configs
-----------------
id                          uuid PK
ai_model_id                 uuid NOT NULL FK ai_models.id

version                     varchar(50) NOT NULL

minimum_confidence          numeric(5,4) NOT NULL
minimum_margin              numeric(5,4) NULL
top_k                       integer NOT NULL DEFAULT 3

requires_human_review       boolean NOT NULL DEFAULT true

parameters                  jsonb NULL

status                      varchar(20) NOT NULL

effective_from              timestamptz NOT NULL
effective_to                timestamptz NULL

created_by                  uuid NOT NULL FK users.id

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Statuses:

```text
DRAFT
ACTIVE
INACTIVE
```

Constraints:

```text
minimum_confidence BETWEEN 0 AND 1
minimum_margin BETWEEN 0 AND 1 when not NULL
top_k > 0
effective_to > effective_from when effective_to IS NOT NULL

UNIQUE(ai_model_id, version)
```

Temporal rule:

```text
A Model cannot have overlapping ACTIVE policy-effective periods.
```

This is enforced transactionally in the application and may additionally use a PostgreSQL exclusion constraint.

Every Inference references the exact Model and Policy version used, preserving reproducibility.

Recommended indexes:

```text
(ai_model_id, status, effective_from DESC)
```

## 59. `diagnosis_cases`

Stores each Farmer diagnosis case.

```text
diagnosis_cases
---------------
id                      uuid PK
farmer_profile_id       uuid NOT NULL FK farmer_profiles.id

case_number             varchar(50) NOT NULL

crop_type               varchar(50) NOT NULL DEFAULT 'RICE'

status                  varchar(30) NOT NULL

final_disease_id        uuid NULL FK diseases.id

farmer_note             varchar(1000) NULL
review_summary          varchar(1000) NULL

submitted_at            timestamptz NOT NULL
completed_at            timestamptz NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
SUBMITTED
PROCESSING
AI_COMPLETED
UNDER_REVIEW
VERIFIED
INCONCLUSIVE
FAILED
CANCELLED
```

Constraint:

```text
UNIQUE(case_number)
```

Rules:

```text
VERIFIED
→ final_disease_id NOT NULL

INCONCLUSIVE
→ final_disease_id NULL
→ no final product/treatment recommendation
```

For Healthy:

```text
final_disease_id references HEALTHY class
```

---

## 60. `diagnosis_images`


Stores diagnosis image metadata and Object Storage references.

```text
diagnosis_images
----------------
id                      uuid PK
diagnosis_case_id       uuid NOT NULL FK diagnosis_cases.id

storage_key             varchar(1000) NOT NULL
image_url               varchar(1500) NOT NULL

file_name               varchar(255) NULL
mime_type               varchar(100) NULL
file_size_bytes         bigint NULL

width                   integer NULL
height                  integer NULL

is_primary              boolean NOT NULL DEFAULT false

uploaded_at             timestamptz NOT NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Rules:

```text
At least one active image is required before AI inference.
At most one active primary image per Diagnosis Case.
```

Recommended partial unique index:

```sql
CREATE UNIQUE INDEX ux_diagnosis_images_primary
ON diagnosis_images(diagnosis_case_id)
WHERE is_primary = TRUE
  AND deleted_at IS NULL;
```

Recommended index:

```text
(diagnosis_case_id, uploaded_at)
```

Object Storage contains image bytes; database stores reference and metadata only.

## 61. `ai_inferences`

Stores immutable AI inference results.

```text
ai_inferences
-------------
id                      uuid PK
diagnosis_case_id       uuid NOT NULL FK diagnosis_cases.id
diagnosis_image_id      uuid NOT NULL FK diagnosis_images.id

ai_model_id             uuid NOT NULL FK ai_models.id
ai_policy_config_id     uuid NOT NULL FK ai_policy_configs.id

predicted_disease_id    uuid NULL FK diseases.id
predicted_class_label   varchar(100) NOT NULL

confidence              numeric(7,6) NOT NULL

top_predictions         jsonb NULL
raw_output              jsonb NULL

passed_policy           boolean NOT NULL

inference_duration_ms   integer NULL

status                  varchar(20) NOT NULL

inferred_at             timestamptz NOT NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
SUCCESS
FAILED
```

Constraints:

```text
confidence BETWEEN 0 AND 1

inference_duration_ms >= 0 when not NULL
```

Rule:

```text
AI Inference is evidence.
It is not the final verified diagnosis by itself.
```

Model/policy records preserve reproducibility.

---

## 62. `agent_reviews`


Stores authorized Human Review at the **Diagnosis Case** level.

A Case may contain multiple images and multiple AI Inferences, so the review is not required to bind to only one Inference.

```text
agent_reviews
-------------
id                          uuid PK
diagnosis_case_id           uuid NOT NULL FK diagnosis_cases.id

primary_ai_inference_id     uuid NULL FK ai_inferences.id

reviewer_member_id          uuid NOT NULL FK store_members.id

decision                    varchar(30) NOT NULL

ai_disease_id_snapshot      uuid NULL FK diseases.id
final_disease_id            uuid NULL FK diseases.id

comment                     varchar(2000) NULL

is_current                  boolean NOT NULL DEFAULT true

reviewed_at                 timestamptz NOT NULL

superseded_at               timestamptz NULL
superseded_by_review_id     uuid NULL FK agent_reviews.id

created_at                  timestamptz NOT NULL
updated_at                  timestamptz NOT NULL
deleted_at                  timestamptz NULL
deleted_by                  uuid NULL FK users.id
```

Decisions:

```text
CONFIRMED
CORRECTED
INCONCLUSIVE
```

Rules:

```text
CONFIRMED
→ final_disease_id required
→ normally equals reviewed AI disease

CORRECTED
→ final_disease_id required
→ may differ from AI prediction

INCONCLUSIVE
→ final_disease_id NULL
```

`primary_ai_inference_id` is optional and identifies the main inference used by the reviewer when useful.

All Case Inferences remain available as supporting evidence.

Only Store Owner / Sales Staff with:

```text
can_review_ai = true
```

may create a review.

Recommended partial unique index:

```sql
CREATE UNIQUE INDEX ux_agent_reviews_current
ON agent_reviews(diagnosis_case_id)
WHERE is_current = TRUE
  AND deleted_at IS NULL;
```

If a review is corrected:

```text
old review → is_current = false
new review inserted
```

Do not overwrite review history.

Recommended indexes:

```text
(diagnosis_case_id, reviewed_at DESC)
(reviewer_member_id, reviewed_at DESC)
(primary_ai_inference_id)
```

## 63. `recommendation_items`

Stores final treatment/product recommendations attached to the current verified Human Review.

```text
recommendation_items
--------------------
id                      uuid PK
diagnosis_case_id       uuid NOT NULL FK diagnosis_cases.id
agent_review_id         uuid NOT NULL FK agent_reviews.id

recommendation_type     varchar(20) NOT NULL

disease_treatment_id    uuid NULL FK disease_treatments.id
store_product_id        uuid NULL FK store_products.id

rank_order              integer NOT NULL DEFAULT 0

reason                  varchar(1000) NULL

approved_by             uuid NOT NULL FK users.id
approved_at             timestamptz NOT NULL

is_active               boolean NOT NULL DEFAULT true

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

`recommendation_type`:

```text
TREATMENT
PRODUCT
```

Target rule:

```text
TREATMENT
→ disease_treatment_id NOT NULL
→ store_product_id NULL

PRODUCT
→ store_product_id NOT NULL
```

Recommendation flow:

```text
Verified Disease
→ Disease Treatments
→ Suitable Active Ingredients
→ Products containing those Active Ingredients
→ Store Products active/sellable
→ Prefer products with available stock
→ Reviewer approves final recommendation
```

Rules:

```text
No recommendation for INCONCLUSIVE cases.

Healthy class normally receives preventive/care guidance only,
not disease-treatment product recommendations.
```

---

# XXIV. CONTENT & SYSTEM

## 64. `articles`

Stores agricultural knowledge articles.

```text
articles
--------
id                      uuid PK

title                   varchar(255) NOT NULL
slug                    varchar(255) NOT NULL

summary                 varchar(1000) NULL
content                  text NOT NULL

thumbnail_url           varchar(1000) NULL

status                  varchar(20) NOT NULL

author_id               uuid NOT NULL FK users.id

published_at            timestamptz NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
DRAFT
PUBLISHED
ARCHIVED
```

Constraint:

```text
UNIQUE(slug) for active records
```

Rule:

```text
PUBLISHED
→ published_at NOT NULL
```

---

## 65. `contact_requests`

Stores customer support/contact submissions.

```text
contact_requests
----------------
id                      uuid PK

request_number          varchar(50) NOT NULL

user_id                 uuid NULL FK users.id

contact_name            varchar(150) NOT NULL
contact_phone           varchar(20) NULL
contact_email           varchar(255) NULL

subject                 varchar(255) NOT NULL
message                 text NOT NULL

status                  varchar(20) NOT NULL

assigned_to             uuid NULL FK users.id

resolved_at             timestamptz NULL
resolution_note         varchar(2000) NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
OPEN
IN_PROGRESS
RESOLVED
CLOSED
```

Constraint:

```text
UNIQUE(request_number)
```

`user_id` may be NULL if the contact form is allowed before login.

---

## 66. `notifications`

Stores in-app notifications.

```text
notifications
-------------
id                      uuid PK
user_id                 uuid NOT NULL FK users.id

notification_type       varchar(50) NOT NULL

title                   varchar(255) NOT NULL
message                 varchar(2000) NOT NULL

data                    jsonb NULL

status                  varchar(20) NOT NULL DEFAULT 'UNREAD'

read_at                 timestamptz NULL

created_at              timestamptz NOT NULL
updated_at              timestamptz NOT NULL
deleted_at              timestamptz NULL
deleted_by              uuid NULL FK users.id
```

Statuses:

```text
UNREAD
READ
ARCHIVED
```

Example notification types:

```text
ORDER_STATUS_CHANGED
PAYMENT_CONFIRMED
DELIVERY_ASSIGNED
DELIVERY_COMPLETED
DEBT_DUE_SOON
DEBT_OVERDUE
CREDIT_LIMIT_CHANGED
LOW_STOCK
EXPIRY_WARNING
AI_REVIEW_COMPLETED
SYSTEM
```

`data` can store navigation metadata such as:

```json
{
  "entityType": "ORDER",
  "entityId": "..."
}
```

No financial/business truth is stored only inside Notification JSON.

---

## 67. `audit_logs`

Append-only system audit trail.

```text
audit_logs
----------
id                      uuid PK

store_id                uuid NULL FK stores.id
actor_user_id           uuid NULL FK users.id

action                  varchar(100) NOT NULL

entity_type             varchar(100) NOT NULL
entity_id               uuid NULL

old_values              jsonb NULL
new_values              jsonb NULL

reason                  varchar(1000) NULL

ip_address              varchar(64) NULL
user_agent              varchar(1000) NULL

correlation_id          varchar(100) NULL

occurred_at             timestamptz NOT NULL
```

Examples of audited actions:

```text
PRICE_OVERRIDE
PRICE_LIST_CHANGED

CREDIT_LIMIT_CHANGED
CREDIT_PROFILE_SUSPENDED

GOODS_RECEIPT_CONFIRMED
INVENTORY_ADJUSTED
STOCKTAKE_COMPLETED

CASH_PAYMENT_CONFIRMED
PAYMENT_REVERSED

DELIVERY_COMPLETED
DELIVERY_INCIDENT_RESOLVED

DEBT_ADJUSTED
DEBT_CANCELLED

RETURN_APPROVED
REFUND_COMPLETED

AI_REVIEW_CONFIRMED
AI_REVIEW_CORRECTED

USER_SUSPENDED
```

Rules:

```text
Audit Logs are append-only.

No DELETE API is provided for Audit Logs.

No ordinary UPDATE API is provided for Audit Logs.
```

`actor_user_id` may be NULL for system-generated operations such as:

```text
payOS webhook
scheduled expiry processing
system automation
```

`correlation_id` links multiple audit records created by one business transaction/request.

---

# XXV. FINAL TABLE LIST

## Authentication & Store

```text
1. roles
2. users
3. farmer_profiles
4. user_addresses
5. stores
6. store_members
```

## Customer Management

```text
7. customer_groups
8. customer_group_assignments
```

## Catalog & Product Packaging

```text
9. categories
10. brands
11. products
12. units
13. product_packagings
14. active_ingredients
15. product_active_ingredients
16. store_products
17. product_reviews
```

## Pricing

```text
18. price_lists
19. price_list_items
20. customer_group_price_lists
```

## Supplier & Stock Receiving

```text
21. suppliers
22. goods_receipts
23. goods_receipt_items
```

## Inventory

```text
24. inventory_lots
25. inventory_lot_balances
26. stock_movements
27. stock_movement_items
28. stocktakes
29. stocktake_items
30. inventory_reservations
31. inventory_reservation_items
```

## Sales & Payment

```text
32. carts
33. cart_items
34. orders
35. order_items
36. payments
37. payment_allocations
```

## Delivery

```text
38. deliveries
39. delivery_items
40. delivery_item_lot_allocations
41. delivery_attempts
42. delivery_attempt_items
43. delivery_incidents
```

## Credit

```text
44. credit_tiers
45. farmer_credit_profiles
46. credit_limit_histories
47. credit_reservations
```

## Accounts Receivable / Debt

```text
48. debt_accounts
49. debt_entries
50. debt_entry_actions
51. debt_transactions
```

## Return / Refund

```text
52. sales_returns
53. sales_return_items
54. refunds
```

## AI Diagnosis

```text
55. diseases
56. disease_treatments
57. ai_models
58. ai_policy_configs
59. diagnosis_cases
60. diagnosis_images
61. ai_inferences
62. agent_reviews
63. recommendation_items
```

## Content & System

```text
64. articles
65. contact_requests
66. notifications
67. audit_logs
```

**Final current baseline: 67 tables.**

---

# XXVI. FINAL CORE BUSINESS FLOWS

## 1. Supplier Receiving

```text
Supplier
→ Goods + Supplier Invoice
→ Manual Entry / AgriSage Excel Template
→ Goods Receipt DRAFT
→ Preview / Validate
→ Confirm
→ Inventory Lot
→ STOCK_IN
→ Weighted Average Cost updated
```

## 2. Pricing

```text
Registered Farmer
→ Customer Group
→ Price List
→ Packaging Price

or

Walk-in
→ Walk-in Default Price List

→ optional Staff Price Override
→ Order Item price snapshot
```

## 3. Sales / Credit

```text
Order
→ Confirmed Upfront Payment
→ Required Credit calculation
→ Credit Limit check
→ Credit Reservation
→ Inventory Reservation
```

## 4. Delivery

```text
Order
→ Delivery Note
→ Lot Allocation
→ Delivery Attempt
→ Attempt Items
→ Success / Partial Success / Failure
```

## 5. Successful Fulfillment

```text
Delivered Goods
→ SALE Stock Movement
→ Inventory Reservation consumed
→ COGS snapshot
→ Prepayment applied
→ Credit Reservation consumed
→ Debt Entry for unpaid delivered amount
→ Debt Transaction
```

## 6. Debt Collection

```text
Cash / payOS Payment
→ Payment PAID
→ Payment Allocation
→ oldest Due Debt first by default
→ PAYMENT Debt Transaction
→ Debt Account decreases
```

## 7. Return / Refund

```text
Fulfilled Item
→ Sales Return
→ Exact sold Lot trace
→ Inspection

RESELLABLE
→ RETURN_IN using original COGS

DAMAGED / EXPIRED / UNUSABLE
→ no available-stock return

Financial resolution
→ reduce unpaid Debt first
→ refund already-paid remainder
```

## 8. AI Diagnosis

```text
Farmer Image
→ AI Inference
→ Model + Policy snapshot
→ Authorized Human Review

CONFIRMED / CORRECTED
→ Verified Disease
→ Treatment
→ Suitable Active Ingredient
→ Store Product Recommendation

INCONCLUSIVE
→ no treatment/product recommendation
```

---

# XXVII. DATABASE IMPLEMENTATION ORDER

Even though all 67 tables are now designed, recommended migration/coding order is:

```text
Phase 1
Authentication
Store
Customer
Catalog
Packaging
Pricing

Phase 2
Supplier Receiving
Inventory
Lot / Expiry
Stocktake

Phase 3
Order
Payment
Credit
Inventory Reservation

Phase 4
Delivery
Accounts Receivable / Debt

Phase 5
AI Diagnosis
Content / Notifications

Phase 6
Return / Refund
Advanced Reporting
```

Database schema can still be created in the initial migration as a complete model if the team wants one full baseline.

Implementation phase order does not require removing later-phase tables from the database design.

---

# XXVIII. IMPORTANT DATABASE INVARIANTS

The Backend must preserve these rules:

```text
1. No hard delete through business APIs.

2. No physical stock change without a posted Stock Movement.

3. No sale below zero available inventory.

4. No Inventory Reservation above available Lot quantity.

5. No Credit Reservation above customer Available Credit.

6. Debt is created only for successfully fulfilled unpaid value.

7. Order confirmation alone never creates receivable debt.

8. Failed Delivery without transferred goods creates no SALE movement and no Debt.

9. Confirmed Payment only may reduce required credit or debt.

10. payOS webhook is authoritative for online payment confirmation.

11. Posted financial/inventory transactions are corrected by adjustment/reversal,
    not silent editing.

12. Packaging conversion and selling prices are snapshotted on transactions.

13. COGS is snapshotted using Weighted Average Cost of the actual issued Lot.

14. Expired / blocked / quarantined Lots cannot be sold.

15. A return cannot exceed previously fulfilled, not-yet-returned quantity.

16. Returned goods only re-enter available inventory after inspection confirms
    they are resellable.

17. Return compensation cannot both reduce the same unpaid debt and refund the
    same monetary portion.

18. One Farmer has only one active Customer Group per Store.

19. One Farmer has at most one active Credit Profile per Store.

20. Audit-sensitive operations must write Audit Logs.
```

---


# XXIX. MIGRATION-CANDIDATE RELATIONSHIP / FK POLICY

## 29.1 Global physical delete behavior

Because AgriSage uses Soft Delete at the business/API layer:

```text
Required business FK
→ ON DELETE RESTRICT / NO ACTION

Optional historical FK
→ normally ON DELETE NO ACTION
```

Do not configure physical cascading deletes across business history.

Examples that must never cascade physically:

```text
Product → Order Item
Supplier → Goods Receipt
Order → Delivery
Order → Debt Entry
Payment → Payment Allocation
Inventory Lot → Stock Movement Item
Diagnosis Case → Human Review
```

Self/history links such as:

```text
reversal_of_*
superseded_by_*
deleted_by
```

also use `NO ACTION / RESTRICT`.

---

## 29.2 Database-level constraint candidates

Implement directly in PostgreSQL / EF migrations where practical:

```text
quantity > 0
amount >= 0
rating BETWEEN 1 AND 5
confidence BETWEEN 0 AND 1

quantity_reserved <= quantity_on_hand

consumed + released <= reserved

fulfilled + cancelled <= ordered

delivered + released <= allocated

attempt delivered + failed = attempted

effective_to > effective_from

exactly one Payment Allocation target

exactly one Recommendation Item target

one active/default/current record through partial unique index
```

Cross-table semantic consistency remains in Application/Domain services.

---

## 29.3 Temporal uniqueness

The following temporal tables must not contain conflicting overlapping active periods:

```text
customer_group_assignments
price_lists
customer_group_price_lists
ai_policy_configs
```

Required rules:

```text
One Farmer has one Customer Group at a time.

One Customer Group has one applicable Price List at a time.

Only one Walk-in default Price List applies to a Store at a time.

A Model must not have overlapping active AI Policy periods.
```

Enforce using:

```text
transactional application validation
+
partial unique indexes where possible
+
PostgreSQL exclusion/range constraints where useful
```

---

# XXX. MIGRATION-CANDIDATE INDEX PLAN

> PostgreSQL does **not** automatically index FK columns.  
> Every operational FK used for joins/filtering should receive an explicit index unless already covered by a suitable unique/composite index.

## Authentication / Store

```text
users(role_id)
users(status)

store_members(user_id, status)
store_members(store_id, status)
```

## Customer

```text
customer_group_assignments(farmer_profile_id, effective_from DESC)
customer_group_assignments(customer_group_id, effective_from DESC)
```

Partial unique:

```text
one current Customer Group per Farmer
```

## Catalog / Product

```text
categories(parent_id)

products(category_id)
products(brand_id)
products(status)

store_products(store_id, is_active, is_sellable)
store_products(product_id)

product_packagings(product_id, status)
```

Recommended uniqueness:

```text
product_packagings(product_id, unit_id)
WHERE deleted_at IS NULL

one active is_base_unit per Product

barcode unique when non-null and active
```

## Pricing

```text
price_lists(store_id, status, effective_from DESC)

price_list_items(
    price_list_id,
    store_product_id,
    product_packaging_id
)

customer_group_price_lists(
    customer_group_id,
    effective_from DESC
)
```

Partial unique:

```text
one active Walk-in default Price List per Store
```

## Supplier / Receiving

```text
suppliers(store_id, name)

goods_receipts(store_id, received_at DESC)
goods_receipts(supplier_id, received_at DESC)
goods_receipts(status)

goods_receipt_items(goods_receipt_id)
goods_receipt_items(store_product_id)
goods_receipt_items(inventory_lot_id)
```

Recommended duplicate invoice protection:

```text
UNIQUE(supplier_id, supplier_invoice_number)
WHERE supplier_invoice_number IS NOT NULL
  AND deleted_at IS NULL
```

if Supplier invoice numbers are expected to be unique.

## Inventory

```text
inventory_lots(store_product_id, expiry_date)
inventory_lots(status, expiry_date)

inventory_reservation_items(inventory_lot_id)
inventory_reservation_items(order_item_id)

stock_movements(store_id, occurred_at DESC)
stock_movements(movement_type, occurred_at DESC)

stock_movement_items(stock_movement_id)
stock_movement_items(inventory_lot_id)

stocktake_items(stocktake_id)
stocktake_items(inventory_lot_id)
```

FEFO hot path:

```text
Store Product
→ eligible Lot status
→ expiry_date ascending
→ available balance
```

Test the query plan with realistic data volume before production.

## Sales / Orders

```text
orders(store_id, created_at DESC)
orders(farmer_profile_id, created_at DESC)
orders(status, created_at DESC)
orders(fulfillment_type, status)
orders(settlement_type, status)

order_items(order_id)
order_items(store_product_id)
```

## Payment

```text
payments(status, initiated_at DESC)
payments(payer_farmer_profile_id, initiated_at DESC)
payments(provider_order_code)

payment_allocations(payment_id)
payment_allocations(order_id)
payment_allocations(debt_entry_id)
```

## Delivery

```text
deliveries(order_id)
deliveries(assigned_to_member_id, status, scheduled_at)
deliveries(status, scheduled_at)

delivery_items(delivery_id)
delivery_items(order_item_id)

delivery_item_lot_allocations(delivery_item_id)
delivery_item_lot_allocations(inventory_lot_id)

delivery_attempts(delivery_id, attempt_number)

delivery_attempt_items(delivery_attempt_id)
delivery_attempt_items(delivery_item_lot_allocation_id)

delivery_incidents(delivery_id, status)
delivery_incidents(delivery_attempt_id)
```

## Credit / Accounts Receivable

```text
farmer_credit_profiles(store_id, farmer_profile_id)

credit_reservations(farmer_credit_profile_id, status)
credit_reservations(order_id)

debt_accounts(store_id, farmer_profile_id)

debt_entries(debt_account_id, status, due_date)
debt_entries(status, due_date)

debt_transactions(debt_account_id, occurred_at DESC)
debt_transactions(debt_entry_id, occurred_at DESC)
debt_transactions(payment_allocation_id)
```

Debt Aging hot path:

```text
(status, due_date)
```

## Return / Refund

```text
sales_returns(order_id, requested_at DESC)
sales_returns(status, requested_at DESC)

sales_return_items(sales_return_id)
sales_return_items(order_item_id)
sales_return_items(inventory_lot_id)

refunds(sales_return_id)
refunds(status, requested_at)
```

## AI

```text
diagnosis_cases(farmer_profile_id, submitted_at DESC)
diagnosis_cases(status, submitted_at)

diagnosis_images(diagnosis_case_id)

ai_inferences(diagnosis_case_id, inferred_at DESC)
ai_inferences(ai_model_id)

agent_reviews(diagnosis_case_id, reviewed_at DESC)

recommendation_items(diagnosis_case_id)
recommendation_items(store_product_id)
```

## Content / Notifications / Audit

```text
articles(status, published_at DESC)

contact_requests(status, created_at DESC)
contact_requests(assigned_to, status)

notifications(user_id, status, created_at DESC)

audit_logs(entity_type, entity_id, occurred_at DESC)
audit_logs(actor_user_id, occurred_at DESC)
audit_logs(correlation_id)
audit_logs(occurred_at DESC)
```

---

# XXXI. RULES ENFORCED IN APPLICATION / DOMAIN LAYER

These rules cross multiple tables and should not be implemented as fragile DB CHECK expressions:

```text
Product Packaging belongs to the Product behind Store Product.

Order Item Packaging belongs to Order Item Product.

Inventory Reservation Item Order Item belongs to Reservation Order.

Delivery Item Order Item belongs to Delivery Order.

Credit Reservation profile belongs to the same Farmer as the Order.

Product Review Farmer is the registered customer of the referenced Order.

Only FARMER users own Farmer Profiles.

Only Delivery Staff may be assigned delivery work.

Only authorized Store Owner/Sales Staff may Human Review AI.

FEFO selection.

Available Credit check + Credit Reservation is atomic.

Goods Receipt confirmation + Weighted Average Cost posting is atomic.

Delivery/Pickup success + Stock Out + Debt creation is atomic.

Return quantity never exceeds fulfilled-not-returned quantity.

Debt reduction + Refund never overcompensate a Return.
```

These must have integration tests.

---

# XXXII. V4 REVIEW CORRECTIONS APPLIED

The following review corrections are now incorporated into this Migration Candidate:

```text
✓ orders.settlement_type added
✓ orders.credit_term_days_snapshot added

✓ debt_transactions.payment_id replaced by payment_allocation_id

✓ stocktakes.adjustment_movement_id removed
✓ debt_entry_actions.related_debt_transaction_id removed

✓ Inventory Lot NULL-aware uniqueness defined
✓ no-lot Inventory bucket uniqueness defined

✓ order_items.base_price renamed suggested_unit_price

✓ delivery rejection removed from Sales Return semantics

✓ agent_reviews moved to Case-level review
  with optional primary_ai_inference_id

✓ one-active/current/default rules formalized as partial unique indexes

✓ temporal-overlap rules documented

✓ FK physical delete behavior standardized to RESTRICT / NO ACTION

✓ FK/hot-path index plan defined

✓ stock balance zero-cost invariant added

✓ Inventory Reservation Item unique Lot allocation added

✓ delivery_attempt_items unique Attempt/Lot Allocation added

✓ Debt Entry source FK optionality corrected for MANUAL_ADJUSTMENT

✓ one-primary Diagnosis Image rule added
```

---

# XXXIII. DESIGN STATUS

```text
Business Rules Freeze: COMPLETE
Table-level Design: COMPLETE
Field-level Design: COMPLETE (67/67 tables) – v4 reviewed
Core Relationships: COMPLETE – FK/constraint/index review applied
Inventory Costing Rule: CONFIRMED
Soft Delete Rule: CONFIRMED
Return/Refund Database Design: COMPLETE
Production AI Scope: RICE
Promotion Module: REMOVED
```

Next technical deliverables after reviewing this document:

```text
1. Final ERD / relationship validation
2. PostgreSQL constraints/index review
3. EF Core Entity classes
4. EF Core Fluent Configurations
5. Initial Migration
6. Apply Migration to Supabase
7. Seed roles / units / initial disease classes / initial store
```


---

# XXXIV. MIGRATION CANDIDATE FREEZE

This document is the proposed schema source for:

```text
EF Core Entities
→ Fluent Configurations
→ Initial Migration
→ Supabase PostgreSQL
```

Before creating `InitialMigration`, perform only these final implementation checks:

```text
1. Confirm exact EF Core enum/string mappings.
2. Confirm PostgreSQL partial-index syntax through Npgsql migrations.
3. Confirm NULLS NOT DISTINCT support on the selected Supabase PostgreSQL version.
4. Confirm migration ordering for cyclic historical/user FKs.
5. Create integration tests for all cross-table invariants.
```

After those implementation checks, schema changes should be introduced only through new EF Core migrations rather than manual schema edits in DBeaver.

---

# XXXV. POST-FREEZE IMPLEMENTATION CLARIFICATIONS (AGRI-9 / AGRI-10)

These clarifications resolve ambiguities found while implementing the Domain layer.
They do not change the frozen schema (tables/columns/types); they fix rounding behavior
and specify status-derivation / lifecycle rules that the frozen tables left open.

## 35.1 Deterministic Rounding (tables 13, 19, 23, 25, 27)

```text
Unit-cost fields stored as numeric(20,6) (base_unit_cost, average_unit_cost,
unit_cost_snapshot) are rounding results of a division and use
MidpointRounding.AwayFromZero to 6 decimal places.

Money fields stored as numeric(18,2) are never rounded by Backend.
Any input unit price/cost with more than 2 decimal places is rejected
at the Domain boundary instead of being rounded.

This keeps quantity × unit_price = line_total exact for every money field.
```

## 35.2 Weighted Average Cost — last unit of a Lot (table 25)

```text
When an issuance (SALE / ADJUSTMENT_OUT / RETURN cost reversal) reduces
quantity_on_hand to exactly zero, that issuance's stock_movement_items
total_cost_snapshot equals the Lot's remaining total_cost_value exactly
(not quantity × rounded average_unit_cost), so cumulative COGS always
equals cumulative incoming cost. total_cost_value is then explicitly set
to zero, per the existing rule in §25.
```

## 35.3 Reservation status derivation (tables 30, 47)

```text
inventory_reservations.status and credit_reservations.status are derived
from the item/line quantities, not set independently:

ACTIVE               → nothing consumed or released yet
PARTIALLY_CONSUMED   → some consumed, remaining > 0
CONSUMED             → remaining = 0 and something was consumed
RELEASED             → remaining = 0 and nothing was ever consumed
CANCELLED            → the Order was cancelled before any consumption

A partial release of the unused remainder does not change a status that
is already PARTIALLY_CONSUMED or CONSUMED; released_at/released_by/
release_reason record only the most recent release action.
```

## 35.4 Order / Order Item / Delivery Item lifecycle (tables 34, 35, 39)

```text
Recommended flow: PENDING_CONFIRMATION → CONFIRMED → [PREPARING] →
READY_FOR_FULFILLMENT.

Order Items may only be added/changed/removed while the Order is
PENDING_CONFIRMATION.

Order.status, order_items.status and delivery_items.status are derived
from fulfilled/cancelled quantities against the ordered/planned quantity:

- fulfilled = ordered, cancelled = 0          → COMPLETED / FULFILLED / DELIVERED
- cancelled = ordered, fulfilled = 0          → CANCELLED
- fulfilled > 0 and cancelled > 0             → PARTIALLY_CANCELLED (terminal)
- fulfilled > 0, remainder still open         → PARTIALLY_FULFILLED / PARTIALLY_DELIVERED

PARTIALLY_CANCELLED is a terminal state: once part of a line has fulfilled
and part has been cancelled, no further change occurs to that line.
Full cancellation (Order.status = CANCELLED) is only possible before
anything has been fulfilled.

fulfillment_type = DELIVERY requires recipient_name_snapshot,
recipient_phone_snapshot, delivery_address_line and delivery_province.
pickup_completed_at/pickup_completed_by are set only when a PICKUP Order
finishes (successfully or otherwise) at the store counter.
```

## 35.5 Delivery lifecycle (table 38)

```text
DRAFT → ASSIGNED → OUT_FOR_DELIVERY.

After each Delivery Attempt completes:

- nothing remains to deliver           → DELIVERED
- some delivered, remainder still open → PARTIALLY_DELIVERED
- attempt failed entirely              → RETRY_PENDING

A Delivery in PARTIALLY_DELIVERED or RETRY_PENDING may be dispatched
again for a new Attempt; only one Attempt may be IN_PROGRESS at a time.
Cancelling a Delivery is only allowed while no Attempt is IN_PROGRESS;
it releases/cancels the not-yet-delivered remainder. If nothing had been
delivered yet, the Delivery itself ends CANCELLED. If part of it had
already been delivered, the Delivery ends DELIVERED (there is no
PARTIALLY_CANCELLED status on deliveries) and the cancelled remainder
shows on the affected delivery_items as PARTIALLY_CANCELLED instead.

Changing the actual Lot for a Delivery Item before dispatch releases the
existing delivery_item_lot_allocations row and creates a new one, rather
than mutating inventory_lot_id in place, so the allocation history stays
intact.
```

## 35.6 Debt Entry Actions (tables 49, 50, 51)

```text
debt_entries.source_type = DELIVERY / PICKUP creates a CREDIT_SALE
Debt Transaction (+). source_type = MANUAL_ADJUSTMENT creates an
ADJUSTMENT_IN Debt Transaction (+) and does not require a
debt_entry_action row.

DISPUTE marks the Entry DISPUTED but does not block further Payment
Allocations against it. KEEP / ADJUST / CANCEL are the actions that
resolve a dispute (or apply directly, without a prior DISPUTE).

ADJUST only decreases outstanding_amount, via an ADJUSTMENT_OUT Debt
Transaction linked to the Action; increasing a Farmer's receivable is
done by creating a new MANUAL_ADJUSTMENT Debt Entry, not by ADJUST.

CANCEL posts an ADJUSTMENT_OUT for the full remaining outstanding_amount
and moves the Entry to CANCELLED.

debt_entry_actions.adjustment_amount follows the debt_transactions sign
convention: negative for ADJUST/CANCEL (receivable decreases). It is
NULL for DISPUTE, KEEP and CHANGE_DUE_DATE, which do not move money.

debt_entries.status is derived from outstanding_amount vs original_amount:
OPEN (outstanding = original) → PARTIALLY_PAID (0 < outstanding <
original) → PAID (outstanding = 0); ADJUSTED marks an Entry whose
outstanding was reduced by ADJUST and is still open (outstanding > 0).
```

## 35.7 Payment Allocation binding (table 37)

```text
payment_allocations.allocation_type is bound to the paying Payment's
payment_context:

payment_context = ORDER_PAYMENT  → allocation_type = ORDER only
payment_context = DEBT_REPAYMENT → allocation_type = DEBT only

An allocation may only be created once the Payment is PAID.
An ORDER allocation cannot be reversed once
prepayment_consumed_amount > 0 for it.
PARTIALLY_REFUNDED / REFUNDED Payment statuses are populated by the
Return/Refund feature, not by this task.
```

## 35.8 Stocktake completion (tables 28, 29)

```text
Stocktake.Complete() requires every non-deleted stocktake_items row to
have a non-null counted_quantity; a row left uncounted blocks
completion instead of being silently skipped.

difference_quantity = counted_quantity - system_quantity_snapshot.
difference_cost_value = difference_quantity × unit_cost_snapshot when
unit_cost_snapshot is present, otherwise NULL.
```

## 35.9 Sales Return lifecycle (tables 52, 53)

```text
REQUESTED → APPROVED | REJECTED | CANCELLED
APPROVED  → RECEIVED | CANCELLED
RECEIVED  → INSPECTED   (every return line has a condition other than
                         PENDING_INSPECTION)

Return lines may only be added/removed while REQUESTED.

PARTIALLY_RESOLVED: after INSPECTED, at least one resolution step has
been recorded (RETURN_IN stock movement linked, debt adjustment
transaction linked, or a Refund COMPLETED), but not all of them.

COMPLETED: every RESTOCK line has its RETURN_IN stock movement linked
and the sum of COMPLETED Refunds equals total_refund_amount.

REJECTED only changes the status; there are no rejected_at/rejected_by
columns. The actor and reason are recorded through audit_logs.
```

## 35.10 Return quantity, source and value (table 53)

```text
returned_base_quantity
<=
order_items.fulfilled_base_quantity
- already returned base quantity

"Already returned" = returned_base_quantity of the same Order Item on
all other Sales Returns that are not REJECTED or CANCELLED (in-flight
returns count, so the same goods cannot be requested twice).

Every return line references exactly one fulfillment source, matching
the Order's fulfillment_type:
DELIVERY → delivery_item_lot_allocation_id only
PICKUP   → original_stock_movement_item_id only
The DB CHECK enforces exactly one of the two columns; matching the
fulfillment type (needs orders) is enforced by the Domain.

selling_unit_price_snapshot and conversion_to_base_snapshot are taken
from the Order Item.

return_value
=
round2(returned_base_quantity × unit_price ÷ conversion_to_base_snapshot)
using MidpointRounding.AwayFromZero, multiplying before dividing.
This is the only computed money amount that is rounded (exception to
coding rule #61); returning a whole packaging reproduces its price.

return_inventory_cost_value = returned_base_quantity × original_cogs_unit_cost
when original_cogs_unit_cost is known.
```

## 35.11 Return inspection, settlement and refunds (tables 52, 53, 54)

```text
inventory_disposition is derived from condition_status:
RESELLABLE                   → RESTOCK
DAMAGED / EXPIRED / UNUSABLE → WRITE_OFF
PENDING_INSPECTION           → NONE
Only RESTOCK lines may be linked to a RETURN_IN stock movement.

Settlement is recorded once, when inspection completes:
total_return_amount   = Σ return_value
total_debt_adjustment = attributable unpaid debt reduced first
                        (calculated by the Application)
total_refund_amount   = total_return_amount - total_debt_adjustment
so every returned value is compensated exactly once.

Refunds belong to their Sales Return. The sum of Refunds that are not
CANCELLED or FAILED cannot exceed total_refund_amount.
Refund: PENDING → COMPLETED | FAILED | CANCELLED (all terminal);
a failed refund is retried with a new Refund. FAILED changes the status
only (there is no failed_at column).
```

## 35.12 Diagnosis Case, Human Review and recommendations (tables 57–63)

```text
SUBMITTED → PROCESSING   (at least one active image)
PROCESSING → AI_COMPLETED (at least one SUCCESS inference) | FAILED
FAILED → PROCESSING       (AI may be run again)
AI_COMPLETED → UNDER_REVIEW (optional)

Human Review is allowed from AI_COMPLETED, UNDER_REVIEW and FAILED
(manual diagnosis), and again from VERIFIED / INCONCLUSIVE:
CONFIRMED / CORRECTED → VERIFIED with final_disease_id
INCONCLUSIVE          → INCONCLUSIVE, final_disease_id NULL
A new review supersedes the current one (is_current = false,
superseded_at, superseded_by_review_id) and deactivates the
recommendations attached to it. Review history is never overwritten.
CANCELLED is possible from any state before a review completes.

Review rules:
- primary_ai_inference_id is optional; when present it belongs to the
  case, has status SUCCESS, and supplies ai_disease_id_snapshot.
- CONFIRMED with a primary inference requires final disease =
  the inference's predicted disease; otherwise use CORRECTED.
- A case with no SUCCESS inference (e.g. FAILED) cannot be CONFIRMED;
  the reviewer resolves it as CORRECTED or INCONCLUSIVE.

Each inference references an ACTIVE AI Model and an ACTIVE Policy of
that model effective at inferred_at. passed_policy = true requires
status SUCCESS and confidence >= minimum_confidence.

ai_policy_configs.requires_human_review is kept as designed but never
bypasses review: recommendations always require a VERIFIED case.

Recommendations:
- only for a VERIFIED case, attached to its current review;
- exactly one target: TREATMENT → active disease_treatment of the final
  disease; PRODUCT → active and sellable store_product;
- a Healthy final disease receives TREATMENT (guidance) only, no PRODUCT.
```

## 35.13 Content, Notifications and Audit (tables 64–67)

```text
Only the documented invariants are enforced; no state-transition graph:
PUBLISHED   → published_at set
RESOLVED    → resolved_at set (contact request)
READ        → read_at set (notification)

audit_logs is append-only: created once, never updated or deleted.
```

## 35.14 Persistence clarifications (AGRI-12)

```text
delivery_attempt_items (refines §29.2 "attempt delivered + failed = attempted"):
DB CHECK:  attempted_base_quantity > 0
           delivered_base_quantity >= 0
           failed_base_quantity >= 0
           delivered_base_quantity + failed_base_quantity <= attempted_base_quantity
Equality (delivered + failed = attempted) is enforced by the Domain when
the attempt is completed; an IN_PROGRESS attempt has delivered = failed = 0.

users contact CHECK (blank/whitespace values do not count, as in the Domain):
NULLIF(BTRIM(email), '') IS NOT NULL
OR NULLIF(BTRIM(phone_number), '') IS NOT NULL

sales_return_items source CHECK (exactly one):
(delivery_item_lot_allocation_id IS NULL)
<> (original_stock_movement_item_id IS NULL)

Expression indexes are created with raw SQL in the InitialCreate
migration (EF Core cannot model them):
ux_inventory_lots_logical_lot (store_product_id, lower(lot_number),
  expiry_date) NULLS NOT DISTINCT WHERE deleted_at IS NULL
  AND lot_number IS NOT NULL
ux_users_email_lower (LOWER(email))
  WHERE deleted_at IS NULL AND email IS NOT NULL

Indexes: operational FKs are indexed; FKs to users that only record an
actor (deleted_by, *_by, author_id) are not indexed unless §XXX lists a
composite index for them.

Additional single-row DB rules (beyond the "Constraints" sections):
- one open (ACTIVE / PARTIALLY_CONSUMED) inventory reservation and one
  open credit reservation per Order;
- one default address per user; one active default customer group per
  Store;
- orders: REGISTERED ⇔ farmer_profile_id NOT NULL; WALK_IN ⇒
  FULL_PAYMENT; price_overridden ⇒ override_reason and overridden_by;
- product_packagings: is_base_unit ⇒ conversion_to_base = 1;
- debt_entries source requirements; PAYMENT debt transaction ⇒
  payment_allocation_id NOT NULL;
- recommendation_items exactly one target;
- diagnosis_cases: VERIFIED ⇒ final_disease_id NOT NULL,
  INCONCLUSIVE ⇒ NULL; agent_reviews: CONFIRMED / CORRECTED ⇔
  final_disease_id NOT NULL;
- articles: PUBLISHED ⇒ published_at NOT NULL.
Every enum-backed varchar column also has an IN (...) CHECK of its
documented values (§0.5).
```

## 35.16 Catalog clarifications

```text
product_packagings.status (free varchar in §13): allowed values ACTIVE / INACTIVE.
INACTIVE keeps the history of a packaging that is no longer sold or bought.

A Store Product may be created or marked sellable only if the Product is ACTIVE and
has an ACTIVE base packaging and at least one ACTIVE sale packaging.

product_active_ingredients UNIQUE(product_id, active_ingredient_id) also counts
soft-deleted rows: adding a removed ingredient again revives the old row
(Domain Reinstate), it never inserts a second one.

categories.code has no unique constraint in the schema; Application rejects a code
already used by another non-deleted category (case-insensitive).

Product DELETE = status DISCONTINUED and the Store Products are deactivated (SKU and
history kept). Packagings, categories, brands and active ingredients are soft deleted
only when nothing references them.

Public catalog (no sign-in): only ACTIVE, sellable Store Products of ACTIVE Products;
no stock, minimum stock, store SKU, lot/expiry flags, internal statuses or audit data.
```

## 35.15 Persistence behavior (AGRI-13)

```text
Applied centrally by EF Core SaveChanges interceptors (no schema change):

Audit timestamps (every table except audit_logs):
- insert: created_at = updated_at = IDateTimeProvider.UtcNow
- update: updated_at = UtcNow; created_at is never changed
- caller-supplied values are overwritten

Soft delete (every table with deleted_at):
- EF Remove() of a row is converted to an UPDATE through the Domain
  MarkDeleted(): deleted_at = UtcNow, deleted_by = current user
- the conversion keeps the EF original values (concurrency snapshot)
- the Domain delete rules still apply: aggregate children cannot be
  removed directly (only via the root), confirmed/posted records and
  ledger rows cannot be deleted, an already-deleted row cannot be
  deleted again (all → DomainException)
- a Domain soft delete (MarkDeleted / root.RemoveItem) keeps the
  deleted_by / deleted_at passed by the Application
- soft-deleting a root does not soft-delete its children, and EF
  cascade fix-up is deferred to SaveChanges so tracked dependents' FKs
  are never nulled by a Remove()
- deleted_by = users.id from the JWT `sub` claim; NULL when no user is
  authenticated (background/system work); no system user id is invented

Query filters: every table with deleted_at is filtered by
deleted_at IS NULL; IgnoreQueryFilters() is the explicit escape hatch for
history/audit/admin reads. A required-navigation Include to a
soft-deleted principal hides the dependent row too.

audit_logs: append-only; an update or delete through EF is rejected.

Optimistic concurrency — exactly the four tables with a version column
(inventory_lot_balances, orders, farmer_credit_profiles, debt_accounts):
- insert: version = 0
- every persisted update of that row (including a soft delete):
  version = original version + 1; the UPDATE is checked against the
  original version → a conflicting write fails with
  DbUpdateConcurrencyException (no automatic retry)
- no change → no increment
- a change only to child rows does not increment the root's version

Bulk APIs bypass the interceptors (no timestamps, soft delete, version or
audit): ExecuteDelete / ExecuteDeleteAsync are forbidden for business
tables; ExecuteUpdate / ExecuteUpdateAsync are forbidden for normal
business mutations unless explicitly reviewed.
```
