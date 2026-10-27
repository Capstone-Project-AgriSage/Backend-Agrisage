# AgriSage — Mermaid swimlane diagrams

16 luồng nghiệp vụ chính theo đặc tả hiện có; đây là mô hình nghiệp vụ mục tiêu, không phải cam kết mọi API đã được triển khai. Các màn hình CRUD danh mục, tài khoản, phân quyền, nội dung và báo cáo không được tách thành luồng riêng trong bộ này.

## Cách dùng

- Bản này dùng `swimlane-beta TB`: mỗi `subgraph` là một cột trách nhiệm, tiến trình đi từ trên xuống như ảnh tham chiếu. Yêu cầu trình dựng hỗ trợ swimlane (Mermaid 11.16.0 trở lên theo tài liệu chính thức).
- Từng mã bên dưới cũng có trong thư mục [native](native/). Chép nội dung một file `.mmd` vào trình dựng Mermaid; không chép dấu hàng rào Markdown.
- Nếu gặp `UnknownDiagramError`, dùng [bản flowchart tương thích](AGRISAGE_SWIMLANES_FLOWCHART.md) hoặc file trong [compatible](compatible/). Bản tương thích dùng các khung `subgraph`; trình tự vẫn đúng nhưng không bảo đảm các cột thẳng hàng như swimlane.
- Giao diện dùng nền tối và action bo góc, decision hình thoi, start/end hình tròn. Mermaid tự bố trí nên khoảng cách và đường nối không trùng từng pixel với ảnh.
- `Agent / AI Reviewer` là Store Member đang hoạt động có `can_review_ai` và quyền endpoint phù hợp; không phải một primary role riêng.
- `Sales Staff` chỉ các thao tác vận hành; `Store Owner / Admin` chỉ các thao tác quản lý theo quyền tương ứng. Quyền động và ownership vẫn áp dụng, tên lane không thay thế kiểm tra phân quyền.
- Nút ghi “one transaction” gom các thay đổi dữ liệu cần atomic; các sơ đồ là mức nghiệp vụ, không mô tả thứ tự câu SQL. Mỗi nhánh lỗi không ghi thành công một phần.

