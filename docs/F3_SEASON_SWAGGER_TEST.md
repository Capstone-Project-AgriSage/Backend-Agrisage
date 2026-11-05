# F3 — Test công nợ theo vụ mùa bằng Swagger

Mở http://localhost:5206/swagger/index.html (hoặc `https://localhost:7068/swagger`), đăng nhập qua API Auth và **Authorize** bằng access token.
Cần 3 tài khoản: **Chủ cửa hàng** (STORE_OWNER hoặc ADMIN), **nhân viên bán hàng** (SALES_STAFF) và một **nông dân** có hồ sơ tín dụng đang hoạt động.
Hợp đồng chi tiết: `docs/reference/api-flows/FLOW_3_CREDIT_DEBT.md` (mục 8b–8i). Hướng dẫn cho FE: `docs/FE_GUIDE_FLOW_3.md`.

> **Ngày giả:** Swagger chạy theo ngày thật của máy chủ nên không giả lập được ngày. Các mốc nhắc nợ (N1–N5) và việc "vụ đã hết thu hoạch" phụ thuộc vào ngày hôm nay;
> kịch bản đầy đủ với đồng hồ giả chạy tự động ở test `FarmerBayScenarioDatabaseTests`. Dưới đây dùng ngày **tương đối** so với hôm nay (`T`).

## 0. Chuẩn bị dữ liệu

**Cách nhanh (chỉ DB cục bộ):** sau khi đã chạy `--seed`, chạy lệnh bảo trì `dotnet run --project src/AgriSage.Api --no-launch-profile -- --seed-debt-demo` với biến môi trường `DebtDemo__Password` (≥ 8 ký tự, tự đặt, không in ra) và kết nối DB cục bộ (`Database__Host=localhost`). Lệnh **từ chối** nếu `Database:Host` không phải localhost/127.0.0.1/::1 hoặc môi trường là Production, và chạy lại không đổi gì. Bộ dữ liệu: Chủ `demo.owner@agrisage.demo`, nhân viên `demo.staff@agrisage.demo`; hai vụ (`DEMO-A` đang thu và đã qua hạn, `DEMO-B` đang nhận đơn); bốn khách có nợ: bình thường (3 triệu, vụ B), quá hạn (5 triệu, vụ A), đang khiếu nại (2 triệu, vụ A), đã gia hạn A→B (4 triệu). Không bắt đầu nhắc nợ và không gửi thông báo. Đăng nhập bằng mật khẩu bạn đặt.**Cách thủ công:** tạo bằng API, bằng tài khoản **Chủ**:

**Cách thủ công** (nếu không dùng bộ demo): tạo bằng API, bằng tài khoản **Chủ**:

1. `GET /api/debt-policy` → `isDefault: true`, `version: 0`, `defaultGraceDays: 30`. (Chưa cần lưu.)
2. `POST /api/crop-seasons` — **vụ đầu** (phải có `orderWindowStart`). Với `T` là hôm nay:

```json
{ "code": "DX", "name": "Đông Xuân", "orderWindowStart": "T-60", "harvestStart": "T+30", "harvestEnd": "T+60", "graceDays": 30 }
```

   Kết quả `201`: `status: "OPEN"`, `paymentDueDate = harvestEnd + 30`, `version: 0`.
3. `POST /api/crop-seasons` — **vụ sau** (không gửi `orderWindowStart`; server lấy `harvestStart` của vụ trước):

```json
{ "code": "HT", "name": "Hè Thu", "harvestStart": "T+210", "harvestEnd": "T+240", "graceDays": 30 }
```

   Kết quả: `status: "PLANNED"`, `orderWindowStart` = `harvestStart` của DX.
4. Thử sai để thấy luật: gửi vụ thứ ba với `orderWindowStart` khác `harvestStart` của vụ trước → `422`; trùng `code` → `409`.

Cần thêm: khách có hồ sơ tín dụng `ACTIVE`, hạng có `defaultPaymentTermDays` (ví dụ 30), một sản phẩm có tồn kho và bảng giá.

## 1. Đơn chịu gắn vào vụ

Bằng tài khoản **Chủ hoặc nhân viên**:

1. `POST /api/customers/{farmerProfileId}/credit-eligibility` `{ "orderAmount": 1000000 }` → `eligible: true`, `expectedCropSeason.code = "DX"`,
   `expectedDueDate = paymentDueDate của DX`, `dueRule: "SEASON"`.
2. Tạo đơn `CREDIT` cho khách (luồng đơn hàng), rồi `POST /api/orders/{id}/confirm`. Phản hồi có `originSeason.code = "DX"` và `expectedDueDate`.
3. `GET /api/debt-entries?orderId={id}` → **chưa có khoản nợ** (nợ chỉ phát sinh khi giao).
4. Giao/nhận hàng (`POST /api/orders/{id}/pickup` hoặc luồng giao hàng). Sau đó `GET /api/debt-entries?orderId={id}`:
   - `originSeason` = `collectionSeason` = DX, `dueDate` = `originalDueDate` = hạn của DX, `rolloverCount: 0`, `dueState: "NOT_DUE"`.
