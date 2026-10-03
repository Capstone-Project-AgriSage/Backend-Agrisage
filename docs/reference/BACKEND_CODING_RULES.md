# AgriSage Backend Architecture & Coding Rules v1

> **Purpose:** Quy định bắt buộc khi triển khai Backend AgriSage.  
> **Architecture:** 4 layers + feature-based folders.  
> **Applies to:** `AgriSage.Api`, `AgriSage.Application`, `AgriSage.Domain`, `AgriSage.Infrastructure`, backend tests.

---

# 1. Mandatory Architecture

```text
AgriSage.Api
AgriSage.Application
AgriSage.Domain
AgriSage.Infrastructure
```

Dependency:

```text
Domain
↑
Application
↑
Api

Infrastructure
→ Application
→ Domain
```

Allowed:

```text
Domain → no project reference
Application → Domain
Infrastructure → Application + Domain
Api → Application + Infrastructure
```

Forbidden:

```text
Domain → Infrastructure       ❌
Domain → Application          ❌
Application → Api             ❌
Application → Infrastructure  ❌
Infrastructure → Api          ❌
```

---

# 2. Organize by Feature

Không gom tất cả file vào các folder global lớn như:

```text
DTOs/
Services/
Controllers/
Validators/
```

Phải chia theo nghiệp vụ:

```text
Products/
Orders/
Payments/
Inventory/
Deliveries/
Credit/
Debt/
Returns/
Diagnosis/
...
```

Ví dụ:

```text
AgriSage.Application/
└── Features/
    └── Products/
        ├── Dtos/
        ├── Interfaces/
        ├── Services/
        ├── Validators/
        └── Mappings/
```

---

# 3. Controller = HTTP Only

Controller chỉ:

```text
Receive HTTP Request
→ Request DTO
→ Authorization
→ Call Application Service
→ Response DTO
→ HTTP Response
```

Controller được phép:

- nhận route/query/body
- nhận Request DTO
- dùng `CancellationToken`
- gọi Service
- dùng authorization policy/attribute
- trả HTTP status/response

Controller **không được**:

```text
Query DbContext
Calculate price
Check/reserve stock
Calculate credit
Create debt
Call payOS directly
Call AI directly
Open EF transaction
Modify Entity
Contain business rule
```

---

# 4. Application Service = Use-case Orchestration

Service chịu trách nhiệm:

```text
Load data
→ Validate business state
→ Call Domain logic
→ Coordinate Entities
→ Call external abstractions
→ Own transaction boundary
→ Save
→ Return DTO
```

Ví dụ `ConfirmDeliveryAsync()`:

```text
Validate Delivery
→ Validate Lots
→ Stock Out
→ Consume Inventory Reservation
→ Calculate COGS
→ Apply Prepayment
→ Consume Credit Reservation
→ Create Debt Entry
→ Create Debt Transaction
→ Update Delivery/Order
→ Commit
```

---

# 5. Business Logic Placement

## Domain / Entity

Logic bảo vệ invariant của chính Entity:

```text
Order.MarkConfirmed()
Order.Cancel()
InventoryReservation.Consume()
InventoryReservation.Release()
CreditReservation.Consume()
DebtEntry.ApplyPayment()
```

## Application Service

Logic điều phối nhiều module/entity:

```text
Order Confirmation
Delivery Fulfillment
Goods Receipt Confirmation
Debt Collection
Return Settlement
```

---

# 6. Domain Must Not Depend on DTO

Sai:

```csharp
public void Update(UpdateProductRequest dto)
```

Domain chỉ nhận primitive/value/domain objects.

---

# 7. DTO Can Be Used by Controller and Application Service

Valid flow:

```text
Controller
↓
Request DTO
↓
Application Service
↓
Domain / Database
↓
Response DTO
↓
Controller
```

Rules:

```text
Api → may use Application DTO
Application → may use Application DTO
Domain → MUST NOT use Application DTO
Infrastructure → MUST NOT depend on API DTO
```

---

# 8. Never Return Entity Directly from API

Không:

```csharp
return Ok(productEntity);
```

Phải:

```text
Entity → Response DTO
```

`Entity` là domain/database model.  
`DTO` là API contract.

---

# 9. Separate Request and Response DTO

Nên dùng:

```text
CreateProductRequest
UpdateProductRequest
ProductResponse
ProductDetailResponse
```

Không dùng một `ProductDto` cho mọi trường hợp nếu không cần thiết.

---

# 10. DTO = Data Only