Tài liệu Mermaid: [Swimlanes syntax](https://mermaid.js.org/syntax/swimlanes.html).

## Danh sách luồng

| Mã | Luồng | Lane từ trái sang phải |
|---|---|---|
| WF-01 | AI Disease Diagnosis & Human Verification | Farmer · Backend System · AI System · Agent / AI Reviewer |
| WF-02 | Quick Counter Sale | Customer · Sales Staff · Backend System |
| WF-03 | Online Cart & Checkout | Farmer · Backend System · Sales Staff |
| WF-04 | payOS Payment & Reconciliation | Payer · Backend System · payOS |
| WF-05 | Order Confirmation, Stock & Credit Reservation | Sales Staff · Backend System |
| WF-06 | Store Pickup & Financial Posting | Customer · Sales Staff · Backend System |
| WF-07 | Delivery, Attempts & Incidents | Sales Staff · Backend System · Delivery Staff · Farmer |
| WF-08 | Debt Repayment | Farmer · Sales Staff · Store Owner / Admin · Backend System · payOS |
| WF-09 | Supplier Receiving & Excel Import | Supplier · Sales Staff · Backend System |
| WF-10 | Stocktake & Reconciliation | Sales Staff · Backend System · Store Owner / Admin |
| WF-11 | Manual Inventory Adjustment | Store Owner / Admin · Backend System |
| WF-12 | Order Cancellation & Cancel Remaining | Requester · Sales Staff · Backend System · payOS |
| WF-13 | Post-fulfillment Return & Inspection | Customer · Sales Staff · Store Owner / Admin · Backend System |
| WF-14 | External Refund & Completion | Store Owner / Admin · Backend System · Customer |
| WF-15 | Debt Dispute & Resolution | Farmer · Sales Staff · Store Owner / Admin · Backend System |
| WF-16 | Inventory Alerts & Expiry Handling | Sales Staff · Backend System · Store Owner / Admin |

## WF-01 — AI Disease Diagnosis & Human Verification

Backend đánh giá policy; đạt hay không đạt đều cần Human Review. Farmer chỉ thấy kết quả đã xác minh. AI lỗi vẫn có thể được chẩn đoán thủ công. Healthy chỉ nhận hướng dẫn, không đề xuất sản phẩm.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-01 - AI Disease Diagnosis and Human Verification
  subgraph FARMER["Farmer"]
    S((Start))
    F1("Upload rice leaf image")
    F2("Receive validation error")
    F3("Receive verified result and guidance")
    F4("Receive inconclusive result; retake photo")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Validate image and Farmer identity")
    D1{"Valid image and request?"}
    B2("Store private image; create SUBMITTED case")
    B3("Call AI with active model; outside DB transaction")
    D2{"Successful, valid model response?"}
    B4("Apply confidence and margin policy; store inference")
    B5("Set AI_COMPLETED; queue for human review")
    B6("Set FAILED; queue for manual review")
    B7("Check reviewer membership and can_review_ai")
    B8("Save VERIFIED result and current review")
    B9("Save INCONCLUSIVE; no recommendations")
    B10("Validate and save recommendations; notify Farmer")
  end
  subgraph AI["AI System"]
    A1("Analyze image; predict rice disease")
    A2("Return calibrated probabilities and model version")
  end
  subgraph REVIEWER["Agent / AI Reviewer"]
    R1("Request case for review")
    R2("Inspect image and available AI evidence")
    D3{"Review decision?"}
    R3("CONFIRMED: keep successful AI prediction")
    R4("CORRECTED: select final disease; manual if AI failed")
    R5("INCONCLUSIVE: record comment and retake hint")
    R6("Select treatments; products only if not Healthy")
  end
  S --> F1 --> B1 --> D1
  D1 -->|No| F2 --> E
  D1 -->|Yes| B2 --> B3 --> A1 --> A2 --> D2
  B3 -->|Timeout or service failure| B6
  D2 -->|Yes| B4 --> B5 --> R1
  D2 -->|No| B6 --> R1
  R1 --> B7 -->|Authorized| R2 --> D3
  D3 -->|Confirm; successful inference required| R3 --> B8
  D3 -->|Correct or manual diagnosis| R4 --> B8
  D3 -->|Inconclusive| R5 --> B9 --> F4 --> E
  B8 --> R6 --> B10 --> F3 --> E
```

## WF-02 — Quick Counter Sale

Chỉ áp dụng `CASH + FULL_PAYMENT + PICKUP`. Bán ghi nợ hoặc giao tận nơi dùng luồng đơn hàng thông thường. Preview không lưu dữ liệu; nhân viên xác nhận lô thực tế.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-02 - Quick Counter Sale
  subgraph CUSTOMER["Customer"]
    S((Start))
    C1("Request products and quantities")
    C2("Approve quote and pay cash")
    C3("Receive goods and sale receipt")
    E((End))
  end
  subgraph SALES["Sales Staff"]
    R1("Select registered Farmer or walk-in; enter sale lines")
    R2("Check quote; confirm actual physical lots")
    R3("Correct request or stop sale")
    R4("Confirm cash received, actual goods and sale")
  end
  subgraph BACKEND["Backend System"]
    B1("Resolve group/default price list; validate price override reason")
    B2("Return preview with total and FEFO suggestions; save nothing")
    B3("Recheck prices, quantities and eligible available lots")
    D1{"Sale valid and sufficient stock?"}
    B4("One transaction: create order; cash Payment PAID; ORDER allocation")
    B5("Confirm order; reserve actual lots; post SALE and COGS")
    B6("Consume stock reservation and prepayment; set order COMPLETED; commit")
    B7("Reject sale; no partial order, payment or stock changes")
  end
  S --> C1 --> R1 --> B1 --> B2 --> R2 --> C2 --> R4 --> B3 --> D1
  D1 -->|No| B7 --> R3 --> E
  D1 -->|Yes| B4 --> B5 --> B6 --> C3 --> E
```

## WF-03 — Online Cart & Checkout

Giá và tổng do Backend xác định. Giỏ hàng và đơn `PENDING_CONFIRMATION` chưa giữ tồn kho; credit draft chỉ được kiểm tra điều kiện, chưa giữ hạn mức.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-03 - Online Cart and Checkout
  subgraph FARMER["Farmer"]
    S((Start))
    F1("Browse catalog; add or update cart")
    F2("Choose FULL_PAYMENT or CREDIT; PICKUP or DELIVERY")
    F3("Provide own address for DELIVERY; submit checkout")
    F4("Correct cart or checkout details")
    F5("Pay through WF-04 or arrange payment with staff")
    F6("View pending order")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Resolve current prices and cart availability")
    B2("Validate cart, packaging, address ownership and credit eligibility")
    D1{"Checkout valid?"}
    B3("One transaction: snapshot order prices and customer data; convert cart")
    B4("Create PENDING_CONFIRMATION; no stock or credit reservation")
    D2{"Settlement type?"}
  end
  subgraph SALES["Sales Staff"]
    R1("Review order; proceed to WF-05 confirmation")
  end
  S --> F1 --> B1 --> F2 --> F3 --> B2 --> D1
  D1 -->|No| F4 --> E
  D1 -->|Yes| B3 --> B4 --> D2
  D2 -->|FULL_PAYMENT| F5 --> R1 --> E
  D2 -->|CREDIT; optional prepayment| F6 --> R1
```

## WF-04 — payOS Payment & Reconciliation

Được tái sử dụng cho thanh toán đơn và trả nợ. Redirect trình duyệt không chứng minh đã trả tiền. Chỉ webhook đã xác minh hoặc truy vấn trạng thái từ payOS mới ghi nhận thanh toán; xử lý lặp không phân bổ tiền lần hai.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-04 - payOS Payment and Reconciliation
  subgraph PAYER["Payer"]
    S((Start))
    P1("Request payment link for order or debt")
    P2("Open checkout or scan QR; transfer money")
    P3("Read payment status from Backend")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Validate ownership, amount and whole VND; create PENDING payment")
    B2("Return checkout URL and QR payload")
    B3("Verify webhook signature or query payOS status")
    D1{"Trusted provider result?"}
    D2{"Already settled?"}
    D3{"Locally cancelled or amount mismatch?"}
    B4("Acknowledge duplicate; do not repost")
    B5("Flag for staff reconciliation; do not allocate")
    D4{"Provider confirms PAID?"}
    B6("One transaction: mark PAID; allocate to ORDER or DEBT")
    B7("Release unused credit reservation for order prepayment; or post debt repayment")
    B8("Keep pending or record provider failure/cancellation; no allocation")
    B9("Reject invalid webhook; do not change payment")
    B10("Record link creation failure; return dependency error")
  end
  subgraph PAYOS["payOS"]
    G1("Create payment link")
    G2("Process payment")
    G3("Send webhook or return queried status")
  end
  S --> P1 --> B1 --> G1
  G1 -->|Created| B2 --> P2 --> G2 --> G3 --> B3 --> D1
  G1 -->|Failure| B10 --> P3 --> E
  D1 -->|No| B9 --> E
  D1 -->|Yes| D2
  D2 -->|Yes| B4 --> P3
  D2 -->|No| D3
  D3 -->|Yes| B5 --> P3
  D3 -->|No| D4
  D4 -->|Yes| B6 --> B7 --> P3
  D4 -->|No| B8 --> P3
```

## WF-05 — Order Confirmation, Stock & Credit Reservation

Luồng chung cho đơn tại quầy thông thường và đơn online. Walk-in không được CREDIT. Hạn mức khả dụng = hạn mức − dư nợ − phần giữ hạn mức chưa sử dụng. Chưa tạo công nợ ở bước xác nhận.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-05 - Order Confirmation Stock and Credit Reservation
  subgraph SALES["Sales Staff"]
    S((Start))
    R1("Review pending order; request confirmation")
    R2("Resolve payment, credit or stock problem")
    R3("Prepare goods; confirm physical lots at pickup or delivery allocation")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Start transaction; lock order; revalidate state and sellable lines")
    D1{"Settlement type?"}
    D2{"PAID allocations cover order total?"}
    B2("Validate registered customer, active credit profile and debt account")
    B3("Required credit = total minus paid; check available credit and policy")
    D3{"Eligible and sufficient credit?"}
    B4("Reserve required credit; snapshot term days from credit tier")
    B5("Choose eligible FEFO lots; lock and recheck balances")
    D4{"Enough available stock for every line?"}
    B6("Create inventory reservation; set order CONFIRMED; commit")
    B7("Reject confirmation; roll back all reservations")
  end
  S --> R1 --> B1 --> D1
  D1 -->|FULL_PAYMENT| D2
  D2 -->|No| B7
  D2 -->|Yes| B5
  D1 -->|CREDIT| B2 --> B3 --> D3
  D3 -->|No| B7
  D3 -->|Yes| B4 --> B5 --> D4
  D4 -->|No| B7 --> R2 --> E
  D4 -->|Yes| B6 --> R3 --> E
```

## WF-06 — Store Pickup & Financial Posting

Không tạo Delivery cho PICKUP. Được nhận hàng từng phần. Chỉ phần hàng bàn giao thành công mới xuất kho và phát sinh công nợ; mọi lô thực tế vẫn phải hợp lệ tại thời điểm xuất.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-06 - Store Pickup and Financial Posting
  subgraph CUSTOMER["Customer"]
    S((Start))
    C1("Arrive to collect confirmed pickup order")
    C2("Receive goods")
    E((End))
  end
  subgraph SALES["Sales Staff"]
    R1("Check order; select actual lots and handover quantities")
    R2("Confirm physical handover")
    R3("Correct lot selection or quantities")
    R4("Arrange next pickup or cancel remaining through WF-12")
  end
  subgraph BACKEND["Backend System"]
    B1("Validate order, whole packages, remaining quantity and eligible lots")
    D1{"Valid handover?"}
    B2("One transaction: move reservations if lots changed; post SALE with COGS")
    B3("Decrease on hand; consume stock reservation and oldest prepayment")
    D2{"Unpaid fulfilled value greater than zero?"}
    B4("For CREDIT: consume credit reservation; create Debt Entry and CREDIT_SALE transaction")
    B5("Update order from fulfilled/cancelled quantities; release unused reservations if terminal; commit")
    D3{"Open quantity remains?"}
  end
  S --> C1 --> R1 --> R2 --> B1 --> D1
  D1 -->|No; no posting| R3 --> E
  D1 -->|Yes| B2 --> B3 --> D2
  D2 -->|Yes; CREDIT only| B4 --> B5
  D2 -->|No| B5
  B5 --> C2 --> D3
  D3 -->|Yes; PARTIALLY_FULFILLED| R4 --> E
  D3 -->|No; terminal order| E
```

## WF-07 — Delivery, Attempts & Incidents

Chỉ có một Attempt `IN_PROGRESS` cho một Delivery. Bất kỳ lượng giao thành công nào cũng cần ảnh minh chứng và tên người nhận. Từ chối trước khi nhận hàng là Incident, không phải Sales Return. Nhánh sự cố dưới đây minh họa xử lý; không phải mọi lần giao thất bại đều bắt buộc mở Incident riêng.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-07 - Delivery Attempts and Incidents
  subgraph SALES["Sales Staff"]
    S((Start))
    R1("Create delivery note; confirm actual lots; assign delivery staff")
    R2("Choose retry, return to store, replacement or cancel remainder")
    R3("If stock adjustment is needed, request authorized WF-11; link movement to incident")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Validate quantities and allocations; set ASSIGNED")
    B2("Dispatch delivery; set OUT_FOR_DELIVERY")
    B3("Start one IN_PROGRESS attempt")
    D1{"Any quantity delivered?"}
    B4("Require proof image and receiver; validate delivered quantities and lots")
    B5("One transaction: SALE and COGS; consume reservations and prepayment")
    B6("Create debt only for unpaid CREDIT fulfillment; update attempt, delivery and order")
    D2{"Remaining delivery quantity?"}
    B7("Set FAILED attempt and RETRY_PENDING; no SALE or debt")
    B8("Record incident and resolution; preserve affected lot and quantity")
    D3{"Retry delivery?"}
    B9("Apply authorized cancellation/resolution; preserve completed fulfillment")
  end
  subgraph DELIVERY["Delivery Staff"]
    DLV1("Accept assignment; dispatch")
    DLV2("Start attempt; travel to customer")
    DLV3("Record actual delivered quantities, proof or failure reason")
    DLV4("Report incident if needed")
  end
  subgraph FARMER["Farmer"]
    F1("Receive all/some goods, or cannot accept delivery")
    F2("View delivery and order status")
  end
  S --> R1 --> B1 --> DLV1 --> B2 --> DLV2 --> B3 --> F1 --> DLV3 --> D1
  D1 -->|Yes| B4 --> B5 --> B6 --> D2
  D2 -->|No; DELIVERED| F2 --> E
  D2 -->|Yes; PARTIALLY_DELIVERED| R2
  D1 -->|No| B7 --> DLV4 --> B8 --> R2
  R2 --> R3 --> D3
  D3 -->|Yes| DLV1
  D3 -->|No| B9 --> F2
```

## WF-08 — Debt Repayment

Tiền mặt có thể chỉ định khoản nợ; mặc định và payOS phân bổ theo ngày đến hạn cũ nhất. Chuyển khoản ghi nhận thủ công cần quản lý xác nhận trước khi giảm nợ. Payment luôn thuộc ngữ cảnh `DEBT_REPAYMENT`.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-08 - Debt Repayment
  subgraph FARMER["Farmer"]
    S((Start))
    F1("View debt; choose repayment amount and method")
    F2("Pay cash, transfer externally or use payOS")
    F3("Receive receipt and updated debt balance")
    E((End))
  end
  subgraph SALES["Sales Staff"]
    R1("Confirm cash received; optionally select debt entries")
    R2("Record external bank transfer as PENDING")
  end
  subgraph OWNER["Store Owner / Admin"]
    O1{"Verify external bank transfer?"}
  end
  subgraph BACKEND["Backend System"]
    B1("Validate payer and amount against outstanding debt")
    D1{"Repayment method?"}
    B2("Create or confirm Payment PAID; lock customer and debt account")
    B3("One transaction: create DEBT payment_allocations; PAYMENT DebtTransactions")
    B4("Reduce entry outstanding and account balance; derive statuses; commit once")
    B5("Mark manual payment FAILED; no debt reduction")
    B6("Create payOS link; verify settlement through WF-04")
    B7("Return pending/failed status; debt unchanged")
  end
  subgraph PAYOS["payOS"]
    P1("Process payment; return trusted settlement")
  end
  S --> F1 --> B1 --> F2 --> D1
  D1 -->|Cash| R1 --> B2
  D1 -->|Manual bank transfer| R2 --> O1
  O1 -->|Confirmed| B2
  O1 -->|Rejected| B5 --> F3
  D1 -->|payOS| B6 --> P1
  P1 -->|Verified PAID| B2
  P1 -->|Not paid| B7 --> F3
  B2 --> B3 --> B4 --> F3 --> E
```

## WF-09 — Supplier Receiving & Excel Import

Nhập tay và Excel dùng cùng quy tắc mặt hàng, quy cách mua, lô và hạn dùng. Preview không ghi tồn. Phiếu chỉ làm tăng tồn khi confirm; phiếu confirmed không được sửa âm thầm.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-09 - Supplier Receiving and Excel Import
  subgraph SUPPLIER["Supplier"]
    S((Start))
    P1("Deliver goods and supplier invoice")
    E((End))
  end
  subgraph SALES["Sales Staff"]
    R1("Inspect delivered goods; select supplier")
    D1{"Input method?"}
    R2("Enter receipt lines manually")
    R3("Fill AgriSage Excel template; upload for preview")
    R4("Correct invalid lines or spreadsheet")
    R5("Import validated file")
    R6("Review DRAFT; confirm receipt")
  end
  subgraph BACKEND["Backend System"]
    B1("Parse Excel; preview products, packaging, lot, expiry and costs")
    D2{"Every Excel row valid?"}
    B2("Validate manual lines; create or update DRAFT")
    B3("Revalidate import; create one DRAFT; reject entire file on errors")
    B4("Revalidate DRAFT and active supplier; lock affected lots")
    D3{"Receipt can be confirmed?"}
    B5("One transaction: resolve/create logical lots; post STOCK_IN and movement items")
    B6("Increase base-unit stock; update lot Weighted Average Cost; confirm receipt; commit")
    B7("Reject confirmation; no stock changes")
  end
  S --> P1 --> R1 --> D1
  D1 -->|Manual| R2 --> B2 --> R6
  D1 -->|Excel| R3 --> B1 --> D2
  D2 -->|No| R4 --> D1
  D2 -->|Yes| R5 --> B3 --> R6
  R6 --> B4 --> D3
  D3 -->|No| B7 --> R4
  D3 -->|Yes| B5 --> B6 --> E
```

## WF-10 — Stocktake & Reconciliation

Phải đếm mọi dòng. Dòng stale cần lấy snapshot mới và đếm lại riêng dòng đó. Điều chỉnh giảm không được làm tồn thấp hơn lượng đã giữ cho đơn khác. Lô chưa có trong hệ thống phải qua nhập hàng, không tự tạo bằng kiểm kho.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-10 - Stocktake and Reconciliation
  subgraph SALES["Sales Staff"]
    S((Start))
    R1("Create stocktake scope; start counting")
    R2("Count each lot in base units; record quantity and difference reason")
    R3("Recount missing or refreshed stale lines")
    R4("Resolve cost, reason or reserved-stock conflict")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Snapshot each lot quantity, cost and timestamp; create DRAFT; start IN_PROGRESS")
    B2("Save counts")
    B3("Lock balances; check every line counted and not stale")
    D1{"Any missing or stale line?"}
    B4("Refuse completion; refresh only stale snapshots and clear their counts")
    B5("Compute counted minus snapshot; validate reasons, positive cost and reserved floor")
    D2{"Adjustments valid?"}
    B6("One transaction: post ADJUSTMENT_IN/OUT for nonzero differences; update stock and costs")
    B7("Set stocktake COMPLETED; commit; unchanged lines have no movement")
  end
  subgraph OWNER["Store Owner / Admin"]
    O1("Review results; request stocktake completion")
  end
  S --> R1 --> B1 --> R2 --> B2 --> O1 --> B3 --> D1
  D1 -->|Yes| B4 --> R3 --> B2
  D1 -->|No| B5 --> D2
  D2 -->|No; rollback| R4 --> R2
  D2 -->|Yes| B6 --> B7 --> E
```

## WF-11 — Manual Inventory Adjustment

Mọi thay đổi tồn vật lý đều có StockMovement và StockMovementItem. Một yêu cầu chỉ gồm dòng tăng hoặc chỉ dòng giảm. Được tái sử dụng để xử lý hàng mất/hỏng trong Incident; Incident chỉ tham chiếu movement đã tạo.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-11 - Manual Inventory Adjustment
  subgraph OWNER["Store Owner / Admin"]
    S((Start))
    O1("Select existing lots; enter base-unit deltas, reason and note")
    O2("Correct data or move affected reservations first")
    O3("Review posted movement; link to delivery incident if applicable")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Validate permission, store, nonzero deltas and same sign; lock lots")
    D1{"Increase or decrease?"}
    B2("Validate entered/current average unit cost; zero-stock lot needs entered cost")
    B3("Use lot average cost; ensure resulting stock is at least reserved stock")
    D2{"All lines valid?"}
    B4("One transaction: post ADJUSTMENT_IN or ADJUSTMENT_OUT with items")
    B5("Update balances and cost value; record audit; commit")
    B6("Reject request; no partial stock change")
  end
  S --> O1 --> B1 --> D1
  D1 -->|Increase| B2 --> D2
  D1 -->|Decrease| B3 --> D2
  D2 -->|No| B6 --> O2 --> E
  D2 -->|Yes| B4 --> B5 --> O3 --> E
```

## WF-12 — Order Cancellation & Cancel Remaining

Farmer chỉ tự hủy đơn của mình ở `PENDING_CONFIRMATION`; nhân viên có thể hủy toàn đơn trước fulfillment. Sau khi đã giao một phần chỉ hủy phần còn lại. Không đảo xuất kho hoặc xóa nợ cho hàng đã giao bằng thao tác hủy đơn.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-12 - Order Cancellation and Cancel Remaining
  subgraph REQUESTER["Requester"]
    S((Start))
    Q1("Farmer requests own pending cancellation; or customer contacts staff")
    Q2("Receive updated order and refund requests")
    E((End))
  end
  subgraph SALES["Sales Staff"]
    R1("For staff requests: choose full cancel or cancel line remainder; record reason")
  end
  subgraph BACKEND["Backend System"]
    B1("Check actor, ownership and state; lock order and affected records")
    D1{"Cancellation allowed?"}
    D2{"Any fulfilled quantity?"}
    B2("Cancel entire order before fulfillment")
    B3("Staff only: cancel remaining quantity; preserve fulfilled portion")
    B4("Release affected inventory and unused credit reservations")
    D3{"Order terminal with cancelled quantity?"}
    D4{"Pending payOS link?"}
    B5("Recheck provider result; abort/reconcile if already paid or cancellation failed")
    B6("Release refundable unconsumed prepayment; request PENDING refunds per payment")
    B7("Persist cancellation, reservations and audit atomically; commit")
    B8("Reject; no successful local cancellation")
  end
  subgraph PAYOS["payOS"]
    P1("Cancel pending link; return authoritative status")
  end
  S --> Q1
  Q1 -->|Farmer self-service| B1
  Q1 -->|Via staff| R1 --> B1
  B1 --> D1
  D1 -->|No| B8 --> E
  D1 -->|Yes| D2
  D2 -->|No| B2 --> B4
  D2 -->|Yes; remaining only| B3 --> B4
  B4 --> D3
  D3 -->|No; other lines remain open| B7
  D3 -->|Yes| D4
  D4 -->|Yes| P1
  P1 -->|Cancelled| B6
  P1 -->|Paid or error| B5 --> B8
  D4 -->|No| B6 --> B7 --> Q2 --> E
```

## WF-13 — Post-fulfillment Return & Inspection

Chỉ trả hàng đã fulfillment. Mỗi dòng truy đúng một nguồn giao hàng/lô hoặc một dòng SALE của pickup. Số lượng khả dụng để trả đã trừ cả yêu cầu trả đang xử lý, tránh trả trùng. Nhập lại đúng lô gốc và giá vốn gốc; hàng hỏng không làm tăng tồn khả dụng.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-13 - Post-fulfillment Return and Inspection
  subgraph CUSTOMER["Customer"]
    S((Start))
    C1("Request full or partial return of fulfilled goods")
    C2("Send or hand goods back after approval")
    C3("Receive rejection or return settlement status")
    E((End))
  end
  subgraph SALES["Sales Staff"]
    R1("Create request for walk-in/staff-assisted return")
    R2("Receive returned goods; inspect every line")
    R3("Record RESELLABLE, DAMAGED, EXPIRED or UNUSABLE")
  end
  subgraph OWNER["Store Owner / Admin"]
    O1{"Approve return request?"}
    O2("Confirm completed inspection")
  end
  subgraph BACKEND["Backend System"]
    B1("Validate ownership, fulfilled source and returnable quantity; create REQUESTED")
    B2("Record REJECTED and audit reason")
    B3("Set APPROVED")
    B3R("Set RECEIVED on physical handback")
    B4("Validate all inspections; mark INSPECTED; calculate value using original selling price")
    B5("One transaction: reduce attributable unpaid debt first; post RETURN debt transactions")
    B6("For RESELLABLE: RETURN_IN into original lot at original COGS; other lines WRITE_OFF without stock-in")
    B7("Refund due = return value minus debt reduction")
    D1{"Any refund due?"}
    B8("Set PARTIALLY_RESOLVED; continue to WF-14")
    B9("Set COMPLETED when all restock links exist and no refund remains")
  end
  S --> C1
  C1 -->|Registered Farmer self-service| B1
  C1 -->|Via staff| R1 --> B1
  B1 --> O1
  O1 -->|No| B2 --> C3 --> E
  O1 -->|Yes| B3 --> C2 --> R2 --> B3R --> R3 --> O2 --> B4 --> B5 --> B6 --> B7 --> D1
  D1 -->|Yes| B8 --> C3
  D1 -->|No| B9 --> C3
```

## WF-14 — External Refund & Completion

Áp dụng cho trả hàng và hủy đơn. Nhân sự có quyền quản lý thực hiện hoàn tiền ngoài hệ thống rồi ghi nhận; không có payOS refund tự động. Refund FAILED/CANCELLED là trạng thái cuối, thử lại bằng Refund mới.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-14 - External Refund and Completion
  subgraph OWNER["Store Owner / Admin"]
    S((Start))
    O1("Open cancellation refund or create return refund request")
    O2("Pay cash back or transfer externally")
    D1{"Refund actually paid?"}
    O3("Record reference or proof; confirm completion")
    O4("Record FAILED or CANCELLED; create new refund to retry if needed")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Check source, payment and remaining refundable cap; create/use PENDING refund")
    B2("One transaction: set Refund COMPLETED; record actor and evidence")
    B3("Update original Payment to PARTIALLY_REFUNDED or REFUNDED when linked")
    D2{"Sales Return source?"}
    D3{"All restock lines linked and total completed refunds covers refund due?"}
    B4("Set Sales Return COMPLETED")
    B5("Keep Sales Return PARTIALLY_RESOLVED")
    B6("Persist refund failure/cancellation; do not mark money returned")
  end
  subgraph CUSTOMER["Customer"]
    C1("Receive refunded money and updated status")
  end
  S --> O1 --> B1 --> O2 --> D1
  D1 -->|No| O4 --> B6 --> E
  D1 -->|Yes| O3 --> B2 --> B3 --> D2
  D2 -->|No; cancelled order| C1 --> E
  D2 -->|Yes| D3
  D3 -->|Yes| B4 --> C1
  D3 -->|No| B5 --> C1
```

## WF-15 — Debt Dispute & Resolution

DISPUTED vẫn được nhận thanh toán. ADJUST chỉ giảm dư nợ; tăng khoản phải thu phải tạo Debt Entry `MANUAL_ADJUSTMENT` riêng. Mọi thay đổi số dư có DebtTransaction và audit, không sửa/xóa bút toán đã ghi.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-15 - Debt Dispute and Resolution
  subgraph FARMER["Farmer"]
    S((Start))
    F1("View own debt; submit dispute and reason")
    F2("Receive resolution and updated outstanding amount")
    E((End))
  end
  subgraph SALES["Sales Staff"]
    R1("Review order, fulfillment and repayment evidence")
    D1{"Keep debt or request reduction/cancellation?"}
    R2("KEEP debt; record reason")
  end
  subgraph OWNER["Store Owner / Admin"]
    D2{"Authorized resolution?"}
    O1("ADJUST: specify reduction no greater than outstanding")
    O2("CANCEL: waive remaining outstanding with reason")
    O3("Reject proposed reduction; keep debt")
  end
  subgraph BACKEND["Backend System"]
    B1("Check ownership and entry state; record DISPUTE; keep payment allocation allowed")
    B2("Record KEEP and resolve dispute; balance unchanged")
    B3("One transaction: lock account; post ADJUSTMENT_OUT; update outstanding and account balance")
    B4("Record action history and audit; preserve posted ledger")
  end
  S --> F1 --> B1 --> R1 --> D1
  D1 -->|Keep| R2 --> B2 --> B4
  D1 -->|Reduce or cancel| D2
  D2 -->|Reduce| O1 --> B3 --> B4
  D2 -->|Cancel| O2 --> B3
  D2 -->|Reject change| O3 --> B2
  B4 --> F2 --> E
```

## WF-16 — Inventory Alerts & Expiry Handling

Tồn vật lý khác tồn có thể bán. Đánh dấu EXPIRED không tự xuất hủy hàng; thay đổi số lượng phải qua WF-11. Kiểm tra ngày hết hạn luôn chặn bán ngay cả khi chưa chạy chức năng đánh dấu hết hạn.

```mermaid
%%{init: {"theme":"dark"}}%%
swimlane-beta TB
  accTitle: WF-16 - Inventory Alerts and Expiry Handling
  subgraph SALES["Sales Staff"]
    S((Start))
    R1("Open stock summary and expiry/low-stock alerts")
    R2("Inspect alerts and affected lots")
    R3("Plan replenishment through WF-09")
    R4("Inspect near-expiry stock; follow eligible-lot sales rules")
    R5("Move affected reservations to eligible lots through fulfillment/allocation workflow")
    E((End))
  end
  subgraph BACKEND["Backend System"]
    B1("Read on-hand, reserved and sellable available quantities in base units")
    B2("Compute EXPIRING, EXPIRED and LOW_STOCK using Vietnam dates")
    D1{"Alert selected for handling?"}
    B3("Mark ACTIVE lots past expiry as EXPIRED; keep physical quantity unchanged")
    B4("Refresh summary and alerts from posted movements and lot status")
  end
  subgraph OWNER["Store Owner / Admin"]
    O1("Run expire-due for expired lots")
    O2("If physical stock must be removed, post EXPIRED adjustment through WF-11")
  end
  S --> R1 --> B1 --> B2 --> R2 --> D1
  D1 -->|LOW_STOCK| R3 --> E
  D1 -->|EXPIRING| R4 --> E
  D1 -->|EXPIRED| O1 --> B3 --> R5 --> O2 --> B4 --> E
  D1 -->|No action needed| E
```

## Nguồn đối chiếu nghiệp vụ

- [Database design](../../reference/DATABASE_DESIGN.md): XXVI; 35.4–35.12; 35.18–35.21.
- [L1 Counter sale](../../reference/api-flows/FLOW_1_COUNTER_SALE.md): pricing, payments, confirmation, pickup, cancellation, quick counter sale.
- [L2 Online order & delivery](../../reference/api-flows/FLOW_2_ONLINE_ORDER_DELIVERY.md): checkout, payOS, delivery, attempts, incidents.
- [L3 Credit & debt](../../reference/api-flows/FLOW_3_CREDIT_DEBT.md): settlement guard, credit reservation, financial posting, repayment, manual bank-transfer confirmation, dispute resolution.
- [L4 Inventory & returns](../../reference/api-flows/FLOW_4_INVENTORY_RETURNS.md): receipt import, alerts, stocktake, adjustment, return, refund.
- [AI diagnosis](../../reference/api-flows/AI_DIAGNOSIS.md): backend policy decision, compulsory human review, verified-only Farmer result, Healthy restrictions.
- [Shared transaction ownership](../../reference/api-flows/README.md): cross-flow interfaces and composable steps.

Không thay đổi code, database hay migration.
