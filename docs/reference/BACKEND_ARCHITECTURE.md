# AgriSage Backend Architecture v1

> **System:** AgriSage – An Intelligent Advisory System for Agriculture  
> **Backend stack:** ASP.NET Core, Entity Framework Core, PostgreSQL/Supabase, JWT, payOS, Python FastAPI AI service  
> **Architecture style:** 4-layer architecture with feature-based folder organization  
> **Purpose:** Describe how the backend solution is structured, how components depend on one another, and where each type of code belongs.  
> **Companion document:** `AgriSage_Backend_Architecture_Coding_Rules_v1.md`

---

# 1. Architecture Goals

The backend architecture is designed to:

```text
Keep business logic independent from HTTP and infrastructure
Make feature ownership clear for a 5-member development team
Keep Controllers thin
Make business use cases testable
Allow EF Core/PostgreSQL to change without affecting Domain rules
Hide payOS, AI FastAPI, storage, and other providers behind abstractions
Support transaction-heavy flows such as Inventory, Delivery, Credit, and Debt
Scale to the current 67-table database without creating giant shared folders
```

The solution combines:

```text
4 architectural layers
+
feature-based source organization
```

The project is **not** organized as one global `Controllers/`, `Services/`, `DTOs/` folder containing every feature.

---

# 2. Solution Structure

```text
AgriSage.sln

src/
├── AgriSage.Api/
├── AgriSage.Application/
├── AgriSage.Domain/
└── AgriSage.Infrastructure/

tests/
├── AgriSage.UnitTests/
└── AgriSage.IntegrationTests/
```

Each project has one primary responsibility.

```text
AgriSage.Api
→ HTTP/API boundary

AgriSage.Application
→ use cases and application orchestration

AgriSage.Domain
→ core business model and invariants

AgriSage.Infrastructure
→ EF Core, PostgreSQL, authentication implementation,
  external providers and technical services
```

---

# 3. Dependency Direction

The dependency rule is:

```text
          ┌──────────────────┐
          │   AgriSage.Api   │
          └────────┬─────────┘
                   │
                   ▼
       ┌────────────────────────┐
       │ AgriSage.Application   │
       └──────────┬─────────────┘
                  │
                  ▼
         ┌──────────────────┐
         │ AgriSage.Domain  │
         └──────────────────┘

AgriSage.Infrastructure
        │
        ├────→ AgriSage.Application
        └────→ AgriSage.Domain
```

Project references:

```text
AgriSage.Domain
→ no project references

AgriSage.Application
→ AgriSage.Domain

AgriSage.Infrastructure
→ AgriSage.Application
→ AgriSage.Domain

AgriSage.Api
→ AgriSage.Application
→ AgriSage.Infrastructure
```

Important principle:

```text
Business code depends on abstractions.
Infrastructure implements those abstractions.
```

Example:

```text
Application:
IPaymentGateway

Infrastructure:
PayOsPaymentGateway : IPaymentGateway
```

---

# 4. Feature-based Organization

Inside the architectural layers, code is grouped by **business feature**.

Main AgriSage feature groups:

```text
Auth
Users
Stores
Customers

Products
Pricing

Suppliers
GoodsReceipts
Inventory

Orders
Payments

Deliveries

Credit
Debt

Returns

Diagnosis

Articles
Notifications

Reports
```

A feature is a business area, not necessarily one database table.

For example:

```text
Inventory
```

contains logic around:

```text
Inventory Lots
Lot Balances
Stock Movements
Stocktakes
Inventory Reservations
FEFO
Weighted Average Cost
```

rather than creating six independent top-level features.

---

# 5. AgriSage.Api

## 5.1 Responsibility

`AgriSage.Api` is the HTTP boundary.

It contains:

```text
Controllers
Authentication/Authorization setup
Middleware
Filters
Swagger/OpenAPI configuration
HTTP-specific extensions
Program.cs
```

It does **not** own business logic.

---

## 5.2 Recommended structure