DTO không chứa:

```text
Price calculation
Credit calculation
Business rule
Database query
```

---

# 11. Validation Has 3 Layers

## Request validation

```text
Required
Length
Format
Quantity > 0
```

→ `Validators/`

## Business validation

```text
Product exists?
Product active?
Lot expired?
Enough credit?
Valid state transition?
```

→ Application / Domain

## Database integrity

```text
NOT NULL
FK
UNIQUE
CHECK
INDEX
```

→ EF Configuration / PostgreSQL

---

# 12. Never Trust Client Business Values

Backend không tin giá client gửi.

Price flow:

```text
Farmer
→ Customer Group
→ Price List
→ Packaging
→ Suggested Price
```

Staff override vẫn phải lưu:

```text
suggested_unit_price
unit_price
price_overridden
overridden_by
override_reason
```

---

# 13. DbContext Must Never Appear in Controller

Controller inject:

```text
IProductService
```

Application dùng:

```text
IAgriSageDbContext
```

Infrastructure implement:

```text
AgriSageDbContext
```

---

# 14. Application Depends on Interfaces, Not Infrastructure Classes

Sai:

```csharp
PaymentService(PayOsPaymentGateway payOs)
```

Đúng:

```csharp
PaymentService(IPaymentGateway paymentGateway)
```

---

# 15. External Services Always Behind Interfaces

```text
payOS → IPaymentGateway
Python FastAPI → IAiDiagnosisClient
Object Storage → IFileStorageService
Excel (.xlsx) → IReceiptSpreadsheet
Clock → IDateTimeProvider
```

---

# 16. Use `IDateTimeProvider`

Không rải `DateTime.UtcNow` khắp code.

```csharp
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
```

---

# 17. Multi-table Use Cases Must Be Atomic

Ví dụ:

```text
Goods Receipt Confirmation
Delivery Success
Debt Payment
Return Settlement
```

Nếu một bước fail:

```text
ROLLBACK ALL
```

---

# 18. One Transaction Owner per Use Case

Không:

```text
DeliveryService opens transaction
→ InventoryService opens another
→ DebtService opens another
```

Một use case chỉ có một transaction owner chính.

---

# 19. Do Not Call SaveChanges Randomly

Một business transaction:

```text
Update Order
Update Inventory
Create Movement
Create Debt
→ SaveChangesAsync()
→ Commit
```

Không commit từng bước riêng.

---

# 20. No Physical Inventory Change Without Stock Movement

Mọi thay đổi `quantity_on_hand` phải có:

```text
StockMovement
+
StockMovementItem
```

`quantity_reserved` không phải physical stock movement.

---

# 21. No Debt Change Without Debt Transaction

Mọi thay đổi Accounts Receivable:

```text
DebtTransaction
+
DebtAccount balance update
```

trong cùng transaction.

---

# 22. Credit Limit Change Requires History + Audit

Mọi thay đổi:

```text
farmer_credit_profiles.credit_limit
```

phải sinh:

```text
credit_limit_histories
audit_logs
```

---

# 23. Do Not Silently Edit Confirmed/Posted Transactions

Ví dụ:

```text
Goods Receipt CONFIRMED
Stock Movement POSTED
Debt Transaction POSTED
Payment PAID
```

Sai thì dùng:

```text
Adjustment
Reversal
Cancellation
Correction transaction
```

---

# 24. Soft Delete Is Mandatory

API DELETE không được physical delete.

Dùng:

```text
deleted_at
deleted_by
is_active
```

hoặc state:

```text
CANCELLED
VOIDED
REVERSED
```

---

# 25. Global Query Filter for Soft Delete

```csharp
builder.HasQueryFilter(x => x.DeletedAt == null);
```

`IgnoreQueryFilters()` chỉ dùng cho explicit admin/audit case.

Filter được áp tự động cho mọi entity kế thừa `SoftDeletableEntity` (AGRI-13); không khai báo lại
trong từng configuration. Lưu ý: `Include` qua navigation bắt buộc tới principal đã soft delete
sẽ ẩn luôn dependent — truy vấn lịch sử cần `IgnoreQueryFilters()`.

---

# 26. No Generic Repository by Default

Không tạo `IGenericRepository<T>` chỉ để wrap EF Core CRUD.

Dùng:

```text
IAgriSageDbContext
```

Specialized repository chỉ tạo khi thật sự có giá trị.

---

# 27. Never Expose IQueryable to Controller

Application Service phải execute query và trả DTO.

