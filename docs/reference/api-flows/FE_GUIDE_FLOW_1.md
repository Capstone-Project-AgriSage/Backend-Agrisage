# Hướng dẫn FE tích hợp API — Luồng 1: Bán hàng tại quầy

Viết cho: đội FE (React) của các app **Bán hàng (Sales staff)**, **Đại lý (Agent)** và **Admin**. Tài liệu chỉ nói về luồng 1 (bán hàng tại quầy):
luồng nghiệp vụ, các màn hình **cần có để thể hiện đúng luồng**, và API của từng màn.

> **Luồng là chuẩn, UI đi theo luồng.** Thiết kế UI/UX hiện có (`AgriSage-UI-Rework-Anti`) chỉ là **tài liệu tham khảo** về bố cục và phong cách. Chỗ nào
> thiết kế không khớp luồng thì **UI phải sửa**, không đổi luồng hay API theo UI. §2 mô tả màn hình theo luồng (kèm trang tham khảo có thể tái sử dụng);
> §6 liệt kê những thay đổi UI bắt buộc.

Nguồn sự thật: code trên nhánh `main` của backend. Hợp đồng chi tiết nằm ở `docs/reference/api-flows/FLOW_1_COUNTER_SALE.md`;
Swagger (`/swagger`, chỉ bật ở môi trường Development) liệt kê đủ route và DTO. Khi tài liệu này và Swagger khác nhau, **Swagger đúng**
và xin báo BE để sửa tài liệu.

---

## Mục lục