```text
AgriSage.Api/
│
├── Features/
│   ├── Auth/
│   │   └── AuthController.cs
│   │
│   ├── Users/
│   │   └── UsersController.cs
│   │
│   ├── Products/
│   │   └── ProductsController.cs
│   │
│   ├── Pricing/
│   │   └── PricingController.cs
│   │
│   ├── Suppliers/
│   │   └── SuppliersController.cs
│   │
│   ├── GoodsReceipts/
│   │   └── GoodsReceiptsController.cs
│   │
│   ├── Inventory/
│   │   └── InventoryController.cs
│   │
│   ├── Orders/
│   │   └── OrdersController.cs
│   │
│   ├── Payments/
│   │   └── PaymentsController.cs
│   │
│   ├── Deliveries/
│   │   └── DeliveriesController.cs
│   │
│   ├── Credit/
│   │   └── CreditController.cs
│   │
│   ├── Debt/
│   │   └── DebtController.cs
│   │
│   ├── Returns/
│   │   └── ReturnsController.cs
│   │
│   ├── Diagnosis/
│   │   └── DiagnosisController.cs
│   │
│   ├── Articles/
│   │   └── ArticlesController.cs
│   │
│   ├── Notifications/
│   │   └── NotificationsController.cs
│   │
│   └── Reports/
│       └── ReportsController.cs
│
├── Middleware/
│   ├── ExceptionHandlingMiddleware.cs
│   └── RequestLoggingMiddleware.cs
│
├── Filters/
│
├── Extensions/
│   ├── AuthenticationExtensions.cs
│   ├── AuthorizationExtensions.cs
│   ├── SwaggerExtensions.cs
│   └── MiddlewareExtensions.cs
│
├── Program.cs
├── appsettings.json
└── appsettings.Development.json
```

---

## 5.3 Controller flow

```text
HTTP Request
↓
Controller
↓
Application Service
↓
Response DTO
↓
HTTP Response
```

Example:

```text
POST /api/products
↓
ProductsController
↓
IProductService.CreateAsync()
↓
ProductResponse
↓
201 Created
```

The Controller never directly accesses:

```text
AgriSageDbContext
payOS SDK
AI HTTP client
Object Storage SDK
```

---

# 6. AgriSage.Application

## 6.1 Responsibility

`AgriSage.Application` contains the application's **use cases**.

It is responsible for:

```text
Use-case orchestration
Request/Response DTOs
Business workflow validation
Service interfaces
Service implementations
External-system abstractions
Application exceptions
Pagination/result models
Transaction orchestration
Mapping
Validation
```

This is where most backend business workflows are coordinated.

---

## 6.2 Recommended structure

```text
AgriSage.Application/
│
├── Common/
│   ├── Interfaces/
│   │   ├── IAgriSageDbContext.cs
│   │   ├── ICurrentUserService.cs
│   │   ├── IDateTimeProvider.cs
│   │   ├── IFileStorageService.cs
│   │   ├── IPaymentGateway.cs
│   │   └── IAiDiagnosisClient.cs
│   │
│   ├── Models/
│   │   ├── PagedResult.cs
│   │   ├── PaginationRequest.cs
│   │   └── ErrorResponse.cs
│   │
│   └── Exceptions/
│       ├── NotFoundException.cs
│       ├── ValidationException.cs
│       ├── ConflictException.cs
│       ├── ForbiddenException.cs
│       └── BusinessRuleException.cs
│
├── Features/
│   ├── Auth/
│   ├── Users/
│   ├── Stores/
│   ├── Customers/
│   ├── Products/
│   ├── Pricing/
│   ├── Suppliers/
│   ├── GoodsReceipts/
│   ├── Inventory/
│   ├── Orders/
│   ├── Payments/
│   ├── Deliveries/
│   ├── Credit/
│   ├── Debt/
│   ├── Returns/
│   ├── Diagnosis/
│   ├── Articles/
│   ├── Notifications/
│   └── Reports/
│
└── DependencyInjection.cs
```

---

# 7. Structure of One Application Feature

Example: `Products`.