---

# 28. Read-only Queries Use `AsNoTracking()`

```csharp
_db.Products
    .AsNoTracking()
```

Tracking chỉ dùng khi sẽ update Entity.

---

# 29. Query Only Required Data

Ưu tiên projection:

```csharp
.Select(x => new ProductResponse
{
    Id = x.Id,
    Name = x.Name
})
```

Không load object graph dư thừa.

---

# 30. List API Must Be Paginated

Standard:

```text
page
pageSize
search
status
sort
filters
```

Dùng:

```text
PagedResult<T>
```

Giới hạn `pageSize`, ví dụ <= 100.

---

# 31. No Magic Strings for Status

Không:

```csharp
order.Status == "CONFIRMED"
```

Dùng enum/constant:

```text
OrderStatus.Confirmed
PaymentStatus.Paid
DeliveryStatus.Delivered
```

---

# 32. Important Entity State Should Not Have Arbitrary Public Setters

Không:

```csharp
order.Status = OrderStatus.Completed;
```

Nên:

```csharp
order.MarkCompleted(now);
```

---

# 33. State Transitions Must Be Controlled

Không cho transition bất hợp lệ như:

```text
CANCELLED → COMPLETED
DELIVERED → OUT_FOR_DELIVERY
```

---

# 34. Mapping Belongs in Application

```text
Entity → Response DTO
Request DTO → creation/update input
```

Ưu tiên manual/explicit mapping cho nghiệp vụ quan trọng.

---

# 35. Unified Exception Model

Dùng:

```text
NotFoundException
ValidationException
ConflictException
ForbiddenException
BusinessRuleException
AuthenticationFailedException   (401: sai thông tin đăng nhập / chưa xác thực)
```

`GlobalExceptionHandler` (Api) ánh xạ: Validation 400, AuthenticationFailed 401, Forbidden 403, NotFound 404,
Conflict 409, BusinessRule/Domain 422, còn lại 500 (không lộ chi tiết).

Không trả `bool`, `null`, hoặc string tùy ý cho business failure.

---

# 36. Controller Does Not Catch Business Exceptions

Dùng:

```text
ExceptionHandlingMiddleware
```

Không `try/catch` từng Controller action.

---

# 37. Never Return Raw Exception Details

Không expose:

```text
SQL error
Stack trace
Connection string
Secrets
Internal paths
```

Response production chỉ chứa safe error information + `traceId`.

---

# 38. Authorization = Role + Ownership + Permission

Ví dụ:

```text
Farmer sees own Order only
Delivery Staff handles assigned Delivery only
AI Reviewer requires can_review_ai = true
```

---

# 39. Never Trust User Identity/Role from Request

Current user lấy từ:

```text
JWT Claims
ICurrentUserService
```

Không tin role/user ID do client tự khai nếu không cần thiết.

---

# 40. Sensitive Actions Must Be Audited

Bắt buộc audit:

```text
Price Override
Credit Limit Change
Cash Payment Confirmation
Inventory Adjustment
Stocktake Completion
Goods Receipt Confirmation
Delivery Confirmation
Debt Adjustment
Refund
AI Review
User Suspension
```

---

# 41. payOS Webhook Must Be Idempotent

Webhook lặp lại không được apply Payment lần hai.

Phải kiểm tra Payment/provider transaction đã xử lý chưa.

---

# 42. Verify External Callback Data

payOS:

```text
Verify signature
Verify amount
Verify order code
Verify state
```

AI response cũng phải validate schema/status.

---

# 43. CancellationToken Is Mandatory

Propagate từ:

```text
Controller
→ Service
→ EF Core / HttpClient / Storage
```

---

# 44. Async End-to-End for I/O

Dùng:

```text
ToListAsync
FirstOrDefaultAsync
SaveChangesAsync
SendAsync
```

Không dùng:

```text
.Result
.Wait()
```

---

# 45. Naming Convention

Classes:

```text
ProductService
CreateProductRequest
ProductResponse
ProductConfiguration
```

Interfaces:

```text
IProductService
IPaymentGateway
```

Async methods:

```text
CreateAsync
GetByIdAsync
ConfirmAsync
```

Boolean:

```text
IsActive
CanReviewAi
HasExpired
```

---

# 46. Avoid God Services

Không để một service hàng nghìn dòng.

Khi thật sự lớn có thể tách:

```text
OrderPricingService
OrderCreditService
OrderFulfillmentService
```

Nhưng không over-engineer từ đầu.

