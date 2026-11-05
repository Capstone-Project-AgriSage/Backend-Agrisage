# Hướng dẫn FE tích hợp API — Luồng 3: Tín dụng và công nợ theo vụ mùa

Viết cho: đội FE (React) của app **Quản lý (Chủ cửa hàng, nhân viên bán hàng)** và **Nông dân (Farmer-Web)**; app Flutter cần cùng hợp đồng.
Tài liệu chỉ nói về phần **công nợ theo vụ mùa** (mới) và những chỗ nó thay đổi công nợ hiện có. Các quy ước chung (đăng nhập, token, lỗi, tiền, giờ)
giống `docs/FE_GUIDE_FLOW_1.md` §1 và §5; không nhắc lại ở đây.

> **Luồng là chuẩn, UI đi theo luồng.** Chỗ nào màn hình hiện có không khớp luồng thì UI phải sửa (§7), không đổi luồng hay API theo UI.

Nguồn sự thật: code trên nhánh `feature/f3-21-debt-permission-lock` của backend (sẽ vào `main` một lần). Hợp đồng chi tiết:
`docs/reference/api-flows/FLOW_3_CREDIT_DEBT.md` (mục 8b–8i). Swagger là bản đúng nhất; nếu khác tài liệu này, báo BE.

---

## Mục lục