5. Đối chiếu số: `GET /api/customers/{farmerProfileId}/debt` → `currentBalance` bằng tổng `outstandingAmount`; `groups[0]` có `collectionSeason.code = "DX"`.

**Hạn khi giao muộn (cách B):** nếu giao sau `paymentDueDate` của vụ thì `dueDate` = ngày giao + số ngày hạng tín dụng (không phải quá hạn ngay).
Muốn thấy, tạo một vụ cũ đã qua hạn rồi giao đơn thuộc vụ đó.

## 2. Gia hạn một khoản (nhân viên rồi Chủ)

1. Tài khoản **nhân viên** (đã được cấp `DEBT.ROLLOVER`): `POST /api/debt-entries/{id}/rollover`

```json
{ "reasonCode": "MAT_MUA", "note": "Sâu cuốn lá, mất mùa" }
```

   `200`: `collectionSeason.code = "HT"`, `dueDate` = hạn của HT, `originalDueDate` và `originSeason` **không đổi**, `rolloverCount: 1`, `outstandingAmount` không đổi.
   `GET /api/debt-entries/{id}` → `actions[0].actionType = "ROLLOVER"`, có `reasonCode`, `actorName`, `actorRole`, vụ cũ/mới.
2. Cùng nhân viên gia hạn lần hai → `403`. Chủ làm được (cần có vụ sau HT; nếu không → `422 "No open season has a later due date"`).
3. Sai đầu vào: `reasonCode: "TRI_AN"` → `400`; thiếu `note` → `400`; khoản đang khiếu nại (`POST …/dispute`) → `422`.
4. **Vượt tổng thời gian gia hạn:** đặt `maxTotalExtensionDays` nhỏ (ví dụ 30) bằng `PUT /api/debt-policy`; nhân viên gia hạn quá mức → `422`; Chủ phải gửi
   `"overrideExtensionLimit": true`.
5. Kiểm tra sổ nợ không đổi: `GET /api/debt-entries/{id}/ledger` có đúng số dòng như trước khi gia hạn.

## 3. Gia hạn cả vụ

`POST /api/crop-seasons/{idDX}/debt/rollover` với `"dryRun": true` trước:

```json
{ "reasonCode": "THIEN_TAI", "note": "Lũ", "dryRun": true }
```

`200` liệt kê `rolledOver[]` (sẽ chuyển) và `skipped[]` (bị bỏ qua + `reason`). Kiểm tra: khoản đang khiếu nại → `DISPUTED`. Sau đó gọi lại với `"dryRun": false`:
các khoản chuyển sang HT; gọi lần nữa (không truyền `entryIds`) → `rolledOver` và `skipped` đều rỗng, vì không còn khoản nào đang thu ở vụ nguồn. `NOT_IN_SEASON` chỉ xuất hiện khi truyền `entryIds` có khoản không thuộc vụ nguồn.

## 4. Đóng vụ và báo cáo

1. `GET /api/crop-seasons/{idDX}/closing-check` → `canClose: false`, `reasons` có `HARVEST_NOT_ENDED` (nếu chưa hết thu hoạch) và/hoặc `OPEN_ENTRIES` (còn nợ đang thu trong vụ),
   `OPEN_CREDIT_ORDERS` (còn đơn chịu chưa xong). Danh sách tối đa 50 mục, số đếm là tổng.
2. Đóng vụ cần vụ đã **hết thu hoạch**. Vì vụ mới chỉ được thêm sau vụ cuối, hãy thử trên môi trường mà vụ **đầu tiên** đã qua (ví dụ `orderWindowStart` T-200,
   `harvestStart` T-120, `harvestEnd` T-90), không còn nợ đang thu trong vụ và không còn đơn chịu mở; rồi `POST /api/crop-seasons/{id}/close` `{ "version": N, "note": "…" }` → `200`, `status: "CLOSED"`. Đóng khi chưa đủ điều kiện → `422`; `version` cũ → `409`.
3. Vụ đã đóng: `PUT /api/crop-seasons/{id}` → `422`; không gia hạn vào vụ đã đóng được; không mở lại.
4. `GET /api/reports/debt-by-season` (Chủ): `origin` thoả `originatedAmount − collectedAmount − returnedAmount − reducedAmount − cancelledAmount = outstandingAmount`;
   khoản đã gia hạn vẫn nằm trong `origin` của vụ phát sinh (`carriedOverAmount`) và trong `collection` của vụ đang thu (`carriedInAmount`). Vụ đã đóng có `isFrozen: true`
   và số liệu không đổi dù sau đó có thu thêm.