---

# 47. Features Must Not Mutate Each Other Arbitrarily

Một feature không được tự ý sửa state nội bộ feature khác nếu nó không sở hữu use case.

---

# 48. Cross-feature Transaction Has One Owner

Ví dụ:

```text
Confirm Goods Receipt → GoodsReceiptService
Confirm Delivery → Delivery/Fulfillment Service
Collect Debt → Debt/Payment use case
```

---

# 49. Critical Calculations Have One Source of Truth

Không duplicate formula:

```text
Available Credit
Required Credit
Weighted Average Cost
Debt Outstanding
Return Value
```

---

# 50. Money Uses `decimal`

.NET:

```text
decimal
```

Database:

```text
numeric
```

Không dùng `double`/`float` cho tiền.

---

# 51. Internal Entity IDs Use `Guid`

Ngoại lệ: external provider IDs có thể dùng kiểu provider yêu cầu.

---

# 52. Timestamp Types

Use:

```text
DateTimeOffset ↔ timestamptz
DateOnly ↔ date
```

---

# 53. EF Core Migration Is Schema Source of Truth

Sau `InitialMigration`:

```text
Change Entity/Configuration
→ Add Migration
→ Review
→ Apply
```

Không `ALTER TABLE` thủ công bằng DBeaver.

---

# 54. Review Every Migration Before Merge

Kiểm tra:

```text
DROP COLUMN
Data loss
Cascade delete
Indexes
Constraints
Defaults
```

---

# 55. Seed Only Reference/System Data

Có thể seed:

```text
Roles
Units
Rice Diseases
Initial Store
```

Không seed fake production transactions.

---

# 56. Never Commit Secrets

Không commit:

```text
Supabase password
JWT secret
payOS API key
payOS checksum key
Storage secret
```

Dùng Environment Variables / User Secrets / Deployment Secrets.

---

# 57. Structured Logging

Nên:

```csharp
_logger.LogInformation(
    "Payment {PaymentId} confirmed for order {OrderId}",
    payment.Id,
    order.Id);
```

Không log password, JWT, provider secret hoặc dữ liệu nhạy cảm không cần thiết.

---

# 58. Testing Rules

## Unit Tests

```text
Domain calculations
State transitions
Invariant validation
Pure business logic
```

## Integration Tests

```text
EF constraints
Transactions
Webhook idempotency
Inventory reservation concurrency
Credit reservation
Delivery → Debt
Return → Debt/Refund
```

Các module high-risk:

```text
Inventory
Credit
Payment
Delivery
Debt
Return
```

---

# 59. Pull Request Architecture Checklist

Không merge nếu:

```text
Controller contains business logic
Controller accesses DbContext
Controller calls provider SDK
Entity depends on DTO
Application depends on concrete Infrastructure class
Entity returned directly from API
Hard delete introduced
Inventory changes without Stock Movement
Debt changes without Debt Transaction
Transaction owner unclear
Unsafe migration not reviewed
```

---

# 60. Standard API Flow

```text
HTTP
↓
ProductsController
↓
CreateProductRequest
↓
Validator
↓
IProductService
↓
ProductService
↓
Domain Entity
↓
IAgriSageDbContext
↓
AgriSageDbContext
↓
PostgreSQL
↓
ProductResponse
↓
Controller
↓
HTTP Response
```

Forbidden:

```text
Controller → DbContext
Controller → payOS SDK
Controller → AI HTTP Client
```

---

# 61. Deterministic Rounding for Money and Unit Costs

Extends #50 (Money Uses `decimal`).

```text
Unit-cost fields (numeric(20,6): base_unit_cost, average_unit_cost,
unit_cost_snapshot, ...) are rounding results of a division.
Round with MidpointRounding.AwayFromZero to 6 decimal places.

Money fields (numeric(18,2)) are never rounded by Backend.
Reject any unit price/cost input with more than 2 decimal places
instead of rounding it, so quantity × unit_price stays exact.
```

Domain implements this once, in `AgriSage.Domain.Common`: `CostRounding.RoundUnitCost`
for the division, and `Guard.Money` / `Guard.UnitCost` / `Guard.NonNegativeMoney` /
`Guard.PositiveMoney` to reject out-of-precision input. Reuse these instead of calling
`Math.Round` directly in a feature.

Single exception: a Sales Return line's `return_value` is money computed by division
(`returned_base_quantity × unit_price ÷ conversion`). It is rounded to 2 decimals with
AwayFromZero, multiplying before dividing, through `CostRounding.RoundMoney`
(database design §35.10).

