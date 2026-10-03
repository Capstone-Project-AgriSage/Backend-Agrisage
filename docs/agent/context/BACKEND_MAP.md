# Backend Map — Agent Context

## Four Layers

```text
AgriSage.Api            → HTTP boundary
AgriSage.Application    → use-case orchestration and contracts
AgriSage.Domain         → entities, enums, invariants
AgriSage.Infrastructure → EF/provider technical implementation
```

## Dependency Direction

```text
Domain
↑
Application
↑
Api

Infrastructure → Application + Domain
```

## Feature Groups

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

## Typical Feature Layout

```text
AgriSage.Api/
└── Features/Products/
    └── ProductsController.cs

AgriSage.Application/
└── Features/Products/
    ├── Dtos/
    │   ├── Requests/
    │   └── Responses/
    ├── Interfaces/
    ├── Services/
    ├── Validators/
    └── Mappings/

AgriSage.Domain/
└── Features/Products/
    ├── Entities/
    └── Enums/

AgriSage.Infrastructure/
└── Persistence/Configurations/Products/
```

## Typical Request Flow

```text
HTTP
→ Controller
→ Request DTO / Validator
→ Application Service
→ Domain + IAgriSageDbContext
→ Infrastructure DbContext/provider implementation
→ Response DTO
→ Controller
→ HTTP
```

## External Abstractions

```text
IPaymentGateway    ← PayOsPaymentGateway
IAiDiagnosisClient ← FastAPI client
IFileStorageService← storage provider implementation
IReceiptSpreadsheet← ClosedXmlReceiptSpreadsheet (goods receipt Excel template/import)
IDateTimeProvider  ← system clock implementation
ICurrentUserService← JWT/current request implementation
```

For exact structure and examples, use `docs/reference/BACKEND_ARCHITECTURE.md`.
