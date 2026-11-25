# Persisted sales invoices (user request, 2026-11-23)

This extension implements sales receipts, not government VAT/e-invoice integration. Existing order/payment APIs stay unchanged.

## Contract and ownership

InvoiceService owns issuance: one immutable invoice per COMPLETED order in the active store, from any order source. Pending/cancelled/partially fulfilled orders cannot be invoiced. Issuance changes no inventory, payment or debt. Repeating issuance returns the same document. Concurrent first issuances are protected by a unique order index; a 409 can be retried. Numbers are INV-yyyyMMdd-NNNN on the Vietnam issue day.

The server snapshots the store, cashier, order lines/prices, customer contact and payment summary. PDF bytes are generated before the single invoice save; failure leaves no partial invoice. Download always returns these saved bytes. Subsequent payments/returns do not rewrite an issued invoice; its balance is explicitly the balance at issuance.

| Route | Permission | Result |
| --- | --- | --- |
| POST /api/orders/{orderId}/invoice | INVOICES.CREATE | 201 first issue, 200 existing, InvoiceResponse |
| GET /api/invoices?page&pageSize&search&orderId&fromDate&toDate&minAmount&maxAmount&hasCustomerEmail&emailStatus | INVOICES.READ | PagedResult<InvoiceListItem> |
| GET /api/invoices/{id} | INVOICES.READ | InvoiceResponse with snapshot |
| GET /api/invoices/{id}/pdf | INVOICES.READ | application/pdf attachment, private/no-store |
| GET /api/invoices/{id}/emails?page&pageSize | INVOICES.READ | PagedResult<InvoiceEmailResponse> |
| POST /api/invoices/{id}/emails | INVOICES.SEND_EMAIL | {recipient, requestId}; InvoiceEmailResponse |

All routes: ADMIN/STORE_OWNER/SALES_STAFF; permissions are delegable and granted to those roles by default. Delivery staff/Farmers have no invoice endpoints in this extension. All records are scoped to the active store. Lists omit PDF/snapshot payloads.

List filters are optional and combine with AND before counting/paging. `fromDate`/`toDate` are inclusive Vietnam calendar days (UTC+7) on `issuedAt`, independently usable; minimum date is 0001-01-02, maximum is 9999-12-30, and from must not exceed to. `minAmount`/`maxAmount` are inclusive nonnegative monetary values (up to 9999999999999999.99, at most two decimal places); min must not exceed max. `hasCustomerEmail=true/false` tests the saved customer email (blank/whitespace counts as missing), independently of whether an invoice was sent to a manually entered recipient. `emailStatus` is NOT_SENT, SENDING, SENT, FAILED or UNKNOWN (case-insensitive): it describes the latest send attempt ordered by createdAt then id descending, not any historical success/failure. NOT_SENT means no attempts exist. Each list item adds `emailStatus` with the same meaning. Search still matches invoice number, order number or customer name. Invalid filter values/ranges return 400; no filters preserves existing behavior. No schema change is needed.

InvoiceResponse: id, invoiceNumber, orderId, orderNumber, customerName, customerEmail, totalAmount, issuedAt, snapshot. Snapshot v1: store {name,address,phone,email,taxCode}, cashierName, order (OrderResponse), payments (OrderPaymentSummary). All values come from persisted data; no client prices/totals/content are accepted.

Email requires a valid single recipient (max 255) and non-empty UUID requestId. It attaches the saved PDF using configured MessageDelivery SMTP/TLS. The send attempt is committed as SENDING before external I/O; success marks SENT, transport failure FAILED, interrupted sending UNKNOWN. An identical (invoiceId,requestId) returns the original attempt without sending again; reusing it for another recipient is a 409. Resending is an explicit new requestId. SMTP cannot guarantee exactly-once delivery after an ambiguous network failure; UNKNOWN is never retried automatically. Status/time/error are saved; provider details/secrets are not exposed. No real emails are sent by automated tests.

## Schema: SalesInvoices migration

sales_invoices: id uuid PK; store_id/order_id/created_by uuid FK; invoice_number varchar(50); order_number varchar(50); customer_name varchar(150); customer_email varchar(255) nullable; total_amount numeric(18,2); issued_at timestamptz; snapshot_json jsonb; pdf_content bytea; created_at/updated_at timestamptz. UNIQUE(order_id), UNIQUE(store_id,invoice_number), index(store_id,issued_at desc). Checks: nonnegative total, nonempty PDF, snapshot is a JSON object. No update/delete API.

invoice_emails: id uuid PK; invoice_id/created_by uuid FK; request_id uuid; recipient varchar(255); status varchar(20) SENDING/SENT/FAILED/UNKNOWN; sent_at nullable timestamptz; error varchar(500) nullable; created_at/updated_at timestamptz. UNIQUE(invoice_id,request_id), index(invoice_id,created_at desc). SENT requires sent_at; other statuses have no sent_at. All business FKs Restrict. No destructive migration operations or startup auto-migration.

PDF implementation: QuestPDF 2026.9.2, Community license selected for this capstone/learning project. Reassess license eligibility before commercial deployment: https://www.questpdf.com/license/guide.html . Bundled Lato covers Vietnamese; generation checks glyph coverage. Verify representative Vietnamese text and long multi-page documents by rendering the actual PDFs.

## Deployment and verification

Apply the reviewed `20261123071131_SalesInvoices` migration with the existing EF deployment workflow, then restart the API. Never auto-migrate/seed on normal startup. SMTP reuses `MessageDelivery:SmtpHost`, `SmtpPort` (587 by default), `FromEmail`, `FromName`, `SmtpUsername`, `SmtpPassword`; credentials belong in environment variables/User Secrets. Missing SMTP returns a clear 503 without claiming an email was sent.

Local development: stop the old API in its terminal with Ctrl+C, then run from the backend repository:

```powershell
dotnet run --project src/AgriSage.Api --launch-profile http
```

Configure SMTP using the real provider's settings (replace placeholders; do not commit credentials):

```powershell
dotnet user-secrets set "MessageDelivery:SmtpHost" "<smtp-host>" --project src/AgriSage.Api
dotnet user-secrets set "MessageDelivery:SmtpPort" "587" --project src/AgriSage.Api
dotnet user-secrets set "MessageDelivery:FromEmail" "<sender-address>" --project src/AgriSage.Api
dotnet user-secrets set "MessageDelivery:SmtpUsername" "<smtp-username>" --project src/AgriSage.Api
dotnet user-secrets set "MessageDelivery:SmtpPassword" "<smtp-password-or-app-password>" --project src/AgriSage.Api
```

Management: /agent/invoices and /sales/invoices. Quick counter sales request invoice issuance after the sale has succeeded, including the no-print choice. A failed invoice request is shown separately and may be retried from the completed order without selling again. Opening an existing invoice never rewrites it; staff may also issue invoices for completed online orders. The history screen reads stored invoices, not the live order list. Preview/print and PDF download both use the saved snapshot. Email is explicit and never automatic after checkout.

Tests: InvoiceTests (domain/validation), InvoiceMigrationTests (schema review), InvoicePdfTests (real renderer and long tables), InvoicesHttpTests (authentication/role/validation), InvoiceDatabaseTests (isolated PostgreSQL persistence, concurrency, email outcomes/idempotency), scripts/test-counter-invoices.mjs (mocked-browser end-to-end). Set AGRISAGE_CREDIT_TEST_CONNECTION_STRING only to a dedicated test database; INVOICE_PDF_TEST_OUTPUT optionally writes renderer QA artifacts.
