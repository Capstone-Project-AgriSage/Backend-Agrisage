# Task FE — công nợ theo vụ mùa (Luồng 3), chia song song với phần BE đang chờ review

Soạn 2026-11-04. Nguồn: `docs/FE_GUIDE_FLOW_3.md` (hợp đồng cho FE, có phụ lục TypeScript) và `docs/reference/api-flows/FLOW_3_CREDIT_DEBT.md` mục 8b–8i.
Chạy thử: dữ liệu demo bằng `--seed-debt-demo` (DB cục bộ) và `docs/F3_SEASON_SWAGGER_TEST.md`. Chưa có ai nhận task; "Người làm" để trống.

**Quy ước.** *Sẵn sàng* = API đã xong và đã kiểm thử ở BE (vẫn chờ review L1/Lead nhưng hợp đồng đã ổn định, đổi nếu có sẽ báo). *Chờ hợp đồng* = BE chưa chốt;
**FE không tự chọn giá trị**, làm sau khi hợp đồng được duyệt. Mã lý do, danh sách mã và việc "bắt buộc" đều thuộc loại *chờ hợp đồng* (xem mục E).

## Ưu tiên

| Ưu tiên | Việc | Lý do |
|---|---|---|
| **P0** | M1 màn phân quyền `RolesPage` (và ẩn nút chỉ-Chủ M2) | Liên quan trực tiếp migration: FE cũ lưu vai trò sẽ lỗi 403/422 ngay sau khi migration chạy. Phải sẵn sàng **trước** hoặc **cùng lúc** triển khai BE |
| **P0** | F1 kiểm tra việc Flutter có gọi API nợ không | Trả lời một phần câu C (bắt buộc mã lý do), rẻ, chặn quyết định BE |
| P1 | M3 vụ mùa, M4 chính sách (kèm bắt đầu nhắc nợ), M5 danh sách + chi tiết khoản nợ | Nền cho mọi màn khác |
| P2 | M6 gia hạn một khoản/cả vụ, M7 "Cần quyết định" + bảng điều khiển, M8 đóng vụ + báo cáo + gợi ý hạn mức, M9 tạo đơn chịu | |
| P2 | W1–W3 Farmer-Web | |
| Sau | Mọi form mã lý do bắt buộc; biên nhận theo vụ | *Chờ hợp đồng* |

## A. Management-Webs (Chủ và nhân viên)

| Mã | Task | API chính (FE_GUIDE) | Trạng thái | Ghi chú / tiêu chí chấp nhận |
|---|---|---|---|---|
| **M1** | **`RolesPage`: không gửi mã chỉ-Chủ** | màn phân quyền hiện có; `grantablePermissions` | Sẵn sàng — **P0** | Hiện gửi toàn bộ danh sách quyền của vai trò. Sau migration, 9 mã (`DEBT.ADJUST`, `DEBT.CANCEL`, `DEBT.MANUAL`, `DEBT.DUE_DATE`, `BANK_PAYMENTS.CONFIRM`, `BANK_PAYMENTS.REJECT`, `CREDIT.SUSPEND`, `CREDIT.BLOCK`, `CREDIT.ACTIVATE`) không cấp được cho nhân viên → chỉ gửi mã trong danh sách cấp được (như `MemberPermissionEditor`). Kiểm: lưu vai trò Sale không còn lỗi; mã chỉ-Chủ hiển thị khóa/ẩn; **kiểm cả trước và sau khi chạy migration trên môi trường thử** |
| **M2** | Ẩn/vô hiệu nút chỉ-Chủ với nhân viên | §2 | Sẵn sàng — P0 | Giảm/xóa nợ, ghi nợ thủ công, đổi hạn (trước đây nhân viên được), xác nhận/từ chối chuyển khoản trả nợ, tạm ngưng/khóa/mở lại tín dụng, đổi `allowCreditPurchase`. Server vẫn trả 403 nếu lách; FE chỉ cải thiện trải nghiệm |
| M3 | Danh sách + tạo/sửa vụ mùa; hộp thoại cảnh báo sửa ngày dời vụ kế | §4.1 | Sẵn sàng | Vụ đầu nhập `orderWindowStart`, vụ sau do server điền. Tùy chọn "áp hạn mới cho khoản đang thu" cần `reason`. **Sau khi lưu có `applyNewDueDateToCollectingDebts`, hiển thị `debtExtension.moved` và `debtExtension.skippedDisputed`** ("Đã dời N khoản; M khoản đang khiếu nại giữ hạn cũ — cần Chủ quyết"); **chịu được `debtExtension = null`** ở các lần đọc thường và khi không chọn tùy chọn. Cập nhật `cropSeasonsApi.ts` theo §7 mục 5 |
| M4 | Màn chính sách công nợ + **Bắt đầu nhắc nợ** | §4.2 | Sẵn sàng | Các trường chính sách và luật kiểm (`ownerReviewAfterRollovers ≥ max(staffRolloverLimit, 1)`). Hiện cảnh báo "Nhắc nợ chưa bắt đầu" khi `remindersStartDate = null`; nút **Bắt đầu nhắc nợ** (chỉ Chủ): gọi `dryRun: true` → hộp xác nhận với `startDate`, `skippedTotal`, chi tiết `skipped.N1…N5`, `groupsWithLaterMilestones` → Chủ đồng ý → `dryRun: false`. Ẩn nút khi đã có `remindersStartDate`. Ghi rõ "đặt một lần, không đổi được" |
| M5 | Danh sách + chi tiết khoản nợ: cột vụ phát sinh/đang thu, hạn gốc, số lần gia hạn, `dueState`, bộ lọc; khối lịch sử hạn | §3.1 mục 3, 4; §4.3 | Sẵn sàng | Hành động cũ không có mã lý do hiển thị "Trước khi áp dụng chính sách" (`isPrePolicy`) |
| M6 | Gia hạn một khoản; gia hạn cả vụ (xem trước → xác nhận) | §4.5, §4.6 | Sẵn sàng | Một khoản: mã trong **4 mã khó khăn** + ghi chú bắt buộc. Cả vụ: gọi `dryRun: true` hiện danh sách gia hạn và bỏ qua + lý do; **ghi chú "đây là bản xem trước, kết quả thật kiểm lại lúc xác nhận và có thể khác"**; chịu được lỗi lúc chạy thật |
| M7 | "Cần quyết định" + bảng điều khiển (vụ hiện tại, đến hạn 7 ngày, cảnh báo hạn mức) | §3.1 mục 6, 10 | Sẵn sàng | |
| M8 | Đóng vụ (`closing-check`), báo cáo nợ theo vụ, gợi ý tăng hạn mức | §4.1, §4.7 | Sẵn sàng (gợi ý hạn mức: chạy theo quy tắc hiện tại, **chờ làm rõ D12** — mục E) | Gợi ý chỉ đọc; nút dẫn sang màn hạn mức cũ |
| M9 | Tạo đơn chịu: `credit-eligibility` hiện hạn dự kiến; chi tiết đơn hiện `originSeason`, `expectedDueDate` | §3.1 mục 11, 12 | Sẵn sàng | Bỏ mọi nhãn "quá hạn nên bị chặn mua chịu"; không còn lý do `OVERDUE_DEBT` |
| M10 | **Form mã lý do** (giảm / xóa / đổi hạn / ghi nợ thủ công) | §7 mục 2 | **Chờ hợp đồng** | Làm sẵn khung: trường chọn mã **tùy chọn**, danh sách mã lấy từ một hằng/cấu hình duy nhất để đổi một chỗ. **Không** đặt bắt buộc và không tự thêm/bớt mã cho tới khi Lead duyệt (câu E) và nhóm trả lời câu C |
| M11 | Biên nhận thanh toán trả nợ theo vụ (`outstandingBySeason[]`) | — | **Chờ hợp đồng** (BE chưa làm: cần L1 đồng ý sửa Payments) | Ẩn; làm sau |