0. [Đọc trước: 10 điều quan trọng](#0-đọc-trước-10-điều-quan-trọng)
1. [Kết nối, đăng nhập, quy ước chung](#1-kết-nối-đăng-nhập-quy-ước-chung)
2. [Luồng bán hàng tại quầy và các màn hình cần có](#2-luồng-bán-hàng-tại-quầy-và-các-màn-hình-cần-có)
3. [Kịch bản đầu–cuối có JSON mẫu](#3-kịch-bản-đầucuối-có-json-mẫu)
4. [Tham chiếu từng API](#4-tham-chiếu-từng-api)
5. [Lỗi và cách hiển thị](#5-lỗi-và-cách-hiển-thị)
6. [Thay đổi UI bắt buộc so với thiết kế tham khảo](#6-thay-đổi-ui-bắt-buộc-so-với-thiết-kế-tham-khảo)
7. [Đề xuất bổ sung phía BE (chưa có)](#7-đề-xuất-bổ-sung-phía-be-chưa-có)
8. [Chưa làm được vì phụ thuộc luồng khác](#8-chưa-làm-được-vì-phụ-thuộc-luồng-khác)
9. [Chuẩn bị môi trường và dữ liệu để chạy thử](#9-chuẩn-bị-môi-trường-và-dữ-liệu-để-chạy-thử)
10. [Phụ lục: kiểu TypeScript](#10-phụ-lục-kiểu-typescript)

---

## 0. Đọc trước: 10 điều quan trọng

1. **Server tính giá, tổng tiền, trạng thái, mã đơn.** FE không gửi giá. Ngoại lệ duy nhất: nhân viên **ghi đè giá** một dòng,
   khi đó phải gửi kèm **lý do**; server lưu giá gợi ý, người sửa và lý do.
2. **`quantity` là số quy cách bán** (bao, hộp, chai…). Mọi trường tên `…BaseQuantity` là **đơn vị cơ sở** của kho (chai, gói…).
   Số lượng theo **lô** luôn là đơn vị cơ sở. Ví dụ: 21 hộp (mỗi hộp 6 chai) = `baseQuantity` 126.
3. **Thứ tự của một đơn tại quầy:** tạo đơn → (sửa dòng) → **thu tiền** → **xác nhận** (giữ hàng theo FEFO) → **giao tại quầy**
   (chọn lô thực tế) → `COMPLETED`. Hoặc **bán nhanh** (`/api/counter-sales`) làm cả chuỗi trong một lần gọi.
4. **Hàng chỉ ra khỏi kho khi giao tại quầy.** Xác nhận chỉ *giữ* hàng (tồn khả dụng giảm, tồn thực chưa giảm).
5. **Server chưa kiểm tra "đã trả đủ mới được xác nhận".** FE tự chặn: chỉ cho bấm *Xác nhận* khi `paidAmount ≥ orderTotal`
   (lấy từ `GET /api/orders/{id}/payments`). Kiểm tra này sẽ do server làm sau (luồng 3), FE giữ lại vẫn đúng.
6. **Không có idempotency key.** Khoá nút (loading) từ lúc bấm đến khi có phản hồi, nếu không bấm đúp sẽ tạo hai đơn.
7. **409** = có người khác vừa sửa → tải lại dữ liệu rồi cho thử lại. **422** = vi phạm quy tắc nghiệp vụ → hiển thị lý do,
   nếu có `errors` thì gắn theo từng dòng hàng (§5).
8. **Tiền** là số JSON (`1250000.00`), tối đa 2 chữ số thập phân; VND thực tế là số nguyên. Hiển thị `1.250.000 ₫`.
9. **Thời gian** trả về là UTC (ISO 8601): đổi sang giờ Việt Nam (UTC+7) khi hiển thị. **Ngày lọc** (`fromDate`, `toDate`) là
   ngày theo giờ Việt Nam, dạng `yyyy-MM-dd`.
10. **Token sống 60 phút, không có refresh.** Gặp `401` (hết hạn, hoặc tài khoản bị khoá/xoá) → xoá token, về trang đăng nhập.

---

## 1. Kết nối, đăng nhập, quy ước chung

### 1.1 Địa chỉ và CORS

| Mục | Giá trị |
|---|---|
| API khi chạy local | `https://localhost:7068` (cổng `http://localhost:5206` sẽ chuyển hướng sang https — **hãy gọi https**, trình duyệt không cho preflight đi qua chuyển hướng) |
| Swagger | `https://localhost:7068/swagger` |
| CORS (dev) | cho phép `http://localhost:5173` và `http://localhost:3000`; origin khác bị chặn. Môi trường thật: BE đặt `Cors__AllowedOrigins__0`… (báo BE origin của web khi triển khai) |
| Header gửi lên | `Authorization: Bearer <accessToken>`, `Content-Type: application/json` |
| Giới hạn tốc độ | đăng nhập/đăng ký: 10 lần/phút/IP (quá thì `429`) |

### 1.2 Đăng nhập

```http
POST /api/auth/login
{ "identifier": "0901234567", "password": "…" }      // identifier = số điện thoại hoặc email
```

```json
{
  "accessToken": "eyJ…", "expiresAt": "2026-10-04T09:00:00Z",
  "user": { "id": "uuid", "fullName": "Trần Thị Hương", "phoneNumber": "0901234567", "email": null, "role": "SALES_STAFF" }
}
```

- Sai tài khoản hoặc mật khẩu: `401`. Tài khoản bị khoá: `403` (`This account is not active.`).
- `GET /api/auth/me` trả thông tin người đang đăng nhập (kiểm tra lại token khi mở app).
- `POST /api/auth/change-password` `{ currentPassword, newPassword }`.
- Thiết kế hiện có nút "Quên mật khẩu": **chưa có API** (không có email/SMS reset). Admin/Chủ cửa hàng đặt lại mật khẩu cho nhân viên
  qua `POST /api/staff/{id}/reset-password`.

### 1.3 Vai trò ↔ app trong thiết kế

| App thiết kế | Vai trò API (`role`) | Quyền dùng cho luồng 1 |
|---|---|---|
| Sales staff (`sales_staff`) | `SALES_STAFF` | Đọc catalog/kho; tạo, sửa, thu tiền, xác nhận, giao, huỷ đơn; bán nhanh. **Không** được ghi bảng giá, xem báo cáo, đổi trạng thái lô |
| Agent / Đại lý (`agent`) | `STORE_OWNER` | Mọi thứ của nhân viên bán hàng **và** bảng giá, báo cáo bán hàng, đổi trạng thái lô, quản lý nhân viên |
| Admin | `ADMIN` | Giống chủ cửa hàng về quyền API; app Admin thiết kế cho quản trị hệ thống (tài khoản, AI, nội dung) |
| Delivery staff | `DELIVERY_STAFF` | **Không** có quyền với luồng 1 (đơn, thanh toán, kho đều 403) |
| Farmer web/mobile | `FARMER` | Chỉ `/api/me/...` (xem thanh toán và đơn của chính mình) |

Gọi route không đủ quyền → `403`. Không gửi token → `401`.

### 1.4 Quy ước

| Chủ đề | Quy ước |
|---|---|
| JSON | camelCase; id là chuỗi UUID; enum là chuỗi `UPPER_SNAKE` (gửi lên không phân biệt hoa thường) |
| Danh sách | `page` (mặc định 1), `pageSize` (mặc định 20, **tối đa 100**); trả `{ items, page, pageSize, totalCount, totalPages }` |
| Tạo mới | `201` + header `Location` + toàn bộ đối tượng vừa tạo |
| Sửa / đổi trạng thái | `200` + toàn bộ đối tượng cha (ví dụ sửa dòng hàng trả cả `OrderResponse`) → **thay luôn state FE bằng bản trả về** |
| Xoá | xoá mềm; nhiều route `DELETE` trả lại đơn sau khi xoá dòng |
| Ngày giờ | trả UTC ISO 8601; trường ngày thuần (`expiryDate`, `fromDate`) dạng `yyyy-MM-dd` |
| Số điện thoại | số di động Việt Nam `0[35789]xxxxxxxx`; chấp nhận khi gõ `0972.445.667`, `0972 445 667`, `+84972445667` (server chuẩn hoá về `0972445667`) |
| Đồng thời | mỗi `OrderResponse` có `version`; hai người sửa cùng lúc → người sau nhận `409` |

### 1.5 Mẫu hàm gọi API (gợi ý)

```ts
export class ApiError extends Error {
  constructor(public status: number, public title: string, public detail?: string,
              public errors?: Record<string, string[]>, public traceId?: string) { super(detail ?? title) }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = localStorage.getItem('agrisage_token')
  const res = await fetch(`${import.meta.env.VITE_API_URL}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init.headers },
  })
  if (res.status === 401) { localStorage.removeItem('agrisage_token'); location.assign('/login'); throw new ApiError(401, 'Unauthorized') }
  if (res.ok) return (res.status === 204 ? undefined : await res.json()) as T
  const p = await res.json().catch(() => ({}))          // 401/403 do JWT middleware có thể không có body
  throw new ApiError(res.status, p.title ?? res.statusText, p.detail, p.errors, p.traceId)
}
```

---

## 2. Luồng bán hàng tại quầy và các màn hình cần có

> **Nguyên tắc:** luồng nghiệp vụ dưới đây là chuẩn. Thiết kế UI/UX hiện có (`AgriSage-UI-Rework-Anti`) **chỉ để tham khảo bố cục và phong cách**;
> khi thiết kế không khớp luồng thì **sửa UI theo luồng**, không đòi API đổi theo UI. Mục "Tham khảo thiết kế" ở mỗi màn chỉ ra trang có thể tái sử dụng
> khung và những gì phải đổi (tổng hợp ở §6).

### 2.0 Luồng tổng thể

```
        ┌────────────── Bán nhanh (khách trả tiền mặt đủ, lấy hàng ngay) ──────────────┐
        │  chọn hàng → xem trước (giá + lô) → Bán  ⇒  đơn COMPLETED + thanh toán PAID   │
        └───────────────────────────────────────────────────────────────────────────────┘

 Đơn nhiều bước:
  Soạn đơn ──► Thu tiền (một hoặc nhiều lần) ──► Xác nhận (giữ hàng theo FEFO) ──► [Chuẩn bị] ──► [Sẵn sàng] ──► Giao tại quầy ──► Hoàn thành
     │                                                   │                                              │ (giao từng phần được)
     └────────────── Hủy đơn (chưa giao gì) ◄────────────┘                         Hủy phần còn lại ◄──┘
                         │                                                              │
                         └──► Khoản phải hoàn tiền cho khách (nếu khách đã trả trước) ◄──┘
```

Ba nguyên tắc của luồng mà giao diện phải thể hiện:
1. **Giữ hàng ≠ xuất hàng.** Xác nhận chỉ giữ hàng; hàng chỉ rời kho khi giao tại quầy. Tồn "khả dụng" = tồn thực − hàng đang giữ.
2. **Tiền và hàng là hai trạng thái độc lập.** Một đơn có *trạng thái đơn* (`status`) và *trạng thái thanh toán* (suy ra từ tóm tắt thanh toán). Luôn hiển thị cả hai, không gộp thành một nhãn.
3. **Lô hàng là một phần của nghiệp vụ.** Nhân viên xác nhận lô thực tế khi giao (hàng hết hạn sớm nhất xuất trước — FEFO). Màn nào xuất hàng đều phải cho thấy lô.

### 2.1 Danh sách màn hình và vai trò

| Mã | Màn hình | Ai dùng | API chính | Trang tham khảo trong thiết kế |
|---|---|---|---|---|
| M1 | Đăng nhập | mọi nhân viên | `POST /api/auth/login` | `features/auth/LoginPage` |
| M2 | Chọn hàng: tra cứu sản phẩm, quy cách, giá | Bán hàng, Đại lý | `GET /api/catalog/...`, `GET /api/inventory/lots` | `features/products/ProductsPage` |
| M3 | Soạn đơn tại quầy (tạo, sửa dòng, ghi đè giá) | Bán hàng, Đại lý | `POST/PUT/DELETE /api/orders…` | form "Tạo đơn hàng" trong `OrdersPage` |
| M4 | Thu tiền cho đơn | Bán hàng, Đại lý | `POST /api/payments/cash`, `GET /api/orders/{id}/payments` | `features/payments/PaymentsPage` |
| M5 | Xác nhận đơn và giữ hàng (xem lô FEFO) | Bán hàng, Đại lý | `GET …/fefo-suggestions`, `POST …/confirm` | *chưa có* |
| M6 | Giao hàng tại quầy (chọn lô thực tế) | Bán hàng, Đại lý | `POST …/pickup` | *chưa có* |
| M7 | Bán nhanh | Bán hàng, Đại lý | `POST /api/counter-sales/preview`, `POST /api/counter-sales` | tuỳ chọn "Hoàn tất ngay" của form tạo đơn |
| M8 | Hủy đơn, hủy phần còn lại, khoản phải hoàn | Bán hàng, Đại lý | `POST …/cancel`, `POST …/cancel-remaining` | nút "Hủy đơn" |
| M9 | Danh sách đơn và chi tiết đơn (trung tâm điều hướng) | Bán hàng, Đại lý | `GET /api/orders`, `GET /api/orders/{id}` | `features/orders/OrdersPage` |
| M10 | Thanh toán: theo dõi tiền thu theo đơn | Bán hàng, Đại lý | `GET /api/orders/{id}/payments`, `GET /api/payments` | `features/payments/PaymentsPage` |
| M11 | Kho: tra cứu lô, hạn dùng, biến động | Bán hàng, Đại lý | `GET /api/inventory/…` | `features/inventory/InventoryPage` |
| M12 | Bảng giá | **Đại lý** (đọc: Bán hàng) | `/api/price-lists…` | *chưa có* |
| M13 | Báo cáo bán hàng, dashboard doanh thu | **Đại lý** | `GET /api/reports/sales` | `features/dashboard/DashboardPage` (Đại lý) |

Gợi ý menu: **Bán hàng** (Bán nhanh · Đơn hàng) · **Thu tiền** · **Kho** · *(chỉ Đại lý)* **Bảng giá** · **Báo cáo**.

---

### M1. Đăng nhập

`POST /api/auth/login` → lưu `accessToken` và `user.role`; vai trò quyết định menu (§1.3). Gặp `401` ở bất kỳ API nào → xoá token, về đăng nhập.
Tham khảo thiết kế: bỏ bước "chọn vai trò để điền sẵn email" (vai trò do server trả); bỏ "Quên mật khẩu" (chưa có API; Chủ cửa hàng đặt lại mật khẩu
cho nhân viên qua `POST /api/staff/{id}/reset-password`); tên cửa hàng chưa có API (hiện chỉ một cửa hàng).

### M2. Chọn hàng: sản phẩm, quy cách, giá, tồn

Dùng làm bộ chọn sản phẩm cho M3 và M7, đồng thời là trang tra cứu độc lập.

| Cần | API | Ghi chú |
|---|---|---|
| Danh sách sản phẩm đang bán + giá | `GET /api/catalog/products?search=&categoryId=&page=&pageSize=` (không cần token) | `id` = **storeProductId** (cái đơn hàng tham chiếu). `fromPrice` = giá thấp nhất của các quy cách bán |
| Quy cách + giá từng quy cách | `GET /api/catalog/products/{id}` | `packagings[]`: `id` = **productPackagingId**, `conversionToBase`, `price` (giá **bảng giá khách lẻ**). `price = null` → quy cách chưa có giá, **không bán được** (server trả 422) |
| Danh mục để lọc | `GET /api/catalog/categories` | |
| Tồn khả dụng | `GET /api/inventory/lots?storeProductId=&hasStock=true` | cộng `quantityAvailable` các lô (đơn vị cơ sở). Chưa có API tổng hợp (§7). Ngưỡng "Sắp hết": `minStockLevelBase` của `GET /api/store-products/{id}` |

Hành vi bắt buộc:
- Người dùng chọn **quy cách** (bao, hộp, chai…) chứ không chỉ chọn sản phẩm; giá và số lượng luôn gắn với một quy cách.
- Hiển thị rõ đơn vị: số lượng đặt là **số quy cách**, tồn kho là **đơn vị cơ sở**; đổi qua lại bằng `conversionToBase` (21 hộp × 6 = 126 chai).
- Quy cách không có giá (`price = null`) hiển thị "Chưa có giá" và không cho thêm vào đơn.
- Giá ở đây là **giá khách lẻ để tham khảo**; giá cuối cùng luôn lấy từ phản hồi của `POST /api/orders` hoặc `preview`.
- API catalog là công khai, giới hạn 120 lần/phút/IP: dùng debounce khi tìm kiếm.

Tham khảo thiết kế: `ProductsPage` có sẵn bộ lọc, tình trạng tồn, nút "Thêm vào đơn" — giữ khung này nhưng thêm bước chọn quy cách.

### M3. Soạn đơn tại quầy

Một đơn gồm **nhiều dòng**; mỗi dòng là (sản phẩm, quy cách, số lượng). Giá **không nhập tay**.

Giao diện bắt buộc:
1. **Khách:** chỉ **Khách lẻ**; tên và SĐT không bắt buộc (để trống tên → server ghi "Khách lẻ"). *Khách quen chưa dùng được (§8).*
2. **Bảng dòng hàng:** chọn sản phẩm → quy cách → số lượng (từ M2). Đơn giá hiển thị **chỉ đọc**; nút "Sửa giá" mở ô nhập giá mới và **lý do bắt buộc**. Dòng đã sửa giá hiển thị cả giá gợi ý (gạch ngang) và giá bán, kèm dấu "đã ghi đè".
3. **Tổng tiền** lấy từ phản hồi của server, không tự cộng ở FE.
4. **Ghi chú** (≤ 1000 ký tự).
5. Hai nút: **Tạo đơn** (đơn *Chờ xác nhận*) và **Bán nhanh** (sang M7).

```http
POST /api/orders
{
  "customerType": "WALK_IN", "settlementType": "FULL_PAYMENT", "fulfillmentType": "PICKUP",
  "customerName": null, "customerPhone": "0972.445.667", "note": "Khách quay lại lấy chiều nay",
  "items": [
    { "storeProductId": "…", "productPackagingId": "…", "quantity": 10 },
    { "storeProductId": "…", "productPackagingId": "…", "quantity": 2, "unitPrice": 640000, "overrideReason": "Khách quen mua nhiều" }
  ]
}
```

- `customerType`, `settlementType`, `fulfillmentType` cố định `WALK_IN`, `FULL_PAYMENT`, `PICKUP` cho màn này.
- Gửi `unitPrice` chỉ khi nhân viên **đã sửa** giá; bằng giá gợi ý thì server bỏ qua. Khác giá mà thiếu `overrideReason` → `422` (`errors["items[i]"]`).
- Mỗi cặp (sản phẩm, quy cách) chỉ **một lần** trong đơn (trùng → `400`): gộp số lượng ở FE.
- Tối đa 100 dòng; `quantity` từ 1 đến 100.000.000.
- Phản hồi `201` là `OrderResponse` đầy đủ: mã `OD-yyyyMMdd-NNNN`, mỗi dòng có `suggestedUnitPrice`, `unitPrice`, `priceOverridden`, `lineTotalAmount`, `baseQuantity`.

**Sửa đơn khi còn *Chờ xác nhận*** (sau khi xác nhận mọi sửa đổi bị từ chối `422`: hãy khoá form và hiện "Đơn đã xác nhận, không sửa được"):

| Việc | API |
|---|---|
| Đổi ghi chú (chuỗi rỗng để xoá) | `PUT /api/orders/{id}` `{ "note": "…" }` |
| Thêm dòng | `POST /api/orders/{id}/items` (một dòng như lúc tạo). Dòng thêm sau dùng **bảng giá đã chốt của đơn**. Thêm cặp đã có → `422` (hãy đổi số lượng dòng đó) |
| Đổi số lượng | `PUT /api/orders/{id}/items/{itemId}` `{ "quantity": 5 }` |
| Ghi đè giá | `PUT /api/orders/{id}/items/{itemId}/price` `{ "unitPrice": 640000, "reason": "…" }` |
| Trả về giá gợi ý | `DELETE /api/orders/{id}/items/{itemId}/price` |
| Xoá dòng | `DELETE /api/orders/{id}/items/{itemId}` |

Mỗi lần gọi trả `OrderResponse` mới → thay state và hiển thị tổng tiền mới ngay.

Tham khảo thiết kế: form hiện tại (một dòng, gõ tay tên sản phẩm và đơn giá, chọn phương thức thanh toán) **phải thay** bằng bố cục trên.
Phương thức thanh toán **không chọn lúc soạn đơn**; tiền được thu ở M4.

### M4. Thu tiền

Tiền mặt được nhân viên nhận tại quầy, nên mỗi lần thu **tạo khoản thanh toán `PAID` ngay**. Một đơn có thể thu **nhiều lần** (đặt cọc rồi trả nốt).

```http
POST /api/payments/cash
{ "paymentContext": "ORDER_PAYMENT", "orderId": "…", "amount": 1250000, "note": "Khách đặt cọc" }
```

Giao diện bắt buộc (hộp thoại "Thu tiền" mở từ M9 hoặc M10):
- Hiển thị **Tổng đơn · Đã thu · Còn phải thu** (lấy từ `GET /api/orders/{id}/payments`: `orderTotal`, `paidAmount`, `remainingToPay`).
- Ô số tiền, kèm các nút nhanh **"Thu hết số còn lại"** (điền `remainingToPay`) và **"Đặt cọc 50%"** (điền 50% tổng). Đặt cọc **không phải loại thanh toán riêng**, chỉ là thu một phần.
- `amount` > 0 và **không vượt `remainingToPay`** (vượt → `422`). Không thu được cho đơn `CANCELLED`, `PARTIALLY_CANCELLED`, `COMPLETED`.
- Sau khi thu, cập nhật lại ba số trên bằng dữ liệu server trả.
- Phương thức duy nhất hiện nay là **tiền mặt** (`CASH`). VietQR/payOS chưa có (§8) — đừng hiển thị lựa chọn này.
- **Cổng chặn phía FE:** nút **Xác nhận đơn** (M5) chỉ bật khi `paidAmount ≥ orderTotal`. Server chưa tự kiểm tra việc này (§0, điều 5).

Trạng thái thanh toán của một đơn (FE tự tính): `paidAmount = 0` → *Chưa thanh toán*; `0 < paidAmount < orderTotal` → *Thanh toán 1 phần*; `paidAmount ≥ orderTotal` → *Đã thanh toán*.

`POST /api/payments/{id}/cancel` chỉ huỷ khoản đang `PENDING`; tiền mặt tạo ra là `PAID` luôn nên thực tế **chưa dùng** (dành cho chuyển khoản/payOS sau này).

Tham khảo thiết kế: `PaymentsPage` có form "Tạo thanh toán" gõ tay mã đơn, tên khách, tổng đơn — thay bằng **chọn một đơn có sẵn** rồi nhập số tiền; mọi thông tin khác lấy từ đơn.

### M5. Xác nhận đơn và giữ hàng

Màn bắt buộc **trước** khi bấm Xác nhận: cho nhân viên thấy lô sẽ bị giữ.

```http
GET /api/orders/{id}/fefo-suggestions
```
```json
{ "orderId": "…", "items": [
  { "orderItemId": "…", "baseQuantity": 125, "remainingBaseQuantity": 125, "shortageBaseQuantity": 0,
    "lots": [
      { "inventoryLotId": "…", "lotNumber": "L01", "expiryDate": "2027-01-31", "availableBaseQuantity": 100, "suggestedBaseQuantity": 100 },
      { "inventoryLotId": "…", "lotNumber": "L02", "expiryDate": "2027-03-31", "availableBaseQuantity": 80,  "suggestedBaseQuantity": 25 } ] } ] }
```

- Mỗi dòng đơn hiển thị các lô sẽ giữ: số lô, hạn dùng, số lượng (đơn vị cơ sở).
- `shortageBaseQuantity > 0` → thiếu hàng: tô đỏ dòng, ghi "Thiếu N", **khoá nút Xác nhận** (nếu vẫn gọi, server trả `422` kèm `errors["items[i]"]` và **không giữ gì**).
- Nút **Xác nhận** → `POST /api/orders/{id}/confirm` (không body) → `CONFIRMED`. Bật nút khi: đơn `PENDING_CONFIRMATION`, có ít nhất một dòng, đã thu đủ tiền (M4), không thiếu hàng.
- Lô được **giữ theo FEFO tự động**; nhân viên **không chọn lô ở bước này** (chọn lô thực tế ở M6).
- Sản phẩm bị ngừng bán sau khi tạo đơn: xác nhận trả `422` (`can no longer be sold`) kèm dòng sai → cho xoá dòng đó hoặc huỷ đơn.
- Sau khi xác nhận: `GET /api/orders/{id}/reservation` cho biết hàng đang giữ ở lô nào.
- *Bắt đầu chuẩn bị* (`POST …/start-preparing`) và *Sẵn sàng giao* (`POST …/mark-ready`) là **bước tuỳ chọn**; bỏ qua vẫn giao được. Hiển thị chúng như nút phụ nếu cửa hàng muốn theo dõi soạn hàng.

Tham khảo thiết kế: chưa có màn tương ứng; thêm hộp thoại/ngăn xác nhận trong chi tiết đơn của `OrdersPage`.

### M6. Giao hàng tại quầy

Mở từ đơn `CONFIRMED`, `PREPARING`, `READY_FOR_FULFILLMENT` hoặc `PARTIALLY_FULFILLED`. Nhân viên nhập **lô thực tế lấy ra**.
Mặc định điền từ `GET /api/orders/{id}/fefo-suggestions` (đơn đã xác nhận trả các lô đang giữ hàng; `suggestedBaseQuantity` = số còn lại), cho phép sửa lô và số lượng.

```http
POST /api/orders/{id}/pickup
{ "items": [ { "orderItemId": "…", "lots": [ { "inventoryLotId": "…", "baseQuantity": 100 }, { "inventoryLotId": "…", "baseQuantity": 25 } ] } ],
  "note": "Khách lấy tại quầy" }
```

- Mỗi dòng: **tổng** `baseQuantity` các lô ≤ số còn lại của dòng và **là bội số của quy cách** (hộp 6 chai: tổng phải chia hết cho 6). Một lô riêng lẻ không cần chia hết.
- Cho phép **giao từng phần**: đơn thành `PARTIALLY_FULFILLED`; giao nốt hoặc *Hủy phần còn lại* (M8).
- Lô hết hạn, bị khoá, hoặc thuộc sản phẩm khác → `422` với `errors["items[i]"]`.
- Trả `OrderResponse` mới; giao đủ → `COMPLETED` và có `pickupCompletedAt/By`.
- Kho tự giảm và phiếu xuất kho tạo ở server; FE không làm gì thêm.
- Khi nhân viên lấy lô khác lô đã giữ, vẫn được (miễn lô đó còn hàng trống); server tự chuyển phần giữ sang lô thực tế.

Tham khảo thiết kế: chưa có (thiết kế dùng trạng thái "Đang giao hàng" cho đơn giao tận nơi, **không phải** việc này).

### M7. Bán nhanh

Dành cho trường hợp phổ biến nhất: khách **trả tiền mặt đủ và lấy hàng ngay**. Một lần gọi tạo đơn, thu tiền, giữ hàng và giao hàng.
Luôn gọi `preview` trước để nhân viên xác nhận lô.

```http
POST /api/counter-sales/preview        // không lưu gì; trường lots bị bỏ qua
{ "customerType": "WALK_IN", "customerName": "Châu Văn Hòa", "customerPhone": "0972.445.667",
  "items": [ { "storeProductId": "…", "productPackagingId": "…", "quantity": 10 } ] }
```
```json
{ "customerGroupId": null, "priceListId": "…", "totalAmount": 6850000,
  "items": [ { "storeProductId": "…", "productPackagingId": "…", "sku": "NPK-2020", "productName": "NPK Đầu Trâu 20-20-15",
               "packagingName": "Bao 50kg", "quantity": 10, "conversionToBase": 1, "baseQuantity": 10,
               "suggestedUnitPrice": 685000, "unitPrice": 685000, "lineTotalAmount": 6850000,
               "lots": [ { "inventoryLotId": "…", "lotNumber": "L01", "expiryDate": "2027-01-31", "availableBaseQuantity": 14, "suggestedBaseQuantity": 10 } ],
               "shortageBaseQuantity": 0 } ] }
```

Giao diện bắt buộc: bảng xác nhận có từng dòng, đơn giá, **lô sẽ xuất** (số lô, hạn dùng, số lượng), tổng tiền phải thu, và nút **Bán**. `shortageBaseQuantity > 0` → khoá nút. Khi bấm **Bán**:

```http
POST /api/counter-sales
{ "customerType": "WALK_IN", "customerName": "Châu Văn Hòa", "customerPhone": "0972.445.667", "note": null,
  "items": [ { "storeProductId": "…", "productPackagingId": "…", "quantity": 10,
               "lots": [ { "inventoryLotId": "…", "baseQuantity": 10 } ] } ] }
```

- `lots` **bắt buộc** khi bán (thiếu → `400`, khoá `items[i].lots`). Tổng `baseQuantity` các lô một dòng **phải bằng đúng** `baseQuantity` của dòng. Mặc định dùng lô `preview` đề xuất; cho đổi lô nếu thực tế lấy lô khác.
- Phản hồi `201`: `{ "order": OrderResponse (COMPLETED), "payment": PaymentResponse (PAID) }`. Hiển thị phiếu bán từ `order` (mã, dòng, `totalAmount`) và `payment.paymentNumber`.
- **Tất cả hoặc không gì cả:** lỗi (hết hàng, lô bị khoá giữa chừng…) thì không có đơn, thanh toán hay thay đổi kho nào được lưu → hiện lỗi và cho thử lại từ `preview`.
- Có thể ghi đè giá: gửi `unitPrice` + `overrideReason` trong dòng, như M3.
- Khách quen, bán nợ, giao tận nơi **không** dùng route này (dùng luồng nhiều bước, và hiện chưa hỗ trợ).

Tham khảo thiết kế: tuỳ chọn "Hoàn tất ngay" trong form tạo đơn tương ứng chức năng này, nhưng cần màn xem trước lô như trên.

### M8. Hủy đơn, hủy phần còn lại, khoản phải hoàn

**Hủy cả đơn** — chỉ khi **chưa giao gì** (`PENDING_CONFIRMATION` đến `READY_FOR_FULFILLMENT`):

```http
POST /api/orders/{id}/cancel
{ "reason": "Khách đổi ý" }                 // bắt buộc, tối đa 1000 ký tự
```
```json
{ "order": { "status": "CANCELLED", "cancelReason": "Khách đổi ý", "…": "…" },
  "refunds": [ { "refundId": "…", "refundNumber": "RF-20261004-0001", "paymentId": "…", "refundMethod": "CASH", "amount": 500000 } ] }
```

- Bắt buộc có hộp thoại nhập **lý do** trước khi gọi.
- `refunds` là **số tiền cửa hàng phải trả lại khách** (khách đã trả trước nhưng đơn bị huỷ): hiển thị nổi bật *"Cần hoàn 500.000 ₫ tiền mặt cho khách"*.
  Mỗi khoản hoàn ở trạng thái `PENDING` (chờ nhân viên trả tiền rồi ghi nhận; **route ghi nhận đã hoàn tiền thuộc luồng 4, chưa có** — §8).
- Lịch sử khoản hoàn của đơn: trường `refunds` trong `GET /api/orders/{id}/payments`; hiển thị trong chi tiết đơn.
- Đơn đã giao một phần mà gọi huỷ cả đơn → `422`. Với đơn `PARTIALLY_FULFILLED` **ẩn nút "Hủy đơn"**, chỉ có "Hủy phần còn lại".

**Hủy phần còn lại** của một dòng (đơn đã giao một phần, hoặc bỏ riêng một dòng sau khi đã xác nhận):

```http
POST /api/orders/{id}/items/{itemId}/cancel-remaining
{ "reason": "Khách không lấy nữa" }
```

- Hủy phần chưa giao của dòng và trả lại hàng đã giữ. Nếu sau đó đơn không còn gì mở: `CANCELLED` (chưa giao gì) hoặc `PARTIALLY_CANCELLED` (đã giao một phần).
- Khi đơn kết thúc theo cách này, phần tiền trả trước chưa dùng cũng được hoàn như trên. **Response chỉ trả `OrderResponse`**, nên sau khi gọi hãy gọi lại `GET /api/orders/{id}/payments` để hiển thị khoản hoàn (trường `refunds`).

Tham khảo thiết kế: nút "Hủy đơn" ở bảng đơn — giữ, nhưng bổ sung hộp thoại lý do, nhánh "Hủy phần còn lại", và phần hiển thị khoản hoàn.

### M9. Danh sách đơn và chi tiết đơn (trung tâm điều hướng)

`GET /api/orders?status=&search=&fromDate=&toDate=&customerType=&source=&page=&pageSize=` (mới nhất trước).

- `search` tìm theo **mã đơn, tên khách, số điện thoại**. `status` nhận **một** giá trị; muốn gộp nhiều trạng thái (ví dụ "Đang xử lý" = `CONFIRMED` + `PREPARING`) thì gọi nhiều lần hoặc lọc ở FE.
- Mỗi hàng (`OrderListItem`): `id, orderNumber, source, customerType, customerName, customerPhone, settlementType, fulfillmentType, status, totalAmount, itemCount, createdAt, confirmedAt`.
  Danh sách **không có tên sản phẩm** (chỉ `itemCount`): hiển thị "3 sản phẩm" hoặc nạp chi tiết khi mở.
- Cột/Nhãn **thanh toán** của từng hàng: chưa có trong danh sách (§7); tạm gọi `GET /api/orders/{id}/payments` cho các hàng đang hiển thị (≤ `pageSize` lần), rồi tự tính trạng thái thanh toán (M4).
- Thẻ KPI: `GET /api/orders?status=…&pageSize=1` lấy `totalCount`; "Tổng giá trị" chưa có API (tính ở FE từ trang đang xem, hoặc chờ §7).
- Chi tiết đơn: `GET /api/orders/{id}` (khách, ghi chú, từng dòng với giá gợi ý/giá bán/lý do ghi đè, số lượng đã giao/đã huỷ/còn lại, `cancelReason`) cộng tóm tắt thanh toán và các khoản hoàn.

**Bảng trạng thái đơn** (hiển thị nhãn tiếng Việt do FE đặt tên, giá trị API là cột giữa):

| Nhãn gợi ý | `status` | Ghi chú |
|---|---|---|
| Chờ xác nhận | `PENDING_CONFIRMATION` | sửa được, huỷ được |
| Đã xác nhận (đã giữ hàng) | `CONFIRMED` | |
| Đang chuẩn bị | `PREPARING` | bước tuỳ chọn |
| Sẵn sàng giao | `READY_FOR_FULFILLMENT` | bước tuỳ chọn |
| Đã giao một phần | `PARTIALLY_FULFILLED` | giao từng phần |
| Hoàn thành | `COMPLETED` | đã giao đủ |
| Đã hủy | `CANCELLED` | huỷ cả đơn |
| Hủy một phần | `PARTIALLY_CANCELLED` | đã giao một phần rồi huỷ phần còn lại |

**Nút hành động theo trạng thái — hiển thị đúng bảng này, đừng tự suy ra bước kế tiếp:**

| `status` | Nút hiển thị | API |
|---|---|---|
| `PENDING_CONFIRMATION` | Sửa đơn (M3) · Thu tiền (M4) · **Xác nhận** (M5) · Hủy đơn (M8) | §M3 / `POST /api/payments/cash` / `POST …/confirm` / `POST …/cancel` |
| `CONFIRMED` | Thu tiền (nếu còn thiếu) · *Bắt đầu chuẩn bị* · *Sẵn sàng giao* · **Giao hàng tại quầy** (M6) · Hủy đơn · Hủy phần còn lại | `…/start-preparing` · `…/mark-ready` · `…/pickup` · `…/cancel` · `…/items/{itemId}/cancel-remaining` |
| `PREPARING` | *Sẵn sàng giao* · **Giao hàng tại quầy** · Hủy đơn · Hủy phần còn lại | `…/mark-ready` · `…/pickup` · `…/cancel` |
| `READY_FOR_FULFILLMENT` | **Giao hàng tại quầy** · Hủy đơn · Hủy phần còn lại | `…/pickup` · `…/cancel` |
| `PARTIALLY_FULFILLED` | Giao tiếp phần còn lại · **Hủy phần còn lại** (từng dòng). *Không* có "Hủy đơn" | `…/pickup` · `…/items/{itemId}/cancel-remaining` |
| `COMPLETED`, `CANCELLED`, `PARTIALLY_CANCELLED` | Chỉ xem (+ lịch sử thanh toán, khoản hoàn) | — |

Các nút `confirm`, `start-preparing`, `mark-ready` **không có body** và trả `OrderResponse` mới.

Tham khảo thiết kế: `OrdersPage` có sẵn bảng, bộ lọc, KPI, hộp chi tiết, ngăn phân trang — dùng làm khung. Phải thay: máy trạng thái tuyến tính (`NEXT_STATUS`) bằng bảng trên;
các trạng thái giao tận nơi (Đang giao hàng, Chờ giao lại, Giao thất bại) ẩn khỏi màn quầy.

### M10. Thanh toán: theo dõi tiền thu theo đơn

Mỗi hàng là **một đơn** với Tổng đơn · Đã thu · Còn lại · Trạng thái thanh toán, kèm lịch sử các lần thu và nút "Thu tiền" (M4). Trong API một đơn có nhiều khoản thanh toán, nên ghép như sau:

| Cần hiển thị | API |
|---|---|
| Danh sách đơn | `GET /api/orders` (bỏ đơn huỷ nếu muốn), mỗi hàng gọi `GET /api/orders/{id}/payments` |
| Tổng đơn / Đã thu / Còn lại | `orderTotal` / `paidAmount` / `remainingToPay` |
| Lịch sử thanh toán | `payments[]` (`paymentNumber, paymentMethod, amount, status, confirmedAt, initiatedAt`), hoặc `GET /api/payments?orderId=` |
| Chi tiết một khoản | `GET /api/payments/{id}` (kèm `allocations`) |
| Khoản hoàn tiền của đơn đã huỷ | `refunds[]` trong tóm tắt |
| Xuất CSV | FE tự xuất từ dữ liệu đã tải |

Người ghi nhận chỉ có `confirmedBy` là **id người dùng**; hiện **không có API tra tên** nhân viên cho vai trò bán hàng (§7). Tạm hiển thị "Nhân viên" (hoặc tên người đang đăng nhập khi chính họ vừa ghi nhận).

Tham khảo thiết kế: `PaymentsPage` (bảng, KPI, hộp chi tiết, lịch sử) dùng được làm khung; đổi form "Tạo thanh toán" theo M4, bỏ VietQR và "Cọc 50%" như loại phương thức, bỏ "Gối nợ vụ mùa".

### M11. Kho: tra cứu lô, hạn dùng, biến động

| Cần hiển thị | API |
|---|---|
| Tồn theo sản phẩm | chưa có API tổng hợp (§7); tạm lấy `GET /api/inventory/lots` rồi gộp theo `storeProductId` |
| Các lô của một sản phẩm | `GET /api/inventory/lots?storeProductId=&hasStock=&status=&expiringBefore=&search=` — mỗi lô: `lotNumber`, `expiryDate`, `isExpired`, `status`, `quantityOnHand`, `quantityReserved`, `quantityAvailable`, `averageUnitCost` |
| Lịch sử biến động kho | `GET /api/inventory/stock-movements?type=&fromDate=&toDate=` (type: `STOCK_IN, SALE, RETURN_IN, ADJUSTMENT_IN, ADJUSTMENT_OUT, REVERSAL`) và `GET /api/inventory/stock-movements/{id}` (từng dòng lô, số âm = xuất) |
| Khoá/cách ly một lô | `POST /api/inventory/lots/{id}/status` `{ "status": "BLOCKED" }` — chỉ Đại lý/Admin |
| Nhập hàng | phiếu nhập kho `/api/goods-receipts` (tạo → thêm dòng → `POST …/{id}/confirm`; nhập Excel: `/api/goods-receipts/import`) |
| Điều chỉnh/kiểm kê tay | **chưa có API** (thuộc luồng 4) — không dựng nút "Điều chỉnh" |

Quy tắc hiển thị: `quantityAvailable = quantityOnHand − quantityReserved` (hàng đã giữ cho đơn chưa giao không bán thêm được). "Sắp hết" khi tổng khả dụng ≤ `minStockLevelBase`; "Hết hàng" khi bằng 0. Lô `isExpired` hoặc `BLOCKED`/`QUARANTINED` hiển thị cảnh báo và **không bán được**.
Tồn kho chỉ thay đổi bằng **phiếu kho**: không có thao tác "sửa số tồn trực tiếp".

Tham khảo thiết kế: `InventoryPage` có form "Ghi nhận biến động kho" cho phép nhập số lượng tự do — **bỏ phần nhập tay**; chỉ giữ tra cứu và lịch sử.

### M12. Bảng giá (Đại lý)

Chưa có thiết kế; cần một màn mới cho Chủ cửa hàng/Admin (nhân viên bán hàng chỉ **đọc**).

| Chức năng | API |
|---|---|
| Danh sách bảng giá (lọc trạng thái, tìm theo mã/tên) | `GET /api/price-lists?status=&isWalkInDefault=&search=` |
| Tạo bảng giá (nháp) | `POST /api/price-lists` `{ code, name, effectiveFrom, effectiveTo?, isWalkInDefault, description? }` — `code` duy nhất, không đổi được sau khi tạo |
| Sửa thông tin | `PUT /api/price-lists/{id}` |
| Nhập/sửa giá hàng loạt (≤ 500 dòng/lần) | `PUT /api/price-lists/{id}/items` `{ "items": [ { storeProductId, productPackagingId, sellingPrice } ] }` → `{ created, updated }` |
| Xem giá | `GET /api/price-lists/{id}/items?search=` |
| Xoá một dòng giá | `DELETE /api/price-lists/{id}/items/{itemId}` |
| Kích hoạt / ngưng | `POST /api/price-lists/{id}/activate`, `…/deactivate` |
| Xoá bảng giá | `DELETE /api/price-lists/{id}` (chỉ bảng nháp chưa từng dùng) |

Quy tắc: chỉ **một** bảng giá khách lẻ (`isWalkInDefault`) được `ACTIVE`; muốn thay thì ngưng bảng cũ trước. Chỉ định giá cho **quy cách bán** đang hoạt động. Một dòng sai thì **cả lần nhập bị từ chối**
(`422`, `errors["items[i]"]` cho từng dòng sai). Không có bảng giá khách lẻ đang hoạt động → mọi lần tạo đơn trả `422`: hãy hiện cảnh báo rõ trên màn Bảng giá và màn M3.
Gợi ý: lưới nhập giá (sản phẩm × quy cách) với nút "Lưu tất cả", đọc Excel ở FE rồi gọi API này.

### M13. Báo cáo bán hàng và dashboard (Đại lý)

`GET /api/reports/sales?fromDate=2026-10-01&toDate=2026-10-31&groupBy=DAY`

```json
{ "fromDate": "2026-10-01", "toDate": "2026-10-31", "groupBy": "DAY",
  "rows": [ { "key": "2026-10-02", "label": "2026-10-02", "orderCount": 12, "fulfilledValue": 15250000, "costOfGoods": 11800000,
              "grossProfit": 3450000, "returnValue": 250000, "netSales": 15000000 } ],
  "totals": { "orderCount": 12, "fulfilledValue": 15250000, "costOfGoods": 11800000, "grossProfit": 3450000, "returnValue": 250000, "netSales": 15000000 } }
```

- `groupBy`: `DAY` (mặc định, khoá là ngày), `PRODUCT` (nhãn `Tên (SKU)`), `STAFF` (nhãn tên nhân viên tạo đơn), `CUSTOMER_GROUP` (nhãn tên nhóm; khách lẻ là `WALK_IN` "Khách lẻ"; khách chưa có nhóm là `UNGROUPED`).
- `fromDate` và `toDate` **bắt buộc**, khoảng tối đa **366 ngày**; sai → `400`.
- **Doanh thu tính lúc giao hàng**, không phải lúc tạo đơn: đơn huỷ hoặc chưa giao không có doanh thu. Một đơn giao hai ngày xuất hiện ở hai hàng ngày nhưng `totals.orderCount` đếm một lần. Ghi chú này nên hiện thành chú thích trên màn hình.
- `grossProfit = fulfilledValue − costOfGoods` (trước trả hàng); `netSales = fulfilledValue − returnValue`. `returnValue` là 0 cho tới khi luồng 4 hoàn tất trả hàng.
- Dashboard Đại lý: *Doanh thu theo ngày* = `DAY`; *Top sản phẩm* = `PRODUCT`; *Theo nhân viên* = `STAFF`; *Theo nhóm khách* = `CUSTOMER_GROUP`.

**Dashboard của nhân viên bán hàng:** vai trò này **không được** gọi báo cáo (`403`). Chỉ hiển thị số liệu việc-cần-làm lấy từ danh sách đơn: *Đơn cần xử lý* =
`totalCount` của `GET /api/orders?status=PENDING_CONFIRMATION&pageSize=1` cộng `status=CONFIRMED`; *Đơn tạo hôm nay* từ `GET /api/orders?fromDate=hôm-nay&toDate=hôm-nay`.
**Không** hiển thị "doanh số/doanh thu" ở dashboard bán hàng (số cộng từ đơn tạo ra khác doanh thu báo cáo). Các thẻ **Công nợ quá hạn** và **Mua chịu chờ duyệt** chưa có API (luồng 3) — bỏ khỏi bản này.

### 2.2 Màn hình trong thiết kế tham khảo chưa thuộc luồng 1

**Nông dân**, **Công nợ**, **Công nợ theo vụ mùa**, **Yêu cầu mua chịu**, **Giao hàng**, **AI**, **Hoạt động**: thuộc luồng khác hoặc chưa có API ở luồng 1 — xem §8. Đừng nối chúng vào API của luồng 1.

---

## 3. Kịch bản đầu–cuối có JSON mẫu

### S1. Bán nhanh cho khách lẻ (1 dòng, 2 lô)
1. `POST /api/counter-sales/preview` (M7) → xem lô và tổng tiền.
2. Nhân viên xác nhận → `POST /api/counter-sales` với `lots`.
3. Nhận `{order(COMPLETED), payment(PAID)}` → in/hiển thị hoá đơn bằng dữ liệu `order` (mã `orderNumber`, từng dòng, `totalAmount`) và `payment.paymentNumber`.

### S2. Đơn nhiều bước, khách đặt cọc rồi lấy sau
1. `POST /api/orders` → `PENDING_CONFIRMATION`, tổng 6.850.000.
2. `POST /api/payments/cash` `{ orderId, amount: 3425000 }` → tóm tắt: `paidAmount 3425000`, `remainingToPay 3425000`.
3. Hôm sau khách trả nốt: `POST /api/payments/cash` `{ amount: 3425000 }` → `remainingToPay 0`.
4. `GET …/fefo-suggestions` → `POST …/confirm` → `CONFIRMED`.
5. Khách đến lấy: mở hộp thoại giao (M6) → `POST …/pickup` → `COMPLETED`.

### S3. Giao từng phần rồi hủy phần còn lại
1. Đơn 50 bao, đã trả đủ, đã xác nhận.
2. `POST …/pickup` giao 20 bao → `PARTIALLY_FULFILLED` (`remainingBaseQuantity` của dòng = 30).
3. Khách không lấy nữa: `POST …/items/{itemId}/cancel-remaining` `{ reason }` → `PARTIALLY_CANCELLED`.
4. `GET …/payments` → `refunds` có khoản hoàn cho phần chưa giao (ví dụ 30 bao × đơn giá). Cửa hàng trả lại tiền cho khách.

### S4. Hủy đơn đã thanh toán
1. Đơn đã trả 100.000 bằng tiền mặt, đang `CONFIRMED`.
2. `POST …/cancel` `{ reason: "Hết hàng" }` → `refunds[0] = { refundNumber, refundMethod: "CASH", amount: 100000 }`, hàng giữ được trả lại kho.

---

## 4. Tham chiếu từng API

Cột **Quyền**: `Vận hành` = Admin, Chủ cửa hàng, Nhân viên bán hàng; `Quản lý` = Admin, Chủ cửa hàng; `Đọc` = thêm Nhân viên giao hàng.

### 4.1 Đơn hàng (`api/orders`)

| Method | Route | Quyền | Body | Kết quả |
|---|---|---|---|---|
| POST | `/api/orders` | Vận hành | `CreateCounterOrderRequest` | `201 OrderResponse` |
| GET | `/api/orders` | Vận hành | query: `status, customerType, settlementType, fulfillmentType, source, farmerProfileId, fromDate, toDate, search, page, pageSize` | `200 Paged<OrderListItem>` |
| GET | `/api/orders/{id}` | Vận hành | — | `200 OrderResponse` |
| PUT | `/api/orders/{id}` | Vận hành | `{ addressId?, deliveryAddress?, note? }` | `200 OrderResponse` (chỉ khi `PENDING_CONFIRMATION`) |
| POST | `/api/orders/{id}/items` | Vận hành | `OrderItemRequest` | `200 OrderResponse` |
| PUT | `/api/orders/{id}/items/{itemId}` | Vận hành | `{ quantity }` | `200 OrderResponse` |
| PUT | `/api/orders/{id}/items/{itemId}/price` | Vận hành | `{ unitPrice, reason }` | `200 OrderResponse` |
| DELETE | `/api/orders/{id}/items/{itemId}/price` | Vận hành | — | `200 OrderResponse` (về giá gợi ý) |
| DELETE | `/api/orders/{id}/items/{itemId}` | Vận hành | — | `200 OrderResponse` |
| GET | `/api/orders/{id}/fefo-suggestions` | Vận hành | — | `200 FefoSuggestionResponse` |
| POST | `/api/orders/{id}/confirm` | Vận hành | — | `200 OrderResponse` |
| POST | `/api/orders/{id}/start-preparing` | Vận hành | — | `200 OrderResponse` |
| POST | `/api/orders/{id}/mark-ready` | Vận hành | — | `200 OrderResponse` |
| GET | `/api/orders/{id}/reservation` | Vận hành | — | `200 ReservationResponse` (`404` nếu đơn chưa từng được giữ hàng) |
| POST | `/api/orders/{id}/pickup` | Vận hành | `PickupRequest` | `200 OrderResponse` |
| POST | `/api/orders/{id}/items/{itemId}/cancel-remaining` | Vận hành | `{ reason }` | `200 OrderResponse` |
| POST | `/api/orders/{id}/cancel` | Vận hành | `{ reason }` | `200 OrderCancellationResponse` |
| POST | `/api/counter-sales/preview` | Vận hành | `CounterSaleRequest` | `200 CounterSalePreviewResponse` |
| POST | `/api/counter-sales` | Vận hành | `CounterSaleRequest` (có `lots`) | `201 CounterSaleResponse` |

Ràng buộc đầu vào chính: `reason` (huỷ) bắt buộc ≤ 1000 ký tự, `reason` (hủy phần còn lại, ghi đè giá) ≤ 500; ghi chú ≤ 1000; tên khách ≤ 150; SĐT ≤ 20 ký tự.
Chỉ `customerType = WALK_IN` dùng được hiện nay.

### 4.2 Thanh toán

| Method | Route | Quyền | Body / query | Kết quả |
|---|---|---|---|---|
| POST | `/api/payments/cash` | Vận hành | `{ paymentContext: "ORDER_PAYMENT", orderId, amount, note? }` | `201 PaymentResponse` |
| GET | `/api/payments` | Vận hành | `paymentContext, paymentMethod, status, orderId, farmerProfileId, fromDate, toDate, search (mã PM-), page, pageSize` | `200 Paged<PaymentListItem>` |
| GET | `/api/payments/{id}` | Vận hành | — | `200 PaymentResponse` |
| GET | `/api/orders/{id}/payments` | Vận hành | — | `200 OrderPaymentSummary` |
| POST | `/api/payments/{id}/cancel` | Vận hành | `{ reason? }` | `200 PaymentResponse` (chỉ khoản `PENDING`) |
| GET | `/api/me/payments`, `/api/me/payments/{id}`, `/api/me/orders/{id}/payments` | Farmer | — | của chính người đăng nhập |

`paymentContext` còn có `DEBT_REPAYMENT` (trả nợ): **chưa dùng** ở luồng 1 (§8). `paymentMethod`: `CASH`, `PAYOS`.

### 4.3 Bảng giá, báo cáo, kho, danh mục

| Nhóm | Route | Quyền |
|---|---|---|
| Bảng giá | `GET /api/price-lists`, `GET /api/price-lists/{id}`, `GET /api/price-lists/{id}/items` | Vận hành |
| | `POST/PUT/DELETE /api/price-lists…`, `PUT /api/price-lists/{id}/items`, `DELETE …/items/{itemId}`, `POST …/activate`, `POST …/deactivate` | Quản lý |
| Báo cáo | `GET /api/reports/sales` | Quản lý |
| Kho | `GET /api/inventory/lots`, `…/lots/{id}`, `GET /api/inventory/stock-movements`, `…/{id}` | Vận hành |
| | `POST /api/inventory/lots/{id}/status` | Quản lý |
| Danh mục (công khai) | `GET /api/catalog/products`, `…/products/{id}`, `…/categories`, `…/brands` | không cần token |
| Sản phẩm nội bộ | `GET /api/products`, `GET /api/products/{id}` (đủ quy cách), `GET /api/store-products` | Đọc |
| Đơn vị, danh mục | `GET /api/units`, `GET /api/categories`, `/tree` | Đọc |

### 4.4 Vòng đời đơn (tóm tắt)

```
PENDING_CONFIRMATION --confirm--> CONFIRMED --start-preparing--> PREPARING --mark-ready--> READY_FOR_FULFILLMENT
        |                              \_______________ pickup (từng phần được) ______________/
        |                                                   |                |
      cancel                                      PARTIALLY_FULFILLED --pickup nốt--> COMPLETED
        v                                                   |
    CANCELLED                                  cancel-remaining --> PARTIALLY_CANCELLED
```

`cancel` dùng được từ `PENDING_CONFIRMATION` đến `READY_FOR_FULFILLMENT`; `cancel-remaining` từ `CONFIRMED` đến `PARTIALLY_FULFILLED`.

---

## 5. Lỗi và cách hiển thị

Mọi lỗi nghiệp vụ trả `application/problem+json`:

```json
{ "title": "Business rule violated.", "status": 422, "detail": "Order 'OD-20261004-0001' has 250000 left to pay; the payment is 300000.",
  "traceId": "0HN…", "errors": { "items[0]": ["SKU-01: 6 base units short of the 125 needed."] } }
```

| Mã | Ý nghĩa | FE nên làm |
|---|---|---|
| 400 | Dữ liệu gửi lên sai định dạng (`errors` theo tên trường camelCase, ví dụ `items[0].quantity`, `customerType`, `address`) | tô đỏ ô nhập tương ứng |
| 401 | Chưa/hết hạn token, hoặc tài khoản bị khoá/xoá | về trang đăng nhập |
| 403 | Vai trò không được phép (hoặc tài khoản không hoạt động) | ẩn/khoá chức năng theo vai trò; thông báo "Bạn không có quyền" |
| 404 | Không tìm thấy (hoặc đã xoá) | quay về danh sách, tải lại |
| 409 | Xung đột ghi (có người vừa sửa/tạo cùng lúc) | tải lại dữ liệu rồi cho thao tác lại |
| 422 | Vi phạm quy tắc nghiệp vụ (sai trạng thái, thiếu hàng, vượt số tiền…) | hiển thị `detail`; nếu có `errors` → gắn vào từng dòng |
| 429 | Gọi quá nhanh (đăng nhập) | báo chờ |
| 503 | Dịch vụ ngoài không sẵn sàng | báo thử lại |

**Khoá của `errors` ở 422:** `items[i]` với `i` là **vị trí dòng hàng trong đơn theo thứ tự `items` mà API trả về** (dòng đầu = 0). Với bán nhanh `i` là vị trí trong
mảng bạn gửi. Mỗi giá trị là mảng thông điệp tiếng Anh, bắt đầu bằng **SKU** của dòng để dễ nhận.

**Thông điệp lỗi hiện là tiếng Anh.** Đừng hiển thị nguyên văn cho nhân viên; đề xuất ánh xạ theo (mã trạng thái + hành động) sang tiếng Việt, và giữ `detail` gốc trong phần "chi tiết kỹ thuật" thu gọn kèm `traceId` để báo BE.
Các tình huống thường gặp:

| Khi gọi | Tình huống (nội dung `detail` chứa) | Gợi ý hiển thị |
|---|---|---|
| tạo đơn | `The price list has no price for this packaging.` | "Quy cách này chưa có giá bán. Báo chủ cửa hàng nhập bảng giá." |
| tạo đơn | `No price list applies` | "Chưa có bảng giá khách lẻ đang áp dụng." |
| tạo đơn / sửa dòng | `A price different from the suggested price needs a reason.` | "Nhập lý do khi đổi giá." |
| tạo đơn | `The product is not for sale.` / `Only ACTIVE sale packagings can be ordered.` | "Sản phẩm/quy cách này hiện không bán." |
| sửa đơn | `… is CONFIRMED; this action is not allowed.` | "Đơn đã xác nhận, không sửa được nữa." |
| thêm dòng | `already on the order` | "Sản phẩm này đã có trong đơn, hãy đổi số lượng." |
| xác nhận | `short of the … needed` (có `errors`) | "Không đủ hàng: thiếu N (đơn vị cơ sở)" gắn vào dòng |
| xác nhận | `can no longer be sold` (có `errors`) | "Sản phẩm trong đơn đã ngừng bán. Xoá dòng hoặc huỷ đơn." |
| xác nhận | `Credit sales are not available yet` | "Chưa hỗ trợ bán nợ." |
| thu tiền | `left to pay` | "Số tiền vượt quá số còn phải trả." |
| thu tiền | `cannot take a payment` | "Đơn đã huỷ/hoàn thành, không thu thêm được." |
| giao hàng | `whole packages` | "Tổng số lượng giao phải là bội số của quy cách." |
| giao hàng | `exceed the … still to hand over` | "Vượt quá số còn phải giao." |
| giao hàng | `cannot be sold (status …` | "Lô này không bán được (hết hạn/đang khoá)." |
| giao hàng | `is a DELIVERY order` | "Đây là đơn giao tận nơi." |
| huỷ đơn | `already partly handed over` | "Đơn đã giao một phần: hãy hủy phần còn lại của từng dòng." |
| huỷ đơn | `still waiting on payOS` | "Còn thanh toán online đang chờ, cần huỷ trước." *(chưa xảy ra vì chưa có payOS)* |
| bán nhanh | `The lots handed over are required.` (400, `items[i].lots`) | "Chọn lô cho từng dòng." |
| bán nhanh | `total above 0` | "Tổng tiền phải lớn hơn 0." |
| bất kỳ (409) | `changed by someone else` / `at the same time` | "Dữ liệu vừa thay đổi, đã tải lại." |

> Đề xuất cho BE (§7): thêm trường `code` ổn định vào lỗi để FE không phải so khớp chuỗi.

---

## 6. Thay đổi UI bắt buộc so với thiết kế tham khảo

Thiết kế UI/UX hiện có chỉ để tham khảo; **luồng ở §2 là chuẩn**, nên UI đổi theo luồng. Bảng dưới liệt kê những gì phải đổi.
Cột *Mức*: **Bắt buộc** = giữ nguyên thì sai luồng; **Nên** = để dùng trơn tru; **Bỏ** = bỏ khỏi bản này.

| # | Mức | Thiết kế tham khảo hiện có | Theo luồng | UI phải làm |
|---|---|---|---|---|
| 1 | Bắt buộc | Form tạo đơn **một dòng**, gõ tay tên sản phẩm và đơn giá | Đơn nhiều dòng; hàng chọn từ catalog (sản phẩm + quy cách); giá do server tính; sửa giá cần lý do | Thay bằng bố cục M2 + M3 |
| 2 | Bắt buộc | Chọn **phương thức thanh toán** ngay khi tạo đơn | Tiền thu ở bước riêng (M4), nhiều lần, tiền mặt | Bỏ ô chọn phương thức khỏi form tạo đơn; thêm hộp thoại Thu tiền |
| 3 | Bắt buộc | Máy trạng thái tuyến tính `Chờ xác nhận → Đã xác nhận → Đang chuẩn bị → Đang giao hàng → Hoàn thành` và nút "bước kế tiếp" | Có bước **giữ hàng** khi xác nhận; *Đang chuẩn bị/Sẵn sàng giao* tuỳ chọn; kết thúc bằng **Giao hàng tại quầy**; có `PARTIALLY_FULFILLED`, `PARTIALLY_CANCELLED` | Dùng bảng trạng thái và bảng nút ở M9; thêm 3 trạng thái còn thiếu |
| 4 | Bắt buộc | Không có bước **chọn/xem lô** | Giao hàng và bán nhanh cần lô thực tế; xác nhận giữ hàng theo FEFO | Thêm M5 (xem lô sẽ giữ), M6 (chọn lô khi giao), bảng lô trong M7 |
| 5 | Bắt buộc | "Hoàn thành" nghĩa là đã bán và đã thanh toán | `COMPLETED` = **đã giao đủ**; thanh toán là trạng thái riêng | Luôn hiển thị hai nhãn tách biệt: trạng thái đơn và trạng thái thanh toán |
| 6 | Bắt buộc | Chặn "Xác nhận" không phụ thuộc tiền | Đơn trả tiền ngay phải **thu đủ trước khi xác nhận** (server chưa chặn) | FE tự khoá nút Xác nhận khi `paidAmount < orderTotal` |
| 7 | Bắt buộc | Số lượng và tồn dùng chung một đơn vị | `quantity` = quy cách; lô và tồn = đơn vị cơ sở | Luôn ghi rõ đơn vị, đổi qua lại bằng `conversionToBase` |
| 8 | Bắt buộc | Form "Ghi nhận biến động kho" nhập số lượng tự do; nút "Điều chỉnh" | Tồn chỉ đổi bằng **phiếu kho** (nhập hàng, giao hàng, điều chỉnh có lý do); điều chỉnh/kiểm kê thuộc luồng 4 | Bỏ nhập tay; chỉ tra cứu lô và lịch sử; nhập hàng qua phiếu nhập |
| 9 | Bắt buộc | Mã đơn `DH-…`, thanh toán `TT-…` tự sinh ở FE | Mã do server: đơn `OD-yyyyMMdd-NNNN`, thanh toán `PM-…`, hoàn tiền `RF-…`, phiếu kho `SM-…` | Hiển thị mã server trả về; không tự sinh |
| 10 | Bắt buộc | Hủy đơn là đổi nhãn trạng thái, không lý do | Hủy cần **lý do**; kết quả có **khoản phải hoàn tiền**; đơn đã giao một phần chỉ hủy được phần còn lại | Hộp thoại lý do, khối hiển thị khoản hoàn, nhánh "Hủy phần còn lại" (M8) |
| 11 | Bắt buộc | Form "Tạo thanh toán" gõ tay mã đơn, tên khách, tổng đơn | Thanh toán gắn với một đơn có sẵn; không vượt số còn phải thu | Chọn đơn rồi nhập số tiền (M4) |
| 12 | Bỏ | Phương thức **"Gối nợ vụ mùa"**, màn **Mua chịu theo vụ mùa**, "hạn mức theo vụ" | **Đã bỏ khái niệm vụ mùa.** Thời hạn nợ phụ thuộc loại khách (nhóm khách → hạng tín dụng), thuộc luồng 3, chưa có | Ẩn khỏi bản này; đổi chữ "vụ mùa" khi luồng 3 xong |
| 13 | Bỏ | Phương thức **VietQR** và hành động "Xác nhận VietQR" | payOS thuộc luồng 2, chưa có | Ẩn; hiện tại chỉ có tiền mặt |
| 14 | Bỏ | **"Cọc 50%"** là một phương thức | Không có loại này; đặt cọc = thu tiền mặt **một phần** | Bỏ khỏi danh sách phương thức; giữ nút nhanh "Đặt cọc 50%" ở M4 |
| 15 | Bỏ | Trạng thái **Đang giao hàng / Chờ giao lại / Giao thất bại**, nút "Giao cho shipper", "Xác nhận đã giao" | Thuộc **giao tận nơi** (luồng 2) | Ẩn khỏi màn bán tại quầy |
| 16 | Bỏ | Dashboard bán hàng có thẻ doanh số, công nợ quá hạn, mua chịu chờ duyệt | Báo cáo chỉ dành cho Đại lý; công nợ/mua chịu thuộc luồng 3 | Dashboard bán hàng chỉ có việc-cần-làm (M13) |
| 17 | Bỏ | "Quên mật khẩu" | Chưa có API | Tạm ẩn |
| 18 | Nên | Bước chọn vai trò trên màn đăng nhập | Vai trò do server trả (`user.role`) | Bỏ bước chọn; điều hướng theo `role` |
| 19 | Nên | Cột "Sản phẩm" của bảng đơn hiện tên và số lượng × giá của dòng đầu | Danh sách chỉ có `itemCount` | Hiển thị số dòng, hoặc nạp chi tiết khi mở |
| 20 | Nên | Mỗi hàng ở trang Thanh toán là một đơn | API tách đơn và từng khoản thanh toán | Ghép theo M10 |
| 21 | Nên | Hiển thị tên người tạo đơn/ghi nhận thanh toán | API chỉ trả id người dùng | Tạm hiển thị "Nhân viên"; xem §7 |
| 22 | Nên | Thông báo lỗi chung chung | Lỗi 422 có `errors` theo từng dòng hàng | Gắn lỗi vào đúng dòng (§5) |
| 23 | Bắt buộc (mới) | Chưa có màn **Bảng giá** và **Báo cáo bán hàng** | Chủ cửa hàng phải có bảng giá khách lẻ đang `ACTIVE` thì mới tạo được đơn | Thiết kế bổ sung M12, M13 |

---

## 7. Đề xuất bổ sung phía BE (chưa có, không chặn việc dựng UI)

Các điểm dưới đây giúp FE bớt công; **chưa làm**, chờ lead BE quyết định. FE đã có cách tạm ở các mục trên. Tài liệu này **không** đòi API đổi theo thiết kế UI;
chỉ những bổ sung giúp thể hiện đúng luồng cho gọn mới được đề xuất.

1. `OrderListItem` thêm `paidAmount`, `remainingToPay`, `paymentStatus` (tránh gọi tóm tắt cho từng hàng ở M9 và M10).
2. `PaymentListItem` thêm `orderId`, `orderNumber`.
3. API **tổng hợp tồn theo sản phẩm** (`quantityOnHand/Reserved/Available`, trạng thái *Còn hàng/Sắp hết/Hết hàng*) — thuộc luồng 4, đã có trong kế hoạch (`stock-summary`).
4. Danh sách sản phẩm cho nhân viên có sẵn **quy cách + giá + tồn** trong một lần gọi (hiện phải gọi catalog + chi tiết + lô).
5. API tra **tên người dùng** theo id cho vai trò bán hàng (hoặc trả `createdByName`, `confirmedByName`).
6. Mã lỗi ổn định `code` trong lỗi 400/422/409 để FE ánh xạ tiếng Việt chắc chắn.
7. Cho phép Nhân viên bán hàng xem báo cáo ngày của **chính mình** (nếu cần doanh thu khớp báo cáo trên dashboard bán hàng).
8. Tìm/tạo **khách hàng** (luồng 2, F2.1) — cần cho khách quen.

---

## 8. Chưa làm được vì phụ thuộc luồng khác

| Việc | Vì sao | Khi nào |
|---|---|---|
| **Khách quen** (chọn nông dân khi tạo đơn, giá theo nhóm, màn Nông dân) | chưa có API khách hàng (luồng 2, F2.1); đơn cần `farmerProfileId` | sau luồng 2 |
| **Bán nợ** (`settlementType: CREDIT`), công nợ, trả nợ (`DEBT_REPAYMENT`) | luồng 3 (F3.3–F3.5); hiện `confirm` đơn CREDIT trả `422` | sau luồng 3 |
| **payOS / VietQR** | luồng 2 (F2.4) | sau luồng 2 |
| **Giao tận nơi** (đơn `DELIVERY` tạo/xác nhận/huỷ được nhưng chưa có luồng giao) | luồng 2 (F2.6) | sau luồng 2 |
| **Ghi nhận đã hoàn tiền** cho khoản `PENDING` | luồng 4 (F4.5) | sau luồng 4 |
| **Điều chỉnh/kiểm kê kho**, trả hàng | luồng 4 | sau luồng 4 |
| Kiểm tra server "trả đủ mới xác nhận" | luồng 3 (F3.3) | FE tự chặn trong lúc chờ (§0, điều 5) |

---

## 9. Chuẩn bị môi trường và dữ liệu để chạy thử

0. **Chưa cài .NET?** Chạy API bằng Docker theo `docs/reference/RUN_WITH_DOCKER.md` (`docker compose up --build`, API ở `http://localhost:5206`; khi đó `VITE_API_URL=http://localhost:5206`).
1. Backend chạy từ `main` mới nhất (API cũ ở cổng 5206 của máy BE có thể chưa có các route này). Hỏi BE mật khẩu DB qua kênh riêng, **không** đưa bí mật vào repo FE.
2. Cần có sẵn để màn bán hàng chạy được:
   - **tài khoản nhân viên** (Admin/Chủ cửa hàng tạo bằng `POST /api/staff`);
   - **sản phẩm đang bán** có quy cách bán (`/api/products`, `/api/store-products`, đánh dấu *sellable*);
   - **một bảng giá khách lẻ đang `ACTIVE`** có giá cho các quy cách (M12) — thiếu thì mọi lần tạo đơn trả `422`;
   - **lô hàng có tồn**: nhập kho bằng phiếu nhập `/api/goods-receipts` (rồi *confirm*) hoặc nhập Excel.
3. Thử nhanh bằng Swagger trước khi viết UI. BE có sẵn script PowerShell chạy cả chuỗi (tạo đơn → ghi đè giá → thu tiền → huỷ → thu đồng thời) làm ví dụ về thứ tự gọi.
4. Dữ liệu thử có tiền tố `TEST-` trong DB dev là của BE; đừng dựa vào nó. Hỏi BE trước khi xoá hoặc sửa.
5. Biến môi trường FE gợi ý: `VITE_API_URL=https://localhost:7068`.

---

## 10. Phụ lục: kiểu TypeScript

```ts
export type Money = number
export type Uuid = string

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }

export type OrderStatus =
  | 'PENDING_CONFIRMATION' | 'CONFIRMED' | 'PREPARING' | 'READY_FOR_FULFILLMENT'
  | 'PARTIALLY_FULFILLED' | 'COMPLETED' | 'CANCELLED' | 'PARTIALLY_CANCELLED'

export interface OrderItemResponse {
  id: Uuid; storeProductId: Uuid; productPackagingId: Uuid; sku: string; productName: string; packagingName: string
  quantity: number; conversionToBase: number; baseQuantity: number
  suggestedUnitPrice: Money; unitPrice: Money; lineTotalAmount: Money
  priceOverridden: boolean; overrideReason: string | null; overriddenBy: Uuid | null
  fulfilledBaseQuantity: number; cancelledBaseQuantity: number; remainingBaseQuantity: number
  status: 'PENDING' | 'PARTIALLY_FULFILLED' | 'FULFILLED' | 'CANCELLED' | 'PARTIALLY_CANCELLED'
}

export interface OrderResponse {
  id: Uuid; orderNumber: string; source: 'COUNTER' | 'FARMER_WEB' | 'FARMER_MOBILE'
  customerType: 'REGISTERED' | 'WALK_IN'; farmerProfileId: Uuid | null; customerName: string; customerPhone: string | null
  customerGroupId: Uuid | null; priceListId: Uuid | null
  settlementType: 'FULL_PAYMENT' | 'CREDIT'; creditTermDays: number | null
  fulfillmentType: 'PICKUP' | 'DELIVERY'
  deliveryAddress: { recipientName: string; recipientPhone: string; addressLine: string; ward: string | null; district: string | null
                     province: string; latitude: number | null; longitude: number | null } | null
  status: OrderStatus; subtotalAmount: Money; totalAmount: Money; note: string | null
  createdBy: Uuid; createdAt: string; confirmedBy: Uuid | null; confirmedAt: string | null
  pickupCompletedBy: Uuid | null; pickupCompletedAt: string | null; completedAt: string | null
  cancelledBy: Uuid | null; cancelledAt: string | null; cancelReason: string | null
  version: number; items: OrderItemResponse[]
}

export interface OrderListItem {
  id: Uuid; orderNumber: string; source: string; customerType: string; customerName: string; customerPhone: string | null
  settlementType: string; fulfillmentType: string; status: OrderStatus; totalAmount: Money; itemCount: number
  createdAt: string; confirmedAt: string | null
}

export interface OrderItemRequest {
  storeProductId: Uuid; productPackagingId: Uuid; quantity: number; unitPrice?: Money | null; overrideReason?: string | null
}

export interface CreateCounterOrderRequest {
  customerType: 'WALK_IN' | 'REGISTERED'; settlementType: 'FULL_PAYMENT' | 'CREDIT'; fulfillmentType: 'PICKUP' | 'DELIVERY'
  items: OrderItemRequest[]; farmerProfileId?: Uuid | null; customerName?: string | null; customerPhone?: string | null
  addressId?: Uuid | null; deliveryAddress?: unknown | null; note?: string | null
}

export interface FefoLotSuggestion { inventoryLotId: Uuid; lotNumber: string | null; expiryDate: string | null; availableBaseQuantity: number; suggestedBaseQuantity: number }
export interface FefoItemSuggestion { orderItemId: Uuid; baseQuantity: number; remainingBaseQuantity: number; lots: FefoLotSuggestion[]; shortageBaseQuantity: number }
export interface FefoSuggestionResponse { orderId: Uuid; items: FefoItemSuggestion[] }

export interface ReservationResponse {
  id: Uuid; orderId: Uuid; status: 'ACTIVE' | 'PARTIALLY_CONSUMED' | 'CONSUMED' | 'RELEASED' | 'CANCELLED'
  reservedAt: string; reservedBy: Uuid; releasedAt: string | null; releasedBy: Uuid | null; releaseReason: string | null
  items: { id: Uuid; orderItemId: Uuid; inventoryLotId: Uuid; lotNumber: string | null; expiryDate: string | null
           reservedBaseQuantity: number; consumedBaseQuantity: number; releasedBaseQuantity: number; remainingBaseQuantity: number }[]
}

export interface PickupRequest {
  items: { orderItemId: Uuid; lots: { inventoryLotId: Uuid; baseQuantity: number }[] }[]; note?: string | null
}

export interface PaymentListItem {
  id: Uuid; paymentNumber: string; paymentContext: 'ORDER_PAYMENT' | 'DEBT_REPAYMENT'; paymentMethod: 'CASH' | 'PAYOS'
  amount: Money; status: 'PENDING' | 'PAID' | 'FAILED' | 'CANCELLED' | 'PARTIALLY_REFUNDED' | 'REFUNDED'
  payerName: string | null; confirmedAt: string | null; initiatedAt: string
}

export interface PaymentResponse extends PaymentListItem {
  currency: string; payerFarmerProfileId: Uuid | null; confirmationSource: 'STAFF' | 'PAYOS_WEBHOOK' | null
  confirmedBy: Uuid | null; checkoutUrl: string | null; providerOrderCode: number | null
  failedAt: string | null; cancelledAt: string | null; note: string | null; unallocatedAmount: Money
  allocations: { id: Uuid; allocationType: 'ORDER' | 'DEBT'; orderId: Uuid | null; orderNumber: string | null
                 debtEntryId: Uuid | null; entryNumber: string | null; allocatedAmount: Money
                 prepaymentConsumedAmount: Money; status: 'ACTIVE' | 'REVERSED'; allocatedAt: string }[]
}

export interface RefundResponse {
  id: Uuid; refundNumber: string; source: 'ORDER' | 'SALES_RETURN'; salesReturnId: Uuid | null; orderId: Uuid | null
  originalPaymentId: Uuid | null; refundMethod: 'CASH' | 'BANK_TRANSFER' | 'OTHER_EXTERNAL'; amount: Money
  status: 'PENDING' | 'COMPLETED' | 'FAILED' | 'CANCELLED'; externalReference: string | null; proofFileUrl: string | null
  requestedBy: Uuid; requestedAt: string; completedBy: Uuid | null; completedAt: string | null
  cancelledBy: Uuid | null; cancelledAt: string | null; cancelReason: string | null; note: string | null
}

export interface OrderPaymentSummary {
  orderId: Uuid; orderTotal: Money; paidAmount: Money; availablePrepayment: Money; consumedPrepayment: Money
  remainingToPay: Money; payments: PaymentListItem[]; refunds: RefundResponse[]
}

export interface OrderCancellationResponse {
  order: OrderResponse
  refunds: { refundId: Uuid; refundNumber: string; paymentId: Uuid; refundMethod: 'CASH' | 'BANK_TRANSFER'; amount: Money }[]
}

export interface CounterSaleRequest {
  customerType: 'WALK_IN'; customerName?: string | null; customerPhone?: string | null; note?: string | null
  items: (OrderItemRequest & { lots?: { inventoryLotId: Uuid; baseQuantity: number }[] })[]
}
export interface CounterSalePreviewResponse {
  customerGroupId: Uuid | null; priceListId: Uuid | null; totalAmount: Money
  items: { storeProductId: Uuid; productPackagingId: Uuid; sku: string; productName: string; packagingName: string
           quantity: number; conversionToBase: number; baseQuantity: number; suggestedUnitPrice: Money; unitPrice: Money
           lineTotalAmount: Money; lots: FefoLotSuggestion[]; shortageBaseQuantity: number }[]
}
export interface CounterSaleResponse { order: OrderResponse; payment: PaymentResponse }

export interface SalesReportRow {
  key: string; label: string; orderCount: number; fulfilledValue: Money; costOfGoods: Money
  grossProfit: Money; returnValue: Money; netSales: Money
}
export interface SalesReportResponse {
  fromDate: string; toDate: string; groupBy: 'DAY' | 'PRODUCT' | 'STAFF' | 'CUSTOMER_GROUP'; rows: SalesReportRow[]
  totals: Omit<SalesReportRow, 'key' | 'label'>
}

export interface InventoryLot {
  id: Uuid; storeProductId: Uuid; productId: Uuid; sku: string; productName: string; lotNumber: string | null
  manufacturingDate: string | null; expiryDate: string | null; isExpired: boolean
  status: 'ACTIVE' | 'QUARANTINED' | 'EXPIRED' | 'BLOCKED' | 'DEPLETED'
  quantityOnHand: number; quantityReserved: number; quantityAvailable: number
  averageUnitCost: Money | null; totalCostValue: Money
}

export interface PriceList {
  id: Uuid; code: string; name: string; description: string | null; effectiveFrom: string; effectiveTo: string | null
  isWalkInDefault: boolean; status: 'DRAFT' | 'ACTIVE' | 'INACTIVE'; itemCount: number
  groups: { id: Uuid; code: string; name: string }[]; createdAt: string
}
export interface PriceListItem {
  id: Uuid; storeProductId: Uuid; productPackagingId: Uuid; sku: string; productName: string; packagingName: string; sellingPrice: Money
}

export interface ProblemDetails { title: string; status: number; detail?: string; traceId?: string; errors?: Record<string, string[]> }
```