5. `GET /api/crop-seasons/{id}/limit-suggestions` — chỉ cho vụ đã đóng (`422` nếu chưa); trả khách đúng hạn liên tiếp `requiredSeasons` vụ có nợ (bỏ qua vụ không nợ), xếp theo số vụ đúng hạn rồi tên, phân trang 20. Chỉ đọc.

## 4b. Bắt đầu nhắc nợ (T0) — làm sau cùng

Khi chưa bắt đầu, job nhắc nợ không gửi gì. Bằng tài khoản **Chủ**:

1. `GET /api/debt-policy` → `remindersStartDate: null`.
2. `POST /api/debt-reminders/initialize` `{ "dryRun": true }` (hoặc body rỗng `{}`) → `200`, `dryRun: true`, `startDate` = hôm nay, `skipped.N1…N5`, `skippedTotal`.
   Kiểm tra **không có gì được ghi**: `GET /api/debt-policy` vẫn `remindersStartDate: null`, `isDefault: true` (nếu cửa hàng chưa lưu chính sách).
3. `POST /api/debt-reminders/initialize` `{ "dryRun": false }` → `200`, `alreadyStarted: false`; `GET /api/debt-policy` → `remindersStartDate` = hôm nay.
   Không có thông báo mới nào cho nông dân hay nhân viên.
4. Gọi lại bước 3 → `alreadyStarted: true`, `startDate` không đổi.
5. Nhân viên (kể cả được cấp mọi quyền công nợ) và nông dân gọi route này (kể cả `dryRun: true`) → `403`.
6. Các khoản nợ cũ bị bỏ qua vẫn hiện đủ ở `GET /api/debt-entries` và trên bảng điều khiển; chỉ việc nhắc tự động là bỏ qua.

> Swagger không đổi được ngày, nên kiểm "mốc trước T0 không gửi, mốc sau T0 vẫn gửi" bằng test tự động `DebtReminderStartDatabaseTests` (đồng hồ giả).

## 5. Màn "Cần quyết định" và bảng điều khiển

1. `GET /api/debt-entries?needsDecision=true` — khoản còn nợ, không khiếu nại, quá hạn từ mốc N5 hoặc đã gia hạn đủ số lần Chủ cần xem (`ownerReviewAfterRollovers`).
2. `GET /api/debt-entries/dashboard` (Chủ) — kiểm tra `currentSeason`, `currentSeasonOutstanding`, `dueSoonAmount`, `needsDecision[]`, `creditLimitAlerts[]` (khách từ 90% hạn mức:
   đặt hạn mức nhỏ để thấy `NEAR_LIMIT`/`AT_LIMIT`).
3. Lọc: `?dueState=OVERDUE`, `?collectionSeasonId=…`, `?originSeasonId=…`, `?rolledOverOnly=true`; giá trị `dueState` sai → `400`.

## 6. Phân quyền

Với tài khoản **nhân viên** (kể cả đã được cấp mọi quyền công nợ trong màn phân quyền), các gọi sau phải `403` và dữ liệu không đổi:

| Gọi | Kết quả |
|---|---|
| `POST /api/crop-seasons`, `PUT /api/crop-seasons/{id}`, `POST /api/crop-seasons/{id}/close` | `403` |
| `PUT /api/debt-policy` | `403` |
| `POST /api/debt-entries/{id}/adjust`, `/cancel`, `/change-due-date`; `POST /api/customers/{id}/debt/manual-entries` | `403` |
| `POST /api/payments/{id}/confirm`, `/reject`; `POST /api/customers/{id}/credit/suspend`, `/block`, `/activate` | `403` |
| `GET /api/debt-policy`, `GET /api/crop-seasons` (có quyền đọc) | `200` |

Nông dân: `GET /api/crop-seasons`, `GET /api/debt-policy`, mọi `POST …/rollover` → `403`; `GET /api/me/debt-entries/{id của người khác}` → `404`.

## 7. Nông dân

Bằng token **nông dân**:

1. `GET /api/me/crop-seasons/current` → vụ hiện tại, `expectedDueDate`, `dueRule`, `isEstimate: true`. Không có vụ → `dueRule: "TERM_DAYS"` (hoặc `"NONE"` nếu chưa có tín dụng).
2. `GET /api/me/debt` → `currentBalance`, `groups[]` (vụ đang thu + hạn + số tiền + số khoản + `dueState`).
3. `GET /api/me/debt-entries?collectionSeasonId=…` chỉ trả khoản của chính mình.
4. `POST /api/me/debt-entries/{id}/dispute` `{ "reason": "…" }` — khiếu nại không đổi số nợ.

## 8. Mã lý do (Chủ)

`POST /api/debt-entries/{id}/adjust` `{ "amount": 100000, "reason": "Tri ân", "reasonCode": "TRI_AN" }` → `200`; trong `actions` có `reasonCode: "TRI_AN"`.
`reasonCode: "BOGUS"` → `400`. Bỏ `reasonCode` → `400` (bắt buộc từ 05/11); action cũ (trước đó) không có mã hiện `isPrePolicy: true`.
