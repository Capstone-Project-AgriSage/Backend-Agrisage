# Gói review — công nợ theo kỳ thu nợ / vụ mùa (L3, nhánh `feature/f3-21-debt-permission-lock`)

Soạn 2026-11-04 bởi owner L3 (người dùng) cùng trợ lý. **Đây là gói để gửi đi; chưa ai đã review.** Mọi "kết luận" ở mục 6 còn trống.

## 0. Cách đọc gói này

- **Review dựa trên diff cuối nhánh so với `origin/main`**, không dựa từng commit: nhiều commit sau đã sửa tiếp commit trước.
  Code cuối: commit `c2c574f` (các commit sau chỉ là tài liệu). Cả nhánh: khoảng 134 file, +59.155/−188 dòng (phần lớn là 4 file
  `*.Designer.cs` do EF sinh, ~48.000 dòng — không cần đọc).
- **Nhánh chưa push**, nên người khác chưa có commit. Hai cách chia sẻ (chọn một — việc push do owner L3 quyết):
  1. push nhánh + mở **draft PR** để bình luận được trên từng dòng (khuyến nghị);
  2. gửi file trong thư mục `D:\CAPSTONE BE\review-packages-f3\` (ngoài repo): `branch.bundle` (toàn bộ commit nhánh, dùng
     `git fetch branch.bundle`) và các file `.patch`/`.sql` theo từng người ở dưới.
- Vai trò theo `docs/reference/api-flows/README.md` §3: **L1 = Lead**, L2 = Teammate 2, L3 = Teammate 3 (owner L3), L4 = Teammate 4.
  Chưa có tên người cụ thể cho L1/L2/L4 trong tài liệu hay CODEOWNERS (repo không có) → **chưa gán tên**.
- Đã kiểm thử trên code cuối: build 0 lỗi/0 cảnh báo; unit 799; integration trên Postgres cục bộ đạt hết (3 test bỏ qua vì cần Supabase
  Storage), chạy 2 lần; chạy thường 1099 đạt. **Chưa chạy trên Supabase hay dữ liệu thật.**

## 1. L1 (Lead) — đơn hàng: gắn vụ khi xác nhận đơn chịu — file `01-L1-orders.patch`

Thay đổi (5 file, +44/−7), tách riêng ở commit `a9b2db5`; nội dung cuối ở diff:
`IOrderSettlementGuard`/`OrderSettlementGuard` (trả thêm `CropSeasonId`), `OrderConfirmer` (truyền vụ vào `Order.Confirm`, audit `cropSeasonId`),
`OrderContracts` (`OrderResponse` thêm `OriginSeason`, `ExpectedDueDate`, đều tùy chọn), `OrderQueries` (nhận `IDateTimeProvider? clock = null`).
Không đụng `CartService`/`MeOrderService`.

Cần kết luận:
1. Đơn chịu được gắn đúng vụ theo ngày **xác nhận giờ Việt Nam** và giữ vụ đó; không có vụ → hạn theo hạng tín dụng như cũ; không phá luồng bán quầy/online.
2. Hai trường mới trong `OrderResponse` (chỉ thêm) chấp nhận được.
3. **B4 (đề xuất — chờ duyệt):** bắt buộc tham số đồng hồ của `OrderQueries` (hiện tùy chọn, thiếu thì `expectedDueDate` im lặng là `null`). Đăng ký DI
   (`AddScoped<OrderQueries>()`) tự cấp đồng hồ; nhưng **16 chỗ trong 12 file test (L1, L2, Advisory, Payments, Reports…) dựng tay `new OrderQueries(context)`** (AdvisoryDatabaseTests,
   DeliveryDatabaseTests, OrdersDatabaseTests…) phải sửa theo — thay đổi test của nhiều luồng nên cần Lead đồng ý. Lead cho phép thì làm trong một commit riêng.

## 2. L1 và L2 — đổi mức khóa cửa hàng — file `02-locks.patch` (3 file, +100/−1)

Thay đổi: `RowLockService.LockStoreAsync` đổi `SELECT … FOR UPDATE` thành `FOR NO KEY UPDATE`; comment ở `IRowLockService`; test `StoreLockDatabaseTests`.
**README §3.1 ghi file này "chỉ thêm", nên đây là ngoại lệ cần L1 đồng ý.**

Vì sao: log Postgres cho thấy vòng chờ — gia hạn nợ (khóa cửa hàng → khóa nông dân) và giảm/xóa/khiếu nại của cùng khách (khóa nông dân → ghi `audit_logs`,
có khóa ngoại tới `stores`, bị `FOR UPDATE` chặn) chờ nhau; Postgres hủy một yêu cầu (40P01). Test V2 đỏ xác định, kể cả trước các sửa gần đây.
`FOR NO KEY UPDATE` vẫn loại trừ mọi lần khóa cửa hàng khác nhưng không chặn việc chỉ chèn bản ghi có khóa ngoại.

**Mọi nơi gọi `LockStoreAsync` (21 chỗ, 11 file)** — đều nằm ở Customers, Credit, Debt; đều là bên tự khóa cửa hàng; L1 (Orders, Payments) và L4 không gọi:
`Customers/CustomerService` (4), `Customers/CustomerGroupService` (4), `Credit/CustomerCreditService` (3), `Debt/{CropSeasonService (2), CropSeasonClosingService,
DebtPolicyService, DebtRolloverService (2), DebtReminderStartService, DebtService (ManualAsync)}`.
Không nơi nào dựa vào việc khóa cửa hàng chặn **người khác** chèn bản ghi con (cửa hàng không bị xóa hay đổi khóa).

Cần kết luận (L1 và L2 mỗi bên): đồng ý đổi mức khóa; không có luồng của mình dựa vào hành vi cũ. `LockFarmerProfileAsync` giữ nguyên (chưa có bằng chứng cần đổi).

## 3. Lead — 4 migration, SQL, tác động dữ liệu, RolesPage — file `03-migrations.sql` + `03-migrations.patch`

| Migration | Việc | Tác động dữ liệu |
|---|---|---|
| `20261102193332_LockDebtPermissionDelegation` | 9 mã quyền `is_delegable=false`; thu hồi cấp quyền của người không phải Chủ; tăng version; ghi audit | **Sửa dữ liệu** (không đổi cấu trúc). `Down()` không cấp lại quyền |
| `20261102203458_CropSeasonsAndDebtPolicy` | bảng `crop_seasons`, `store_debt_policies`; cột mới ở `debt_entries`, `debt_entry_actions`, `orders` | Cấu trúc + `original_due_date` NOT NULL **backfill**. `Down()` từ chối khi đã có dữ liệu vụ |
| `20261102205426_CropSeasonDebtPermissions` | 6 mã quyền mới + gán mặc định theo vai trò | Thêm dòng |
| `20261103161207_DebtReminderStartDate` | cột `store_debt_policies.reminders_start_date date NULL` | Một `ADD COLUMN` |

Điểm cần Lead xem (4 điểm trong `F3_21_PART2_LOCK_DELEGATION_SQL.md` mục 8 cho migration 1) và: thứ tự chạy, kế hoạch sao lưu/khôi phục (mục 8),
**`RolesPage` gửi toàn bộ danh sách quyền của vai trò → nếu FE cũ còn chạy sau migration, lưu vai trò sẽ lỗi 403/422** → FE phải triển khai sát BE.
Câu SQL chỉ-đọc để đếm quy mô trên Supabase nằm trong `F3_21_PART2_LOCK_DELEGATION_SQL.md` mục 4 và `F3_20_REMINDER_START_MIGRATION_PROPOSAL.md` mục 7.

## 4. Lead / nhóm nghiệp vụ — các câu cần trả lời trước khi mở F3.14 phần còn lại và B1

Mỗi câu cần: **người chịu trách nhiệm trả lời**, **kết luận ghi lại** (mục 6).

| # | Câu hỏi | Chặn việc | Vì sao hỏi |
|---|---|---|---|
| C | Có cửa hàng dùng thật chưa? Ứng dụng Flutter có gọi `adjust`/`cancel`/`change-due-date`/mở lại tín dụng không? Server thật có đặt `Credit__BlockCreditWhenOverdue` không? | F3.14 (bắt buộc mã lý do), cách chuyển đổi client cũ | Bắt buộc mã lý do là thay đổi hợp đồng: client cũ không gửi mã sẽ lỗi |
| E | Duyệt hai danh sách mã: 7 mã giảm/xóa/đổi hạn (`MAT_MUA`, `THIEN_TAI`, `KHO_KHAN`, `TRI_AN`, `SAI_SOT`, `KHONG_THU_HOI`, `KHAC`) và 4 mã được dùng khi gia hạn (`MAT_MUA`, `THIEN_TAI`, `KHO_KHAN`, `KHAC`) | F3.14, B1 | Hợp đồng API và dữ liệu báo cáo |
| D12-1 | Khách **không có nợ** ở một vụ liền trước có được tính là "đúng hạn" và xuất hiện trong danh sách gợi ý tăng hạn mức không? Hiện: không (bị loại) | B2 | Quy tắc nghiệp vụ chưa rõ |
| D12-2 | Danh sách gợi ý lọc/sắp xếp theo tiêu chí nào (hiện: theo khách, đủ `requiredSeasons` vụ đóng liền trước, mọi khoản trả bằng thanh toán thật ≤ hạn gốc, không gia hạn/giảm/xóa/trả hàng)? | B2 | Phân trang chỉ làm sau khi chốt đúng tập dữ liệu |
| BR-D | Nguồn của các quy tắc BR-D (hiện chỉ có trong tài liệu do AI soạn) | Nghiệm thu | Truy nguồn |

**B1 đã được xem xét riêng:** phần "kiểm tra dữ liệu theo quy tắc đã chốt" **không thiếu gì**. Quy tắc đã chốt cho `change-due-date` (FLOW_3 mục 8d): chỉ Chủ, không đặt ngày
trong quá khứ, không tính là gia hạn, ghi `exceedsTotalExtension`; code làm đủ bốn điều, khoản đã trả/hủy bị Domain chặn. Ngày sớm hơn hạn hiện tại nhưng chưa qua **không** bị
quy tắc nào cấm. Mọi mã trong 7 mã đều được nhận cho cả ba hành động (theo tài liệu). Nên B1 hoàn toàn chờ hợp đồng mã lý do (câu C và E), không có đầu vào "chắc chắn sai" nào đang lọt.

## 5. L1 — phụ trách Payments: phạm vi sửa F3.17 phần biên nhận — xin đồng ý

Đề xuất — chờ duyệt: thêm `outstandingBySeason[]` vào phản hồi chi tiết thanh toán trả nợ (`PaymentResponse` hoặc một DTO riêng), gồm theo từng vụ đang thu: vụ, số tiền đã phân bổ
bởi lần thanh toán này, số dư còn lại của khách ở vụ đó. Dữ liệu lấy từ `payment_allocations` → `debt_entries.collection_crop_season_id`; **không đổi schema**.
Chạm: `Payments/PaymentContracts.cs`, `PaymentQueries.cs` (read-only), test. Cần L1 đồng ý phạm vi; làm trong commit riêng để L1 review.

## 6. Ghi kết luận (điền khi có)

| Mục | Người (theo vai trò) | Kết luận (đồng ý / sửa thế nào / từ chối) | Ngày | Ghi chú |
|---|---|---|---|---|
| 1. Đơn hàng `a9b2db5` + B4 | L1 — Lead | | | |
| 2. Đổi mức khóa cửa hàng | L1 — Lead | | | |
| 2. Đổi mức khóa cửa hàng | L2 — Teammate 2 | | | |
| 3. 4 migration + thứ tự + sao lưu | Lead | | | |
| 4. Câu C | Lead / nhóm | | | |
| 4. Danh sách mã lý do (E) | Lead | | | |
| 4. D12-1, D12-2 | Người soạn tài liệu + owner L3 | | | |
| 4. Nguồn BR-D | Người soạn tài liệu | | | |
| 5. Phạm vi sửa Payments (F3.17) | L1 — Lead | | | |

## 7. Các lựa chọn đã chốt (owner L3, 2026-11-04) — để người review biết

| Việc | Quyết định | Ghi chú |
|---|---|---|
| B6 — nhắc lại khoản bị bỏ qua lúc T0 | **Hoãn** khỏi bản này | Là chức năng mới: cần xác định khoản, mốc, người nhận, chống trùng |
| B7 — cập nhật số trong tin tổng hợp đã gửi | **Giữ số tại thời điểm gửi** | Tin ghi "(tổng hợp lúc HH:mm dd/MM/yyyy)"; nhóm phát sinh sau đó trong ngày chưa nằm trong tin |
| B11 — giới hạn vòng lặp khóa nông dân | **Giữ nguyên** | Có căn cứ: mỗi lượt khóa thêm ít nhất một nông dân mới hoặc dừng, số nông dân hữu hạn → tối đa (số nông dân có khoản đang thu của vụ) + 1 lượt; thực tế 2 lượt. Không thêm giới hạn để khỏi bỏ sót nông dân (comment trong `ExtendCollectingDebtAsync`) |
| Dữ liệu demo | **Đã làm** bộ tối thiểu cho DB cục bộ | Lệnh `--seed-debt-demo`, từ chối DB không cục bộ/Production; có test |
| Lô tin tổng hợp mỗi lượt | **30, cấu hình được** (`BackgroundJobs:DebtSummaryBatch`) | Là 30 tin (mỗi ngày tối đa 3 tin), không phải "10 ngày cố định"; giới hạn này giới hạn việc ghi mỗi lượt, **chưa chứng minh** cả lượt chạy nhẹ |
| B3 — truy vấn chống trùng | **Đã thu hẹp phạm vi tra khóa; chưa đo hiệu năng** | Hành vi không đổi (chống trùng thật ở `NotificationWriter`); muốn khẳng định nhanh hơn phải đo trước/sau trên dữ liệu đại diện |

## 8. Tình trạng phần "đã xong phần chính, còn phần…" và tiêu chí chấp nhận bản phát hành

| Mục | Trạng thái ghi đúng | Phần còn lại | Là tiêu chí phát hành hay hoãn? |
|---|---|---|---|
| F3.14 gia hạn một khoản | **Đã xong phần chính**; còn bắt buộc mã lý do khi giảm/xóa/đổi hạn | Câu C + E; nếu bật bắt buộc: cách chuyển đổi client cũ | *Đề xuất — chờ duyệt:* hoãn sang bản sau nếu chưa có trả lời; mã lý do vẫn tùy chọn |
| F3.17 xem nợ | **Đã xong phần chính** (cột vụ, trạng thái hạn, "Cần quyết định", cảnh báo hạn mức); còn `outstandingBySeason[]` trên biên nhận | L1 đồng ý phạm vi (mục 5) | *Đề xuất — chờ duyệt:* hoãn nếu L1 chưa đồng ý; FE ẩn mục này |
| B2 gợi ý hạn mức | Chạy được theo quy tắc hiện tại, tải hết dữ liệu vào bộ nhớ | D12-1, D12-2 | Hoãn |
| B1, B6, B7, B11 | Xem mục 4, 7 | | Hoãn / đã chốt |

Tiêu chí tối thiểu để phát hành (đề xuất — chờ duyệt): mục 1, 2, 3 đã có kết luận "đồng ý"; test xanh trên bản đã tích hợp `main` mới nhất; 4 migration đã thử trên DB thử.

## 9. Kế hoạch phát hành — tách thao tác Git khỏi triển khai DB (đề xuất — chờ Lead duyệt)

**A. Git (chỉ khi owner L3 yêu cầu):**
1. Tích hợp `main` mới nhất vào nhánh. 2. Xử lý xung đột — đặc biệt migration: nếu `main` có migration mới hơn `InventoryReplenishment`, tạo lại 4 migration của nhánh sau migration đó
   (quy trình đã dùng ở F3.21: xóa file migration + khôi phục snapshot từ Git, rồi tạo lại; không dùng `ef migrations remove` vì nó kết nối Supabase).
3. Chạy lại build, unit, integration cục bộ ×2 và chạy thường trên bản đã tích hợp. 4. Push, mở draft PR, review (mục 1–5). 5. Merge vào `main` **một lần**.

**B. DB / BE / FE (Lead quyết kế hoạch cho DB đang dùng; chỉ làm sau bước A.5 hoặc trên nhánh thử):**
1. **Thử 4 migration trên một bản sao hoặc DB thử** bằng script `03-migrations.sql`; đối chiếu số dòng bị đổi với câu đếm chỉ-đọc (mục 3). Chạy thử `Down()` ở DB thử nếu cần biết đường lui.
2. Sao lưu DB đang dùng (Lead). Ghi giờ bắt đầu cửa sổ triển khai.
3. Áp 4 migration theo thứ tự. Migration 2–4 chỉ **thêm** bảng/cột/dòng (BE cũ vẫn chạy được trên schema mới); migration 1 đổi dữ liệu quyền (không hoàn tác bằng `Down()`).
4. Triển khai BE mới. 5. Triển khai FE (Management-Webs, đặc biệt `RolesPage`) **ngay sau BE**; FE cũ lưu vai trò sẽ lỗi 403/422 trong khoảng giữa. 6. Smoke test theo `docs/F3_SEASON_SWAGGER_TEST.md`.
7. Nhập các vụ mùa. 8. Chủ gọi `POST /api/debt-reminders/initialize` với `dryRun` để xem số liệu, rồi chạy thật — **bước cuối**; từ lúc này job nhắc nợ mới gửi thông báo.
- Đường lui: khôi phục từ bản sao lưu (migration 1 và 2 không có `Down()` an toàn trên dữ liệu thật). Cần Lead xác nhận.