---

# 62. Aggregate Root Owns Child Entity Mutation

An entity that only exists as part of another entity's aggregate (e.g. a Goods Receipt
Item, an Order Item, a Stock Movement Item) must not expose any public mutation —
including soft delete — that lets a caller change or remove it without going through
the aggregate root.

Sai:

```csharp
goodsReceiptItem.MarkDeleted(userId, now);
```

Đúng:

```csharp
goodsReceipt.RemoveItem(itemId, userId, now);
```

Child entities inherit a dedicated Domain base type (`SoftDeletableChildEntity`) whose
soft-delete hook always throws, so only the aggregate root, in the same Domain
assembly, can perform the mutation internally.

---

# 63. Ledger-Owning Aggregate Mediates Balance-Affecting Mutation

Some entities have their own identity and are read independently (they are not a
`SoftDeletableChildEntity`), but every change to them must also update a balance/ledger
owned by another aggregate — e.g. a Debt Entry's outstanding amount must always move
together with its Debt Account's `current_balance` and a new Debt Transaction row.

For these, the balance-affecting methods (payment, adjustment, cancellation) are
`internal`, callable only by the ledger-owning aggregate (in the same Domain assembly).
Read-only state (status, actions/history) stays public.

Sai:

```csharp
debtEntry.Adjust(amount, reason, staffId); // bypasses the account balance and ledger
```

Đúng:

```csharp
debtAccount.AdjustEntry(debtEntry, amount, reason, staffId, now);
```

Example: `AgriSage.Domain.Features.Debt.Entities.DebtEntry` / `DebtAccount`.

---

# 64. Persistence Bookkeeping Is Central — Do Not Bypass SaveChanges

`created_at` / `updated_at`, soft delete (`deleted_at` / `deleted_by`) and `version` are set by the
EF Core SaveChanges interceptors (`Infrastructure/Persistence/Interceptors`, database design §35.15).
Business code never assigns them.

Bulk APIs run SQL directly and **bypass every interceptor** (no timestamps, no soft delete,
no version increment, no concurrency check):

```text
ExecuteDelete / ExecuteDeleteAsync   → forbidden for business entities
ExecuteUpdate / ExecuteUpdateAsync   → forbidden for normal business mutations;
                                       allowed only after explicit review
```

Sai:

```csharp
await db.Carts.Where(c => c.Status == CartStatus.Abandoned).ExecuteDeleteAsync(ct);
```

Đúng:

```csharp
cart.MarkDeleted(currentUser.UserId, clock.UtcNow); // or db.Carts.Remove(cart)
await db.SaveChangesAsync(ct);
```

`tests/AgriSage.UnitTests/Architecture/ForbiddenPersistenceApiTests.cs` fails on any
`ExecuteDelete*` in `src/`, and on any `ExecuteUpdate*` outside the reviewed allow-list kept in that test.

---

# Team Quick Reference

```text
Controller
= HTTP only

DTO
= Data contract only

Application Service
= Use-case orchestration

Domain
= Core business rules + invariants

Infrastructure
= Database + external technical implementations

Entity
≠ API Response

Controller
≠ Business Logic

Infrastructure
≠ Business Rule Owner

No Hard Delete

No Inventory change without Stock Movement

No Debt change without Debt Transaction

No silent edit of confirmed/posted transactions

External systems always behind interfaces

Cross-module business operation
= one transaction owner
```

---

# Recommended Feature Structure

```text
AgriSage.Api/
└── Features/
    ├── Products/
    ├── Orders/
    ├── Payments/
    ├── Inventory/
    └── ...
```

```text
AgriSage.Application/
└── Features/
    └── Products/
        ├── Dtos/
        │   ├── Requests/
        │   └── Responses/
        ├── Interfaces/
        ├── Services/
        ├── Validators/
        └── Mappings/
```

```text
AgriSage.Domain/
└── Features/
    └── Products/
        ├── Entities/
        └── Enums/
```

```text
AgriSage.Infrastructure/
└── Persistence/
    └── Configurations/
        └── Products/
```

---

# Status

```text
Backend Architecture Rules: FROZEN v1
Architecture: 4 Layers
Folder Organization: Feature-based
Soft Delete: Mandatory
EF Core Migrations: Schema source of truth
Generic Repository: Not used by default
Business Transactions: One owner / one atomic boundary
```

Any exception to these rules must be discussed and agreed by the team before implementation.