```text
Features/
└── Products/
    ├── Dtos/
    │   ├── Requests/
    │   │   ├── CreateProductRequest.cs
    │   │   ├── UpdateProductRequest.cs
    │   │   └── ProductListRequest.cs
    │   │
    │   └── Responses/
    │       ├── ProductResponse.cs
    │       └── ProductDetailResponse.cs
    │
    ├── Interfaces/
    │   └── IProductService.cs
    │
    ├── Services/
    │   └── ProductService.cs
    │
    ├── Validators/
    │   ├── CreateProductValidator.cs
    │   └── UpdateProductValidator.cs
    │
    └── Mappings/
        └── ProductMappings.cs
```

A larger feature may add internal helper services when needed:

```text
Orders/
├── Services/
│   ├── OrderService.cs
│   ├── OrderPricingService.cs
│   └── OrderFulfillmentService.cs
```

but only when the feature actually becomes complex enough.

---

# 8. Application Service Flow

Example: create Product.

```text
ProductsController
↓
CreateProductRequest
↓
CreateProductValidator
↓
IProductService
↓
ProductService
↓
Product Domain Entity
↓
IAgriSageDbContext
↓
SaveChangesAsync
↓
ProductResponse
```

Example: confirm Delivery.

```text
DeliveriesController
↓
DeliveryService
↓
Validate Delivery
↓
Validate actual Lots
↓
Inventory Reservation
↓
Weighted Average Cost
↓
SALE Stock Movement
↓
Payment Prepayment Consumption
↓
Credit Reservation
↓
Debt Entry
↓
Debt Transaction
↓
Update Delivery + Order
↓
Commit Transaction
```

The cross-module operation has one transaction owner.

---

# 9. AgriSage.Domain

## 9.1 Responsibility

`AgriSage.Domain` contains the core business model.

It contains:

```text
Entities
Enums
Domain exceptions
Entity state-transition methods
Business invariants
Optional Value Objects / Domain Services
```

Domain must not know:

```text
HTTP
Controllers
DTOs
EF Core implementation
PostgreSQL
Supabase
payOS
FastAPI
Object Storage
```

---

## 9.2 Recommended structure

```text
AgriSage.Domain/
│
├── Common/
│   ├── BaseEntity.cs
│   ├── AuditableEntity.cs
│   ├── SoftDeletableEntity.cs
│   ├── SoftDeletableChildEntity.cs
│   ├── IHasConcurrencyVersion.cs
│   ├── Guard.cs
│   ├── CostRounding.cs
│   └── Exceptions/
│       └── DomainException.cs
│
├── Features/
│   ├── Identity/
│   │   ├── Entities/
│   │   └── Enums/
│   │
│   ├── Stores/
│   │   ├── Entities/
│   │   └── Enums/
│   │
│   ├── Customers/
│   │   ├── Entities/
│   │   └── Enums/
│   │
│   ├── Products/
│   │   ├── Entities/
│   │   └── Enums/
│   │
│   ├── Pricing/
│   ├── Suppliers/
│   ├── GoodsReceipts/
│   ├── Inventory/
│   ├── Orders/
│   ├── Payments/
│   ├── Deliveries/
│   ├── Credit/
│   ├── Debt/
│   ├── Returns/
│   ├── Diagnosis/
│   ├── Content/
│   ├── Notifications/
│   └── Audit/
│
└── Constants/
```

`SoftDeletableChildEntity` is the base type for entities that only exist as part of
another entity's aggregate (e.g. a Goods Receipt Item, an Order Item, a Stock Movement
Item). Its soft-delete hook always throws, so it can only be removed through its
aggregate root's own method, never directly. `IHasConcurrencyVersion` marks entities
backed by a DB `version bigint` column (see database design §0.8); Infrastructure maps
it to an EF concurrency token and increments it on save (AGRI-13, database design §35.15). `CostRounding` and `Guard`'s money/unit-cost
helpers (`Guard.Money`, `Guard.UnitCost`, `Guard.NonNegativeMoney`, `Guard.PositiveMoney`)
are the single implementation of coding rule #61 and must be reused rather than
re-implemented per feature.