0. [Đọc trước: 12 điều quan trọng](#0-đọc-trước-12-điều-quan-trọng)
1. [Khái niệm](#1-khái-niệm)
2. [Ai làm được gì](#2-ai-làm-được-gì)
3. [Các màn hình cần có](#3-các-màn-hình-cần-có)
4. [Tham chiếu API](#4-tham-chiếu-api)
5. [Lỗi và cách hiển thị](#5-lỗi-và-cách-hiển-thị)
6. [Thông báo](#6-thông-báo)
7. [Thay đổi bắt buộc ở FE hiện có](#7-thay-đổi-bắt-buộc-ở-fe-hiện-có)
8. [Chưa có hoặc chưa chốt](#8-chưa-có-hoặc-chưa-chốt)
9. [Chuẩn bị dữ liệu để chạy thử](#9-chuẩn-bị-dữ-liệu-để-chạy-thử)
10. [Phụ lục: kiểu TypeScript](#10-phụ-lục-kiểu-typescript)

---

## 0. Đọc trước: 12 điều quan trọng

1. **Hệ thống không bao giờ tự khoá hay hạn chế tín dụng của khách** (chính sách công nợ đã duyệt). Nợ quá hạn **vẫn chiếm hạn mức** nhưng khách vẫn mua chịu
   được trong hạn mức còn lại. Chỉ **Chủ** tạm ngưng/khoá/mở lại tín dụng, kèm lý do.
2. **Không có phí, lãi, phạt.** Gia hạn chỉ dời ngày trả và kỳ thu; số tiền nợ không đổi. Xoá nợ là miễn nợ thật, có lý do, lịch sử vẫn giữ.
3. **Mỗi khoản nợ có hai vụ:** `originSeason` (vụ phát sinh, **không bao giờ đổi**) và `collectionSeason` (vụ đang thu, đổi khi gia hạn). Báo cáo đọc theo vụ phát sinh;
   màn thu nợ và nhắc nợ đọc theo vụ đang thu. Đừng gộp hai thứ này thành một cột "vụ".
4. **Nợ chỉ phát sinh khi giao hoặc nhận hàng**, không phải lúc xác nhận đơn. Đơn chịu đã xác nhận nhưng chưa giao: có `originSeason` và `expectedDueDate`, **chưa có nợ**.
5. **Đơn chịu thuộc vụ nào do ngày xác nhận** (giờ Việt Nam): vụ có khoảng nhận đơn chứa ngày đó. Xác nhận lúc đang gặt thì thuộc vụ sau. Không có vụ → dùng hạn
   theo hạng tín dụng như trước (`dueRule = TERM_DAYS`).
6. **Hạn trả khi giao:** hạn của vụ; nếu giao **sau** hạn vụ thì ngày giao + số ngày hạng tín dụng (không bị quá hạn ngay ngày đầu). Cửa hàng có thể bật trần kỳ hạn.
7. **9 thao tác chỉ dành cho Chủ cửa hàng**, nhân viên không làm được dù được ủy quyền: giảm nợ, xoá nợ, ghi nợ thủ công, đổi hạn trực tiếp, xác nhận/từ chối chuyển khoản
   trả nợ, tạm ngưng/khoá/mở lại tín dụng. **FE phải ẩn các nút này với nhân viên** (§2). Server vẫn trả 403 nếu lọt.
8. **Nhân viên gia hạn được lần đầu** (khi có quyền `DEBT.ROLLOVER`); **từ lần hai chỉ Chủ**. Số lần nhân viên được làm do cửa hàng cấu hình (`staffRolloverLimit`).
9. **Vụ đã đóng không sửa, không mở lại, không nhận nợ mới.** Đóng vụ chỉ làm được khi vụ đã hết thu hoạch, không còn khoản nợ nào (kể cả đang khiếu nại) đang thu trong vụ,
   và không còn đơn chịu chưa xong của vụ. Gọi `closing-check` trước để biết còn vướng gì.
10. **`version` bắt buộc khi sửa hoặc đóng vụ và khi sửa chính sách** (`409` nếu có người khác vừa sửa → tải lại). Chính sách chưa lưu lần nào trả `isDefault: true, version: 0`.
11. **Mã lý do** (7 mã) **bắt buộc** khi giảm/xoá/đổi hạn (từ 05/11) và khi lưu vụ có "áp hạn mới cho các khoản đang thu"; **bắt buộc** cả khi gia hạn (4 mã khó khăn). Thiếu → `400`.
    Lịch sử cũ không có mã hiện `isPrePolicy: true`; hành động mới luôn có mã nên không bao giờ hiện nhãn này.
12. **Ngày** là ngày theo giờ Việt Nam, dạng `yyyy-MM-dd`; **thời điểm** trả về là UTC.

---

## 1. Khái niệm

| Từ | Nghĩa |
|---|---|
| **Vụ (crop season)** | Một mục lịch thu nợ của cửa hàng do Chủ nhập mỗi năm: `orderWindowStart` (bắt đầu nhận đơn), `harvestStart`, `harvestEnd`, `graceDays` (ân hạn). **Hạn trả của vụ = `harvestEnd + graceDays`** (`paymentDueDate`). Các vụ nối tiếp nhau không hở, không chồng. |
| **Trạng thái vụ** (`status`) | Suy ra theo ngày hôm nay, không do người dùng chọn: `PLANNED` (chưa nhận đơn) → `OPEN` (đang nhận đơn) → `HARVESTING` (đang thu hoạch, gồm cả ngày kết thúc) → `COLLECTING` (đang thu nợ) → `CLOSED` (đã đóng). |
| **Vụ phát sinh / vụ đang thu** | Xem điều 3 ở §0. Lúc đầu hai vụ giống nhau; gia hạn chỉ đổi vụ đang thu. |
| **Hạn gốc** (`originalDueDate`) | Hạn lúc khoản nợ được tạo. Tổng thời gian gia hạn tính từ hạn gốc (mặc định tối đa 365 ngày với nhân viên; Chủ vượt được nếu xác nhận). |
| **`dueState`** | `NOT_DUE`, `DUE_SOON` (đến hạn hôm nay hoặc trong 7 ngày), `OVERDUE`; `null` khi khoản nợ đã trả hết. |
| **`needsDecision`** ("Cần quyết định") | Khoản còn nợ, không đang khiếu nại, và đã quá hạn từ mốc N5 (mặc định 30 ngày) hoặc đã gia hạn đủ số lần Chủ cần xem lại (mặc định 2). Dành cho Chủ chọn: gia hạn tiếp, giảm/xoá, hoặc làm việc với khách. |
| **`dueRule`** | Cách tính hạn: `SEASON`, `CAPPED` (trần kỳ hạn), `TERM_DAYS` (theo hạng tín dụng), `MANUAL` (Chủ tự chọn ngày), `NONE` (khách chưa có tín dụng). |
| **Mốc nhắc nợ** | N1 đầu vụ thu hoạch, N2 trước hạn 7 ngày, N3 đúng hạn, N4 quá hạn 14 ngày, N5 quá hạn 30 ngày (cấu hình được). Server tự gửi thông báo; FE chỉ hiển thị. |

---

## 2. Ai làm được gì

| Thao tác | Chủ / Admin | Nhân viên bán hàng | Nông dân |
|---|---|---|---|
| Xem vụ, xem khoản nợ, xem chính sách | Có | Có (khi được cấp quyền đọc) | Chỉ nợ của mình |
| Tạo/sửa vụ, đóng vụ, sửa chính sách | **Chỉ Chủ** | Không (ẩn nút) | Không |
| Gia hạn một khoản / cả vụ | Có, không giới hạn lần | **Lần đầu**, cần quyền `DEBT.ROLLOVER`; từ lần hai → 403 | Không |
| Giảm nợ, xoá nợ, đổi hạn trực tiếp, ghi nợ thủ công | **Chỉ Chủ** | Không (ẩn nút) | Không |
| Xác nhận / từ chối chuyển khoản trả nợ | **Chỉ Chủ** (kể cả khoản do chính mình ghi) | Chỉ **ghi nhận** (khoản chờ) | Không |
| Tạm ngưng / khoá / mở lại tín dụng | **Chỉ Chủ** | Không (ẩn nút) | Không |
| Thu tiền mặt, bán chịu trong hạn mức, khiếu nại giữ nguyên số nợ | Có | Có | — |
| Báo cáo nợ theo vụ, gợi ý hạn mức, bảng điều khiển công nợ | Có | Chỉ khi được cấp `DEBT.READ_DASHBOARD` | Không |
| Khiếu nại khoản nợ của mình | — | — | Có |

Cách ẩn nút: dựa vào `role` trong token (và danh sách quyền hiệu lực của người dùng nếu màn phân quyền đã có). Quyền nhân viên có được là **tối đa** các quyền ủy quyền
được; các mã chỉ-Chủ không xuất hiện trong danh sách quyền có thể cấp (`grantablePermissions`).

---

## 3. Các màn hình cần có

### 3.1 Quản lý (Chủ và nhân viên)

1. **Danh sách vụ mùa** (`GET /api/crop-seasons`) — cột: mã, tên, khoảng nhận đơn, thu hoạch, hạn trả, `status`, nợ phát sinh (`originated`) và nợ đang thu (`collecting`).
   Chủ có nút **Thêm vụ** (chỉ thêm sau vụ cuối: ngày bắt đầu nhận đơn được server điền, vụ đầu tiên nhập tay) và **Sửa**.
   Sửa ngày thu hoạch của vụ có thể dời vụ kế (server tự dời `orderWindowStart` của vụ sau); hộp thoại cần cảnh báo điều đó. Tuỳ chọn "áp hạn mới cho các khoản đang thu"
   (`applyNewDueDateToCollectingDebts`, bắt buộc `reason`) chỉ dời muộn hơn, chỉ khoản còn theo hạn cũ của vụ và **không đụng khoản đang khiếu nại** (Chủ quyết riêng). Phản hồi `200` của lần cập nhật đó có `debtExtension: { moved, skippedDisputed }` — hiển thị "đã dời N khoản; M khoản đang khiếu nại giữ hạn cũ, cần Chủ quyết".
2. **Chính sách công nợ** (`GET/PUT /api/debt-policy`) — Chủ sửa: ân hạn mặc định, mốc nhắc N2/N4/N5, số lần nhân viên được gia hạn, số lần gia hạn thì Chủ xem lại, số vụ đúng hạn
   để gợi ý tăng hạn mức, trần kỳ hạn (để trống = tắt), tổng thời gian gia hạn tối đa. Đổi ân hạn chỉ áp cho **vụ tạo sau**.
3. **Danh sách khoản nợ** — thêm cột **vụ phát sinh**, **vụ đang thu**, **hạn gốc**, **số lần gia hạn** (biểu tượng "đã gia hạn" khi `isRolledOver`), badge `dueState`, đánh dấu
   `needsDecision`. Bộ lọc mới: vụ phát sinh, vụ đang thu, `dueState`, "chỉ khoản đã gia hạn", "cần quyết định".
4. **Chi tiết khoản nợ** — thêm khối **lịch sử hạn trả**: mỗi `action` có loại, mã lý do (`reasonCode`; `isPrePolicy` hiện nhãn "Trước khi áp dụng chính sách"), người làm
   (`actorName` + `actorRole`), hạn cũ → hạn mới, vụ cũ → vụ mới. Nút **Gia hạn** (hộp thoại: chọn vụ đích, mã lý do trong 4 mã khó khăn, ghi chú bắt buộc). Chủ thêm nút
   Giảm / Xoá / Đổi hạn với **chọn mã lý do** (7 mã) và ghi chú tự nhập.
5. **Gia hạn cả vụ** (từ màn chi tiết vụ) — bước 1 gọi với `dryRun: true` để hiện danh sách **sẽ gia hạn** và **bị bỏ qua + lý do** (`skipped[].reason`, §4.6); bước 2 xác nhận gọi
   lại với `dryRun: false`. Có thể chọn từng khoản (`entryIds`, tối đa 500).
6. **Cần quyết định** (Chủ) — danh sách `needsDecision` trong bảng điều khiển hoặc lọc `needsDecision=true`, mỗi dòng có hành động: gia hạn tiếp, giảm/xoá, xem khách.
7. **Đóng vụ** — gọi `closing-check`; nếu `canClose` thì nút **Đóng vụ** (có ghi chú), nếu không thì hiển thị `reasons` kèm danh sách `openEntries` / `openCreditOrders`
   (mỗi danh sách tối đa 50, `…Count` là tổng) để Chủ xử lý (gia hạn khoản nợ, huỷ/hoàn tất đơn) rồi kiểm tra lại. Sau khi đóng, vụ chỉ đọc.
8. **Báo cáo nợ theo vụ** (`GET /api/reports/debt-by-season`) — mỗi vụ hai khối: **theo vụ phát sinh** (phát sinh, đã thu, trả hàng, giảm, xoá, **chuyển sang vụ sau**, còn nợ; luôn
   thoả phát sinh − thu − trả hàng − giảm − xoá = còn nợ) và **theo vụ đang thu** (nợ chuyển đến, còn phải thu). Vụ đã đóng (`isFrozen`) hiện số liệu lúc đóng; chú thích
   "số liệu chốt khi đóng vụ".
9. **Gợi ý tăng hạn mức** (`GET /api/crop-seasons/{id}/limit-suggestions`, vụ đã đóng) — danh sách khách đúng hạn liên tiếp (bỏ qua vụ khách không nợ; xếp theo số vụ đúng hạn liên tiếp `onTimeSeasons` giảm dần rồi tên; phân trang `page`/`pageSize`, mặc định 20); nút dẫn sang màn sửa hạn mức **hiện có**
   (API hạn mức cũ). Server không tự đổi hạn mức.
10. **Bảng điều khiển công nợ** (`GET /api/debt-entries/dashboard`) — thêm: vụ hiện tại và nợ đang thu trong vụ, tổng nợ đến hạn trong 7 ngày, danh sách "Cần quyết định",
    **khách sắp/đã hết hạn mức** (`creditLimitAlerts`, `NEAR_LIMIT` từ 90% hạn mức, `AT_LIMIT` khi hết). Với khách hết hạn mức Chủ chọn: nâng hạn mức, giảm/xoá nợ, hoặc thu một phần.
11. **Kiểm tra tín dụng khi tạo đơn chịu** (`POST /api/customers/{id}/credit-eligibility`) — hiển thị `expectedCropSeason`, `expectedDueDate` ("Dự kiến trả ngày …", ghi rõ là
    **dự kiến**, hạn thật tính khi giao) và `dueRule`. Vượt hạn mức → `reasonCode = INSUFFICIENT_CREDIT` kèm hạn mức còn lại; **không còn** lý do `OVERDUE_DEBT`.
12. **Chi tiết đơn** — đơn chịu đã xác nhận có `originSeason` và `expectedDueDate` (chỉ khi còn giao được).

### 3.2 Nông dân

1. **Nợ của tôi** (`GET /api/me/debt`) — `currentBalance` và `groups[]`: nợ đang còn gộp theo **(vụ đang thu, hạn trả)**, mỗi nhóm có số tiền, số khoản, `dueState`.
   Nhóm không có vụ (`collectionSeason: null`) là nợ theo hạng tín dụng.
2. **Danh sách và chi tiết khoản nợ** (`/api/me/debt-entries`) — hiển thị vụ, hạn gốc, hạn hiện tại, số lần gia hạn, `dueState`, lịch sử hạn. Lọc theo vụ đang thu.
3. **Hạn dự kiến khi đặt hàng chịu** (`GET /api/me/crop-seasons/current`) — vụ hiện tại, `expectedDueDate`, `dueRule`, `isEstimate: true`. Hiển thị "Hạn trả dự kiến …" trước khi đặt.
4. **Khiếu nại** khoản nợ như cũ. Khiếu nại không đổi số nợ, không chặn thanh toán.
5. **Thông báo** (§6).

> Nông dân **không** có nút "Xin gia hạn" ở giai đoạn này. Muốn gia hạn, nông dân liên hệ cửa hàng và nhân viên/Chủ gia hạn trực tiếp.

---

## 4. Tham chiếu API

Tất cả đường dẫn bắt đầu bằng `/api`. "Quản lý" = ADMIN, STORE_OWNER (và nhân viên khi có quyền tương ứng, xem §2).

### 4.1 Vụ mùa

| Method | Route | Ai | Ghi chú |
|---|---|---|---|
| GET | `/crop-seasons?year=&status=&page=&pageSize=` | nhân viên trở lên | `status`: `PLANNED`, `OPEN`, `HARVESTING`, `COLLECTING`, `CLOSED` |
| GET | `/crop-seasons/{id}` | nhân viên trở lên | |
| POST | `/crop-seasons` | **Chủ** | `{ code, name, harvestStart, harvestEnd, graceDays?, orderWindowStart?, cropType?, seasonalRuleId? }`; `orderWindowStart` chỉ cho vụ đầu |
| PUT | `/crop-seasons/{id}` | **Chủ** | `{ name, harvestStart, harvestEnd, graceDays, version, seasonalRuleId?, applyNewDueDateToCollectingDebts?, reason?, reasonCode? }` (`reasonCode` bắt buộc khi `applyNewDueDateToCollectingDebts`) |
| GET | `/crop-seasons/{id}/closing-check` | nhân viên trở lên | `{ canClose, reasons[], openEntryCount, openEntries[], openCreditOrderCount, openCreditOrders[] }` |
| POST | `/crop-seasons/{id}/close` | **Chủ** | `{ version, note? }` → vụ với `status: "CLOSED"` |
| GET | `/crop-seasons/{id}/limit-suggestions` | quản lý | `422` nếu vụ chưa đóng |
| GET | `/me/crop-seasons/current` | nông dân | `{ cropSeasonId?, code?, name?, harvestStart?, harvestEnd?, expectedDueDate?, dueRule, termDays?, isEstimate }` |

`reasons` của `closing-check`: `ALREADY_CLOSED`, `HARVEST_NOT_ENDED`, `OPEN_ENTRIES`, `OPEN_CREDIT_ORDERS`.

### 4.2 Chính sách công nợ

| Method | Route | Ai | Ghi chú |
|---|---|---|---|
| GET | `/debt-policy` | nhân viên trở lên | chưa lưu → `isDefault: true`, `version: 0` |
| PUT | `/debt-policy` | **Chủ** | gửi **toàn bộ** chính sách + `version` (+ `reason?`) |

```json
{
  "defaultGraceDays": 30, "reminderOffsets": { "n2": -7, "n4": 14, "n5": 30 },
  "staffRolloverLimit": 1, "ownerReviewAfterRollovers": 2, "onTimeSeasonsForLimitRaise": 2,
  "maxCreditTermDays": null, "maxTotalExtensionDays": 365, "version": 0, "isDefault": true,
  "remindersStartDate": null
}
```

Luật (sai → `400`): ân hạn 0–365; `n2 < 0 < n4 < n5`; `staffRolloverLimit ≥ 0`; `ownerReviewAfterRollovers ≥ max(staffRolloverLimit, 1)`; `onTimeSeasonsForLimitRaise ≥ 1`;
`maxCreditTermDays` để trống hoặc ≥ 1; `maxTotalExtensionDays ≥ 1` (bắt buộc).

**Bắt đầu nhắc nợ** (chỉ Chủ). Server **không gửi nhắc nợ nào** cho tới khi Chủ bắt đầu; `remindersStartDate = null` nghĩa là chưa bắt đầu — màn chính sách nên hiện cảnh báo
"Nhắc nợ chưa bắt đầu" và nút **Bắt đầu nhắc nợ** (chỉ hiện cho Chủ).

| Method | Route | Ghi chú |
|---|---|---|
| POST | `/debt-reminders/initialize` | **Chủ**. Body `{ "dryRun": true }` (mặc định) chỉ đếm, **không ghi, không gửi thông báo**; `{ "dryRun": false, "reason"?: "…" }` bắt đầu thật |

Luồng màn hình: gọi `dryRun: true` → hiện hộp xác nhận "Sẽ bắt đầu nhắc nợ từ hôm nay (`startDate`). Có `skippedTotal` mốc nhắc cũ sẽ được bỏ qua, không gửi bù;
các khoản nợ đó vẫn hiện đầy đủ trên màn công nợ" (chi tiết theo `skipped.N1…N5`; `groupsWithLaterMilestones` là số nhóm nợ sẽ được nhắc từ nay) → Chủ đồng ý → gọi `dryRun: false`.
Ngày bắt đầu (T0) là **hôm nay theo giờ Việt Nam**, đặt **một lần**, không đổi được. Gọi lại → `alreadyStarted: true`, không có gì thay đổi (nút nên ẩn khi `remindersStartDate` đã có).
Khoản nợ cũ đã qua mốc N5 trước ngày bắt đầu sẽ không được nhắc tự động — Chủ xem ở mục "Cần quyết định" và nhắc riêng.

```json
{ "dryRun": true, "alreadyStarted": false, "startDate": "2026-11-03", "farmers": 12, "activeGroups": 31,
  "skipped": { "N1": 0, "N2": 0, "N3": 2, "N4": 5, "N5": 9 }, "skippedTotal": 16, "groupsWithLaterMilestones": 14 }
```

### 4.3 Khoản nợ (staff)

| Method | Route | Ghi chú |
|---|---|---|
| GET | `/debt-entries` | thêm bộ lọc `originSeasonId`, `collectionSeasonId`, `dueState`, `rolledOverOnly`, `needsDecision` |
| GET | `/debt-entries/{id}` | thêm `originSeason`, `collectionSeason`, `originalDueDate`, `rolloverCount`, `isRolledOver`, `dueState`, `needsDecision`; mỗi action thêm `reasonCode`, `isPrePolicy`, `oldCropSeasonId`, `newCropSeasonId`, `actorName`, `actorRole` |
| GET | `/customers/{id}/debt` | thêm `groups[]` |
| GET | `/debt-entries/dashboard` | thêm `currentSeason`, `currentSeasonOutstanding`, `dueSoonAmount`, `needsDecisionCount`, `needsDecision[]`, `creditLimitAlertCount`, `creditLimitAlerts[]` |
| POST | `/debt-entries/{id}/rollover` | `{ reasonCode, note, targetCropSeasonId?, overrideExtensionLimit? }` |
| POST | `/debt-entries/{id}/adjust` | **Chủ**, `{ amount, reason, reasonCode }` (bắt buộc) |
| POST | `/debt-entries/{id}/cancel` | **Chủ**, `{ reason, reasonCode }` (bắt buộc) |
| POST | `/debt-entries/{id}/change-due-date` | **Chủ**, `{ newDueDate, reason, reasonCode }` (bắt buộc); không lùi về quá khứ; không tính là gia hạn |
| POST | `/customers/{id}/debt/manual-entries` | **Chủ**, `{ amount, dueDate?, reason, cropSeasonId? }`; có `cropSeasonId` thì có thể bỏ `dueDate` (lấy hạn vụ) |

### 4.4 Khoản nợ (nông dân)

| Method | Route |
|---|---|
| GET | `/me/debt` (có `groups[]`) |
| GET | `/me/debt-entries?collectionSeasonId=&originSeasonId=&dueState=&status=` và `/{id}` |
| POST | `/me/debt-entries/{id}/dispute` |

### 4.5 Gia hạn một khoản

`POST /debt-entries/{id}/rollover` → trả lại khoản nợ (`DebtEntryResponse`).

- `reasonCode`: **chỉ** `MAT_MUA`, `THIEN_TAI`, `KHO_KHAN`, `KHAC`. `note` bắt buộc. Thiếu hoặc sai → `400`.
- Không gửi `targetCropSeasonId`: server chọn vụ kế tiếp còn mở có hạn trả muộn hơn hạn hiện tại. Hạn mới = hạn trả của vụ đích.
- Chỉ đổi vụ đang thu, hạn và `rolloverCount`; số tiền, trạng thái, vụ phát sinh, hạn gốc giữ nguyên, không có dòng sổ nợ mới.
- Nhân viên đã hết số lần được gia hạn → `403`. Tổng thời gian gia hạn vượt mức → `422`; Chủ vượt được khi gửi `overrideExtensionLimit: true` (nên hỏi xác nhận trước).

### 4.6 Gia hạn cả vụ

`POST /crop-seasons/{id}/debt/rollover` (`{id}` là vụ đang thu nợ), body `{ reasonCode, note, targetCropSeasonId?, entryIds?, dryRun?, overrideExtensionLimit? }`.

```json
{
  "sourceSeasonId": "…", "targetSeason": { "id": "…", "code": "HT-2027", "name": "Hè Thu 2027" },
  "newDueDate": "2027-09-14", "dryRun": true,
  "rolledOver": [ { "entryId": "…", "entryNumber": "DE-…", "farmerProfileId": "…", "oldDueDate": "2027-04-14", "newDueDate": "2027-09-14", "outstandingAmount": 2000000 } ],
  "skipped": [ { "entryId": "…", "entryNumber": "DE-…", "reason": "DISPUTED" } ]
}
```

Lý do bỏ qua (`skipped[].reason`): `NOT_FOUND`, `NOT_IN_SEASON`, `NOTHING_OWED`, `DISPUTED` (đang khiếu nại), `NOT_LATER`, `NEEDS_OWNER` (nhân viên hết lượt, cần Chủ),
`EXCEEDS_TOTAL_EXTENSION`. Gọi lần hai (không truyền `entryIds`) thì cả `rolledOver` và `skipped` rỗng vì vụ nguồn không còn khoản nào đang thu; `NOT_IN_SEASON` chỉ có khi truyền `entryIds` chứa khoản không thuộc vụ nguồn. `dryRun: true` không ghi gì.

### 4.7 Báo cáo

`GET /reports/debt-by-season?year=&page=&pageSize=` (quản lý). Mỗi phần tử: `season`, `status`, `isFrozen`, `origin { originatedAmount, collectedAmount, returnedAmount, reducedAmount, cancelledAmount, carriedOverAmount, outstandingAmount, entryCount, customerCount }`,
`collection { carriedInAmount, toCollectAmount, openEntryCount }`.

---

## 5. Lỗi và cách hiển thị

| Mã | Khi nào | Hiển thị |
|---|---|---|
| `400` | thiếu/sai trường (mã lý do, ghi chú, ngày, số) | gắn lỗi vào ô tương ứng (`errors`) |
| `401` | hết phiên | về trang đăng nhập |
| `403` | thao tác chỉ-Chủ, hoặc nhân viên hết lượt gia hạn | "Chỉ Chủ cửa hàng mới thực hiện được thao tác này" / "Cần Chủ quyết định gia hạn tiếp" |
| `404` | không thấy (kể cả khoản nợ của người khác) | "Không tìm thấy" |
| `409` | `version` cũ | "Dữ liệu vừa được người khác sửa, đã tải lại" rồi cho thử lại |
| `422` | quy tắc nghiệp vụ: vụ hở/chồng, vụ đã đóng, chưa đóng được, vượt tổng thời gian gia hạn, không có vụ sau, khoản đang khiếu nại, vượt hạn mức… | hiển thị `detail` của server |

Mã lý do hợp lệ (7 mã): `MAT_MUA` (mất mùa), `THIEN_TAI` (thiên tai), `KHO_KHAN` (khó khăn gia đình), `TRI_AN` (tri ân), `SAI_SOT` (cửa hàng ghi sai),
`KHONG_THU_HOI` (không thu hồi được), `KHAC` (khác). **Gia hạn chỉ nhận 4 mã:** `MAT_MUA`, `THIEN_TAI`, `KHO_KHAN`, `KHAC`; giảm/xoá/đổi hạn nhận cả 7.
Nhãn tiếng Việt do FE đặt; server trả mã.

---

## 6. Thông báo

Nông dân nhận thông báo (đã có màn thông báo; thêm các `notificationType` mới để hiển thị đúng biểu tượng):

| Loại | Khi nào |
|---|---|
| `DEBT_DUE_DATE_CHANGED` | gia hạn hoặc Chủ đổi hạn |
| `DEBT_REDUCED` | Chủ giảm nợ |
| `DEBT_CANCELLED` | Chủ xoá nợ |
| `DEBT_DISPUTE_RESOLVED` | cửa hàng giữ nguyên số nợ sau khiếu nại |
| `DEBT_DUE_SOON`, `DEBT_OVERDUE` | nhắc nợ theo mốc N1–N5 (không còn nhắc mỗi ngày); tin tổng hợp cho Chủ/nhân viên ghi "N nhóm nợ" (một khách có thể có nhiều nhóm theo hạn) |

Nhân viên/Chủ nhận bản tóm tắt: mỗi ngày tối đa một tin cho mốc N3 và N4 (nhân viên và Chủ), một tin cho mốc N5 (chỉ Chủ).

---

## 7. Thay đổi bắt buộc ở FE hiện có

1. **Ẩn nút chỉ-Chủ với nhân viên** (§2): giảm/xoá nợ, ghi nợ thủ công, đổi hạn, xác nhận/từ chối chuyển khoản trả nợ, tạm ngưng/khoá/mở lại tín dụng, đổi `allowCreditPurchase`.
   Trước đây `change-due-date` cho nhân viên; nay chỉ Chủ.
2. **`Management-Webs/src/api/debtApi.ts`** (`changeDueDate`, `adjust`, `cancel`) và form lưu vụ mùa có "áp hạn mới": thêm `reasonCode` và chọn mã lý do trên giao diện. **Server từ chối nếu thiếu mã (400)**;
   ghi nợ thủ công không cần mã.
3. **Màn phân quyền**: các mã chỉ-Chủ không còn cấp được cho nhân viên. `MemberPermissionEditor` chỉ gửi mã trong `grantablePermissions` nên không cần đổi; `RolesPage` trên FE `main` đã lọc theo `allowedRoles` nên cũng không cần đổi (chưa kiểm bằng trình duyệt: thử một lần lưu vai trò Sale, không được báo 403/422).
4. **Bỏ nhãn "Quá hạn nên bị chặn mua chịu"** nếu có: nợ quá hạn không chặn mua chịu (chỉ báo `hasOverdueDebt`).
5. **`Management-Webs/src/api/cropSeasonsApi.ts`** là hợp đồng đề xuất cũ: đổi `outstandingAmount`/`openEntryCount` thành nhóm `originated` và `collecting`, đổi
   `applyNewDueDateToDebts` thành `applyNewDueDateToCollectingDebts`, và dùng `originSeason`/`collectionSeason` ở khoản nợ.
6. **Farmer-Web `debtApi.ts`** chỉ đọc và khiếu nại nên không bị ảnh hưởng; thêm các trường mới khi muốn hiển thị.

---

## 8. Chưa có hoặc chưa chốt

- **Biên nhận thanh toán theo vụ**: chi tiết thanh toán trả nợ (`DEBT_REPAYMENT`) có `outstandingBySeason[]` = `{ seasonId, seasonCode, seasonName, paidAmount, outstandingNow }` mỗi vụ đang thu (vụ null = nợ không thuộc vụ nào, xếp cuối). `paidAmount` = lần trả này đã trả cho vụ đó; `outstandingNow` = khách còn nợ ở vụ đó **tại lúc đọc (không phải ngay sau lần trả)**. Thanh toán loại khác: `null`.
- **Mã lý do bắt buộc** khi giảm/xoá/đổi hạn và khi lưu vụ có áp hạn mới (từ 05/11, lead đã đồng ý bắt buộc nếu form web đổi cùng đợt; Flutter không gọi các API này theo lời lead, chưa kiểm). Gửi thiếu mã → `400`.
- **Yêu cầu xin gia hạn của nông dân:** bỏ ở giai đoạn này.
- Hai danh sách mã lý do (7 mã, và 4 mã cho gia hạn) đang chờ lead duyệt; nếu đổi sẽ báo FE.
- Triển khai: 4 migration mới phải chạy **cùng lúc** với code backend. **Bắt đầu nhắc nợ là bước cuối**, sau khi cửa hàng đã nhập vụ (xem mục "Bắt đầu nhắc nợ").

---

## 9. Chuẩn bị dữ liệu để chạy thử

Hiện **chưa có seed dữ liệu demo** cho công nợ theo vụ. Tạo bằng API (chi tiết từng bước và kết quả cần thấy: `docs/F3_SEASON_SWAGGER_TEST.md`):

1. Đăng nhập Chủ. `POST /crop-seasons` vụ đầu **có** `orderWindowStart`, rồi các vụ sau (không gửi `orderWindowStart`).
2. `PUT /debt-policy` nếu muốn đổi mặc định.
3. Có một khách có hồ sơ tín dụng đang hoạt động (luồng khách hàng/tín dụng); tạo đơn chịu, xác nhận, giao.
4. Xem `GET /debt-entries/{id}` để thấy vụ và hạn, rồi thử gia hạn, đóng vụ, báo cáo.

Kịch bản đầy đủ "nông dân Bảy" chạy bằng đồng hồ giả trong test `FarmerBayScenarioDatabaseTests` — dùng làm tài liệu số liệu mẫu.

---

## 10. Phụ lục: kiểu TypeScript

```ts
type DueState = 'NOT_DUE' | 'DUE_SOON' | 'OVERDUE';
type DueRule = 'SEASON' | 'CAPPED' | 'TERM_DAYS' | 'MANUAL' | 'NONE';
type SeasonStatus = 'PLANNED' | 'OPEN' | 'HARVESTING' | 'COLLECTING' | 'CLOSED';
type DebtReasonCode = 'MAT_MUA' | 'THIEN_TAI' | 'KHO_KHAN' | 'TRI_AN' | 'SAI_SOT' | 'KHONG_THU_HOI' | 'KHAC';
type RolloverReasonCode = 'MAT_MUA' | 'THIEN_TAI' | 'KHO_KHAN' | 'KHAC';

interface SeasonRef { id: string; code: string; name: string }
interface DebtFigures { outstandingAmount: number; openEntryCount: number }

interface CropSeason {
  id: string; code: string; name: string; cropType: string;
  orderWindowStart: string; harvestStart: string; harvestEnd: string; graceDays: number;
  paymentDueDate: string; status: SeasonStatus; seasonalRuleId: string | null; closedAt: string | null;
  version: number; originated: DebtFigures; collecting: DebtFigures;
  debtExtension?: { moved: number; skippedDisputed: number } | null;   // chỉ có ở phản hồi PUT có applyNewDueDateToCollectingDebts
}

interface DebtEntryListItem {
  id: string; entryNumber: string; farmerProfileId: string; fullName: string; phoneNumber: string | null;
  sourceType: string; orderNumber: string | null; originalAmount: number; totalPaid: number; outstandingAmount: number;
  dueDate: string; isOverdue: boolean; overdueDays: number; status: string; createdAt: string;
  originSeason: SeasonRef | null; collectionSeason: SeasonRef | null; originalDueDate: string;
  rolloverCount: number; isRolledOver: boolean; dueState: DueState | null; needsDecision: boolean;
}

interface DebtAction {
  id: string; actionType: string; reason: string; adjustmentAmount: number | null;
  oldDueDate: string | null; newDueDate: string | null; createdBy: string; createdAt: string;
  reasonCode: DebtReasonCode | null; isPrePolicy: boolean;
  oldCropSeasonId: string | null; newCropSeasonId: string | null; actorName: string | null; actorRole: string | null;
}

interface DebtCollectionGroup { collectionSeason: SeasonRef | null; dueDate: string; outstandingAmount: number; entryCount: number; dueState: DueState }

interface DebtPolicy {
  defaultGraceDays: number; reminderOffsets: { n2: number; n4: number; n5: number };
  staffRolloverLimit: number; ownerReviewAfterRollovers: number; onTimeSeasonsForLimitRaise: number;
  maxCreditTermDays: number | null; maxTotalExtensionDays: number; version: number; isDefault: boolean;
  remindersStartDate: string | null;   // null = nhắc nợ chưa bắt đầu (job không gửi gì)
}

interface RolloverRequest { reasonCode: RolloverReasonCode; note: string; targetCropSeasonId?: string; overrideExtensionLimit?: boolean }
interface SeasonRolloverRequest extends RolloverRequest { entryIds?: string[]; dryRun?: boolean }
type SkipReason = 'NOT_FOUND' | 'NOT_IN_SEASON' | 'NOTHING_OWED' | 'DISPUTED' | 'NOT_LATER' | 'NEEDS_OWNER' | 'EXCEEDS_TOTAL_EXTENSION';
interface SeasonRolloverResponse {
  sourceSeasonId: string; targetSeason: SeasonRef; newDueDate: string; dryRun: boolean;
  rolledOver: { entryId: string; entryNumber: string; farmerProfileId: string; oldDueDate: string; newDueDate: string; outstandingAmount: number }[];
  skipped: { entryId: string; entryNumber: string | null; reason: SkipReason }[];
}

interface ClosingCheck {
  seasonId: string; status: SeasonStatus; canClose: boolean;
  reasons: ('ALREADY_CLOSED' | 'HARVEST_NOT_ENDED' | 'OPEN_ENTRIES' | 'OPEN_CREDIT_ORDERS')[];
  openEntryCount: number; openEntries: { entryId: string; entryNumber: string; farmerProfileId: string; fullName: string; outstandingAmount: number; status: string }[];
  openCreditOrderCount: number; openCreditOrders: { orderId: string; orderNumber: string; status: string }[];
}

interface DebtBySeason {
  season: SeasonRef; harvestStart: string; paymentDueDate: string; status: SeasonStatus; closedAt: string | null; isFrozen: boolean;
  origin: { originatedAmount: number; collectedAmount: number; returnedAmount: number; reducedAmount: number; cancelledAmount: number;
            carriedOverAmount: number; outstandingAmount: number; entryCount: number; customerCount: number };
  collection: { carriedInAmount: number; toCollectAmount: number; openEntryCount: number };
}

interface CreditLimitAlert {
  farmerProfileId: string; fullName: string; phoneNumber: string | null; creditLimit: number; usedCredit: number; availableCredit: number;
  state: 'NEAR_LIMIT' | 'AT_LIMIT';
}

interface CreditEligibility { /* các trường cũ */ expectedCropSeason: SeasonRef | null; expectedDueDate: string | null; dueRule: DueRule | null }
interface OrderSeasonFields { originSeason: SeasonRef | null; expectedDueDate: string | null }
```