## B. Farmer-Web (nông dân)

| Mã | Task | API | Trạng thái | Ghi chú |
|---|---|---|---|---|
| W1 | "Nợ của tôi": số dư và nhóm theo (vụ đang thu, hạn) | `GET /api/me/debt` | Sẵn sàng | Nhóm `collectionSeason: null` là nợ theo hạng tín dụng |
| W2 | Danh sách/chi tiết khoản nợ: vụ, hạn gốc, hạn hiện tại, số lần gia hạn, `dueState`, lịch sử hạn; lọc theo vụ | `/api/me/debt-entries` | Sẵn sàng | Không có nút "Xin gia hạn" ở giai đoạn này |
| W3 | Hạn dự kiến khi đặt hàng chịu; khiếu nại như cũ; hiển thị thông báo nhắc nợ | `GET /api/me/crop-seasons/current`, `/dispute`, thông báo | Sẵn sàng | Ghi "dự kiến" (`isEstimate`). Tin nhắc nợ chỉ có sau khi Chủ bắt đầu nhắc nợ |

## C. Flutter

| Mã | Task | Trạng thái | Ghi chú |
|---|---|---|---|
| **F1** | **Kiểm tra mã hiện có: màn/hàm nào gọi** `adjust`, `cancel`, `change-due-date`, `credit/activate`, `credit/suspend|block`, xác nhận/từ chối chuyển khoản trả nợ, `manual-entries` | **P0** | Kết quả là bảng "chỗ gọi → vai trò người dùng". Nếu nhân viên dùng được trên Flutter thì sau F3.21 sẽ nhận 403 → cần sửa hoặc ẩn. Gửi bảng cho owner L3 để trả lời **câu C** |
| F2 | Màn nợ của nông dân (tương đương W1–W3) nếu app có | Sẵn sàng | Chỉ làm sau F1 nếu app có màn nợ |

## D. Thứ tự và phối hợp với triển khai

1. Làm **M1, M2, F1** trước; trên môi trường thử kiểm `RolesPage` cả trước và sau migration.
2. Triển khai FE `RolesPage` **sát ngay sau** BE (kế hoạch ở `F3_REVIEW_PACKAGE.md` mục 9).
3. M3–M9 và W1–W3 song song, đối chiếu với dữ liệu demo.
4. M10 chỉ khung; hoàn tất sau khi có kết luận câu C và câu E.

## E. Những chỗ FE chưa được tự chốt (chờ hợp đồng được duyệt)

- Danh sách **7 mã** giảm/xóa/đổi hạn và **4 mã** gia hạn: đang chờ Lead duyệt; có thể đổi.
- Việc **bắt buộc** mã lý do và thời điểm bắt buộc: chờ câu C.
- Quy tắc gợi ý tăng hạn mức (khách không có nợ ở vụ liền trước): chờ D12.
- Biên nhận theo vụ: chờ L1 đồng ý sửa Payments.
- `debtExtension` chỉ có ở phản hồi `PUT /api/crop-seasons/{id}` có chọn áp hạn mới; mọi nơi khác là `null`/vắng.