A cross-entity value object used only within one feature (e.g. a Stock Movement's
`LotBalanceChange`, an Order's `PriceOverride`/`DeliveryAddress`) is a `record` placed
directly under that feature's folder, next to `Entities/` and `Enums/`, not inside either
of them.

---

# 10. Domain Entity Organization

Example:

```text
Features/
└── Inventory/
    ├── Entities/
    │   ├── InventoryLot.cs
    │   ├── InventoryLotBalance.cs
    │   ├── StockMovement.cs
    │   ├── StockMovementItem.cs
    │   ├── Stocktake.cs
    │   ├── StocktakeItem.cs
    │   ├── InventoryReservation.cs
    │   └── InventoryReservationItem.cs
    │
    └── Enums/
        ├── InventoryLotStatus.cs
        ├── StockMovementType.cs
        ├── StockMovementStatus.cs
        └── InventoryReservationStatus.cs
```

Domain methods protect important state transitions.

Example conceptual API:

```text
InventoryReservation.Consume(...)
InventoryReservation.Release(...)

Order.Confirm(...)
Order.MarkPartiallyFulfilled(...)
Order.MarkCompleted(...)
Order.Cancel(...)

Delivery.MarkOutForDelivery(...)
Delivery.MarkDelivered(...)

CreditReservation.Consume(...)
CreditReservation.Release(...)
```

---

# 11. AgriSage.Infrastructure

## 11.1 Responsibility

Infrastructure contains technical implementations.

It owns:

```text
EF Core DbContext
Entity configurations
PostgreSQL implementation
Database migrations
Seed data
JWT implementation
Password hashing
payOS integration
AI FastAPI integration
Object storage implementation
Technical clocks/providers
Persistence interceptors
```

Infrastructure does not own business policies.

---

## 11.2 Recommended structure

```text
AgriSage.Infrastructure/
│
├── Persistence/
│   ├── AgriSageDbContext.cs
│   │
│   ├── Configurations/
│   │   ├── Identity/
│   │   ├── Stores/
│   │   ├── Customers/
│   │   ├── Products/
│   │   ├── Pricing/
│   │   ├── Suppliers/
│   │   ├── GoodsReceipts/
│   │   ├── Inventory/
│   │   ├── Orders/
│   │   ├── Payments/
│   │   ├── Deliveries/
│   │   ├── Credit/
│   │   ├── Debt/
│   │   ├── Returns/
│   │   ├── Diagnosis/
│   │   ├── Content/
│   │   ├── Notifications/
│   │   └── Audit/
│   │
│   ├── Interceptors/
│   │   ├── EntityStateInterceptor.cs        (shared base)
│   │   ├── SoftDeleteInterceptor.cs         (1st)
│   │   ├── AuditableEntityInterceptor.cs    (2nd, + audit_logs append-only)
│   │   └── ConcurrencyVersionInterceptor.cs (3rd)
│   │
│   ├── Seed/
│   │   ├── RoleSeeder.cs
│   │   ├── UnitSeeder.cs
│   │   ├── DiseaseSeeder.cs
│   │   └── StoreSeeder.cs
│   │
│   └── Migrations/
│
├── Authentication/
│   ├── JwtTokenService.cs
│   ├── PasswordHasher.cs
│   └── CurrentUserService.cs
│
├── Payments/
│   └── PayOs/
│       ├── PayOsPaymentGateway.cs
│       ├── PayOsOptions.cs
│       └── PayOsSignatureVerifier.cs
│
├── Ai/
│   └── FastApi/
│       ├── AiDiagnosisClient.cs
│       └── AiOptions.cs
│
├── Storage/
│   ├── FileStorageService.cs
│   └── StorageOptions.cs
│
├── Services/
│   └── DateTimeProvider.cs
│
└── DependencyInjection.cs
```

---

# 12. EF Core Configuration Organization

Each database entity should have a dedicated configuration when meaningful.

Example:

```text
Persistence/
└── Configurations/
    ├── Products/
    │   ├── ProductConfiguration.cs
    │   ├── ProductPackagingConfiguration.cs
    │   ├── StoreProductConfiguration.cs
    │   └── ProductReviewConfiguration.cs
    │
    ├── Inventory/
    │   ├── InventoryLotConfiguration.cs
    │   ├── InventoryLotBalanceConfiguration.cs
    │   ├── StockMovementConfiguration.cs
    │   ├── StockMovementItemConfiguration.cs
    │   └── ...
    │
    └── Debt/
        ├── DebtAccountConfiguration.cs
        ├── DebtEntryConfiguration.cs
        └── DebtTransactionConfiguration.cs
```

Configuration files define:

```text
Table names
Keys
Column types
Max lengths
Required/nullable fields
Relationships
Delete behavior
Indexes
Unique indexes
Check constraints
Query filters
Concurrency settings
```

Business workflow logic does not belong in EF Configuration.

---

# 13. DbContext Abstraction

Application should depend on an abstraction:

```text
IAgriSageDbContext
```

Infrastructure provides:

```text
AgriSageDbContext
```

Conceptual dependency:

```text
Application Service
↓
IAgriSageDbContext
↑
AgriSageDbContext
↓
PostgreSQL
```

This prevents the Application project from referencing Infrastructure.

`IAgriSageDbContext` exposes `DbSet<T>` for every entity, `SaveChangesAsync` and
`BeginTransactionAsync` (for the single transaction owner of a use case). For this,
Application references the `Microsoft.EntityFrameworkCore` abstractions package only —
never `Npgsql`, `Microsoft.EntityFrameworkCore.Relational` or Infrastructure. Mappings,
provider configuration and interceptors stay in Infrastructure.

---

# 14. Repository Strategy

AgriSage does **not** use a generic repository by default.

Avoid:

```text
IGenericRepository<T>
GenericRepository<T>
```

that merely wrap:

```text
DbSet.Add
DbSet.Update
DbSet.Remove
DbSet.ToList
```

Normal database access uses:

```text
IAgriSageDbContext
```

Specialized repository/query abstractions may be introduced only when they solve a real problem.

Examples:

```text
IInventoryQueryRepository
IDebtLedgerRepository
```

---

# 15. Dependency Injection

Each main project exposes its registration method.

Application:

```csharp
services.AddApplication();
```

Infrastructure:

```csharp
services.AddInfrastructure(configuration);
```

API `Program.cs` stays small.

Conceptually:

```text
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthentication(...);
builder.Services.AddAuthorization(...);
```

Concrete implementations are registered in Infrastructure.

Example:

```text
IPaymentGateway
→ PayOsPaymentGateway

IAiDiagnosisClient
→ AiDiagnosisClient

IFileStorageService
→ FileStorageService

IDateTimeProvider
→ DateTimeProvider
```

---

# 16. External Integration Architecture

## payOS

```text
PaymentsController
↓
IPaymentService
↓
PaymentService
↓
IPaymentGateway
↑
PayOsPaymentGateway
↓
payOS
```

payOS webhook:

```text
payOS
↓
PaymentsController/Webhook endpoint
↓
PaymentService
↓
IPaymentGateway signature verification
↓
Payment + Allocation + financial posting
```

Webhook is the source of truth for online payment confirmation.

---

## AI FastAPI

```text
DiagnosisController
↓
DiagnosisService
↓
IAiDiagnosisClient
↑
AiDiagnosisClient
↓
Python FastAPI
↓
PyTorch model
```

Application never directly creates an `HttpClient` call to FastAPI.

---

## Object Storage

```text
Application Service
↓
IFileStorageService
↑
FileStorageService
↓
Object Storage Provider
```

Database stores:

```text
object key
URL/reference
metadata
```

rather than file bytes.

---

# 17. Authentication Architecture

Authentication uses:

```text
JWT
```

Conceptual flow:

```text
Login Request
↓
AuthController
↓
AuthService
↓
User validation
↓
Password verification
↓
JWT token generation
↓
Login Response
```

JWT-related implementation belongs in Infrastructure.

Application uses:

```text
ICurrentUserService
```

to access current user identity.

Business services must not parse raw JWT manually.

---

# 18. Authorization Architecture

Authorization is applied at two levels.

## HTTP-level authorization

Examples:

```text
[Authorize]
Role/Policy requirements
```

## Business-level authorization

Examples:

```text
Farmer may access only own Order

Delivery Staff may update only assigned Delivery

Sales Staff/Store Owner require can_review_ai
for AI Human Review
```

Business ownership checks remain inside Application services.

---

# 19. Transaction Architecture

High-risk workflows must execute atomically.

## Goods Receipt Confirmation

```text
Goods Receipt DRAFT
↓
Resolve/Create Inventory Lots
↓
Create STOCK_IN Movement
↓
Update Lot Balances
↓
Weighted Average Cost
↓
Receipt CONFIRMED
↓
COMMIT
```

---

## Delivery Success

```text
Delivery Attempt
↓
Validate actual Lots
↓
SALE Stock Movement
↓
Update Lot Balance
↓
Consume Inventory Reservation
↓
Update Delivery quantities
↓
Update Order quantities
↓
Apply Prepayment
↓
Consume Credit Reservation
↓
Create Debt Entry if needed
↓
Create Debt Transaction
↓
COMMIT
```

One Application use case owns the transaction.

---

## Debt Payment

```text
Payment confirmed
↓
Payment Allocations
↓
Debt Transactions
↓
Debt Entry outstanding balances
↓
Debt Account current balance
↓
COMMIT
```

---

## Return

```text
Return approved
↓
Inspect returned goods
↓
RETURN_IN if resellable
↓
Debt reduction for unpaid portion
↓
Refund for already-paid portion
↓
COMMIT
```

---

# 20. Persistence Architecture

Database:

```text
PostgreSQL on Supabase
```

ORM:

```text
Entity Framework Core + Npgsql
```

Schema source of truth after coding begins:

```text
EF Core Migrations
```

Workflow:

```text
Domain Entity
+
EF Configuration
↓
EF Migration
↓
PostgreSQL/Supabase
```

DBeaver is used for:

```text
Database inspection
Queries
Debugging
Data verification
```

not manual schema evolution after Initial Migration.

---

# 21. Soft Delete Architecture

All business DELETE APIs use Soft Delete.

Common fields:

```text
DeletedAt
DeletedBy
```

Master data may additionally use:

```text
IsActive
Status
```

Transaction records use business states such as:

```text
Cancelled
Reversed
Voided
```

EF Core Global Query Filters exclude soft-deleted records by default.

---

# 22. Database Design Source

Backend persistence must follow:

```text
AgriSage_Detailed_Database_Design_v4_Migration_Candidate.md
```

That document is authoritative for:

```text
67 tables
fields
PK/FK
relationships
constraints
indexes
statuses
soft-delete fields
weighted-average inventory cost
credit/debt relationships
return/refund relationships
AI relationships
```

The code architecture does not replace the database design.

---

# 23. Testing Architecture

```text
tests/
├── AgriSage.UnitTests/
└── AgriSage.IntegrationTests/
```

## Unit Tests

Recommended organization:

```text
AgriSage.UnitTests/
└── Features/
    ├── Inventory/
    ├── Orders/
    ├── Credit/
    ├── Debt/
    └── Returns/
```

Target:

```text
Domain invariant
Calculation
State transition
Pure application logic
```

---

## Integration Tests

Recommended organization:

```text
AgriSage.IntegrationTests/
├── Infrastructure/
│   └── Persistence/
└── Features/
    ├── GoodsReceipts/
    ├── Inventory/
    ├── Payments/
    ├── Deliveries/
    ├── Credit/
    ├── Debt/
    └── Returns/
```

Important scenarios:

```text
Inventory reservation concurrency
Weighted Average Cost
Goods Receipt atomic posting
payOS webhook idempotency
Credit overcommit prevention
Delivery → Stock Out → Debt
Partial delivery
Partial debt repayment
Return → Debt reduction / Refund
Soft Delete query filters
PostgreSQL unique/check constraints
```

---

# 24. Recommended Complete Source Tree

```text
AgriSage/
│
├── AgriSage.sln
│
├── README.md
│
├── docs/
│   ├── BACKEND_ARCHITECTURE.md
│   ├── BACKEND_CODING_RULES.md
│   └── DATABASE_DESIGN.md
│
├── src/
│   │
│   ├── AgriSage.Api/
│   │   ├── Features/
│   │   │   ├── Auth/
│   │   │   ├── Users/
│   │   │   ├── Products/
│   │   │   ├── Pricing/
│   │   │   ├── Suppliers/
│   │   │   ├── GoodsReceipts/
│   │   │   ├── Inventory/
│   │   │   ├── Orders/
│   │   │   ├── Payments/
│   │   │   ├── Deliveries/
│   │   │   ├── Credit/
│   │   │   ├── Debt/
│   │   │   ├── Returns/
│   │   │   ├── Diagnosis/
│   │   │   ├── Articles/
│   │   │   ├── Notifications/
│   │   │   └── Reports/
│   │   ├── Middleware/
│   │   ├── Filters/
│   │   ├── Extensions/
│   │   └── Program.cs
│   │
│   ├── AgriSage.Application/
│   │   ├── Common/
│   │   │   ├── Interfaces/
│   │   │   ├── Models/
│   │   │   └── Exceptions/
│   │   ├── Features/
│   │   │   ├── Auth/
│   │   │   ├── Users/
│   │   │   ├── Stores/
│   │   │   ├── Customers/
│   │   │   ├── Products/
│   │   │   ├── Pricing/
│   │   │   ├── Suppliers/
│   │   │   ├── GoodsReceipts/
│   │   │   ├── Inventory/
│   │   │   ├── Orders/
│   │   │   ├── Payments/
│   │   │   ├── Deliveries/
│   │   │   ├── Credit/
│   │   │   ├── Debt/
│   │   │   ├── Returns/
│   │   │   ├── Diagnosis/
│   │   │   ├── Articles/
│   │   │   ├── Notifications/
│   │   │   └── Reports/
│   │   └── DependencyInjection.cs
│   │
│   ├── AgriSage.Domain/
│   │   ├── Common/
│   │   ├── Features/
│   │   │   ├── Identity/
│   │   │   ├── Stores/
│   │   │   ├── Customers/
│   │   │   ├── Products/
│   │   │   ├── Pricing/
│   │   │   ├── Suppliers/
│   │   │   ├── Inventory/
│   │   │   ├── Orders/
│   │   │   ├── Payments/
│   │   │   ├── Deliveries/
│   │   │   ├── Credit/
│   │   │   ├── Debt/
│   │   │   ├── Returns/
│   │   │   ├── Diagnosis/
│   │   │   └── Content/
│   │   └── Constants/
│   │
│   └── AgriSage.Infrastructure/
│       ├── Persistence/
│       │   ├── AgriSageDbContext.cs
│       │   ├── Configurations/
│       │   ├── Interceptors/
│       │   ├── Seed/
│       │   └── Migrations/
│       ├── Authentication/
│       ├── Payments/
│       │   └── PayOs/
│       ├── Ai/
│       │   └── FastApi/
│       ├── Storage/
│       ├── Services/
│       └── DependencyInjection.cs
│
└── tests/
    ├── AgriSage.UnitTests/
    └── AgriSage.IntegrationTests/
```

---

# 25. Example: Product Feature Across All Layers

```text
AgriSage.Api
└── Features/Products/
    └── ProductsController.cs
```

```text
AgriSage.Application
└── Features/Products/
    ├── Dtos/
    ├── Interfaces/IProductService.cs
    ├── Services/ProductService.cs
    ├── Validators/
    └── Mappings/
```

```text
AgriSage.Domain
└── Features/Products/
    ├── Entities/
    │   ├── Product.cs
    │   ├── ProductPackaging.cs
    │   └── StoreProduct.cs
    └── Enums/
```

```text
AgriSage.Infrastructure
└── Persistence/Configurations/Products/
    ├── ProductConfiguration.cs
    ├── ProductPackagingConfiguration.cs
    └── StoreProductConfiguration.cs
```

Runtime flow:

```text
ProductsController
↓
IProductService
↓
ProductService
↓
Product Entity
↓
IAgriSageDbContext
↑
AgriSageDbContext
↓
PostgreSQL
```

---

# 26. Example: Payment Feature Across All Layers

```text
AgriSage.Api
└── Features/Payments/
    └── PaymentsController.cs
```

```text
AgriSage.Application
└── Features/Payments/
    ├── Dtos/
    ├── Interfaces/
    │   └── IPaymentService.cs
    └── Services/
        └── PaymentService.cs
```

Application common abstraction:

```text
Common/Interfaces/IPaymentGateway.cs
```

Infrastructure:

```text
Payments/PayOs/
├── PayOsPaymentGateway.cs
├── PayOsOptions.cs
└── PayOsSignatureVerifier.cs
```

Flow:

```text
PaymentsController
↓
IPaymentService
↓
PaymentService
├──→ IAgriSageDbContext
└──→ IPaymentGateway
          ↑
    PayOsPaymentGateway
          ↓
        payOS
```

---

# 27. Example: AI Diagnosis Feature

```text
DiagnosisController
↓
IDiagnosisService
↓
DiagnosisService
├──→ IAgriSageDbContext
├──→ IFileStorageService
└──→ IAiDiagnosisClient
          ↑
      AiDiagnosisClient
          ↓
    Python FastAPI / PyTorch
```

Human Review remains an AgriSage business workflow and is stored in PostgreSQL.

AI inference does not directly become the final verified diagnosis without authorized review.

---

# 28. Reports Architecture

Initial reporting does not require separate report tables.

`Reports` is an Application/API feature that queries transactional data.

Examples:

```text
Sales Report
Inventory Report
Near Expiry Report
Supplier Receiving Report
Credit Exposure Report
Debt Aging Report
Delivery Report
Gross Profit Report
```

Architecture:

```text
ReportsController
↓
IReportService
↓
ReportService / specialized query services
↓
IAgriSageDbContext
↓
Projection / aggregation queries
```

Read-heavy report queries should:

```text
use AsNoTracking
project only necessary fields
avoid loading full entity graphs
```

Materialized views may be considered later only if performance requires them.

---

# 29. What Each Layer Must Know

| Layer | Knows Domain | Knows DTO | Knows EF Core Implementation | Knows HTTP | Knows payOS/FastAPI concrete implementation |
|---|---:|---:|---:|---:|---:|
| Domain | Yes | No | No | No | No |
| Application | Yes | Yes | No | No | No |
| Infrastructure | Yes | Application abstractions | Yes | Technical only | Yes |
| API | Through Application | Yes | DI registration only | Yes | No |

---

# 30. Architecture vs Coding Rules

This document answers:

```text
What projects exist?
Where does each kind of code live?
How do layers depend on each other?
How does a request flow through the backend?
How are EF Core, payOS, AI and storage integrated?
How are features organized?
```

The companion coding-rules document answers:

```text
What developers MUST do?
What developers MUST NOT do?
Where is business logic allowed?
How must transactions be handled?
What are the soft-delete rules?
How should DTOs, validation, logging and testing be written?
```

Use both documents together.

---

# 31. Official Backend Documentation Baseline

Before implementation, the backend team uses three primary documents:

```text
1. AgriSage_Detailed_Database_Design_v4_Migration_Candidate.md
   → Database schema and relationships

2. AgriSage_Backend_Architecture_v1.md
   → Backend architecture and source structure

3. AgriSage_Backend_Architecture_Coding_Rules_v1.md
   → Mandatory implementation rules
```

---

# 32. Architecture Status

```text
Architecture: FROZEN v1

Style:
4-layer architecture
+
feature-based organization

Projects:
AgriSage.Api
AgriSage.Application
AgriSage.Domain
AgriSage.Infrastructure

Persistence:
EF Core + Npgsql + PostgreSQL/Supabase

External integrations:
payOS
Python FastAPI AI
Object Storage

Testing:
Unit Tests
Integration Tests
```

Any major architectural change should be discussed by the backend team before implementation so that all features follow the same structure.
