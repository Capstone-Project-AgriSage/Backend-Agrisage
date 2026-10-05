# Hướng dẫn FE tích hợp API — Luồng 2: Đặt hàng online, thanh toán payOS và giao hàng

Viết cho: đội FE của **web nông dân** (`farmer_web_agrisage`), **app mobile nông dân** và **web quản lý** (`Management-Webs`:
Bán hàng, Đại lý, Giao hàng). Tài liệu chỉ nói về luồng 2: luồng nghiệp vụ, các màn hình **cần có để thể hiện đúng luồng**, và API của từng màn.

> **Luồng là chuẩn, UI đi theo luồng.** Màn hình hiện có (đang chạy bằng dữ liệu mock như `mockCart.ts`, `mockOrders.ts`) chỉ là tham khảo bố cục.
> Chỗ nào UI không khớp luồng thì **sửa UI**, không đổi luồng hay API theo UI.

Nguồn sự thật: code trên nhánh `main` của backend. Hợp đồng chi tiết: `docs/reference/api-flows/FLOW_2_ONLINE_ORDER_DELIVERY.md`.
Swagger (`/swagger`, chỉ bật ở Development) liệt kê đủ route và DTO. Khi tài liệu này và Swagger khác nhau, **Swagger đúng** và xin báo BE để sửa tài liệu.
Phần chung (địa chỉ API, CORS, đăng nhập, quy ước JSON, mẫu hàm `api()`) **giống hệt luồng 1**: xem `FE_GUIDE_FLOW_1.md` §1.

---

## Mục lục

0. [Đọc trước: 10 điều quan trọng](#0-đọc-trước-10-điều-quan-trọng)
1. [Vai trò, app và đăng ký](#1-vai-trò-app-và-đăng-ký)
2. [Luồng tổng thể](#2-luồng-tổng-thể)
3. [App nông dân (web + mobile)](#3-app-nông-dân-web--mobile)
4. [Web quản lý: Bán hàng / Đại lý](#4-web-quản-lý-bán-hàng--đại-lý)
5. [Web quản lý: Giao hàng (tài xế)](#5-web-quản-lý-giao-hàng-tài-xế)
6. [Kịch bản đầu–cuối có JSON mẫu](#6-kịch-bản-đầucuối-có-json-mẫu)
7. [Lỗi và cách hiển thị](#7-lỗi-và-cách-hiển-thị)
8. [Chưa làm được hoặc phụ thuộc luồng khác](#8-chưa-làm-được-hoặc-phụ-thuộc-luồng-khác)
9. [Chạy thử ở máy local](#9-chạy-thử-ở-máy-local)
10. [Phụ lục: kiểu TypeScript](#10-phụ-lục-kiểu-typescript)

---

## 0. Đọc trước: 10 điều quan trọng

1. **Nông dân chỉ dùng `/api/me/...`.** Danh tính lấy từ token; FE **không bao giờ** gửi `userId`/`farmerProfileId` của chính mình.
   Id của người khác → `404` (không phải 403), để không lộ id.
2. **Giỏ hàng không lưu giá.** Mỗi lần gọi API giỏ, server tính lại giá theo bảng giá hiện hành. Dòng có `isAvailable: false`
   (`NOT_SELLABLE`, `NO_PRICE`) phải được người dùng **xoá** thì mới đặt hàng được (đặt hàng sẽ trả 422).
3. **Đặt hàng lấy hàng từ giỏ**, không gửi danh sách dòng. Sau khi đặt thành công giỏ cũ đóng lại; `GET /api/me/cart` trả giỏ rỗng (`id: null`).
4. **`quantity` / `plannedQuantity` là số quy cách bán** (bao, thùng…). Mọi trường `…BaseQuantity` là **đơn vị cơ sở** của kho.
   Ví dụ: 2 thùng × 10 gói = `plannedBaseQuantity` 20. Màn giao hàng làm việc theo **lô**, luôn bằng đơn vị cơ sở.
5. **payOS: tiền chỉ được tính là đã trả khi server nói PAID.** Trang `returnUrl` sau khi thanh toán **không chứng minh gì**:
   FE chỉ đọc `orderCode`, rồi hỏi API (`GET /api/me/payments/{id}`, hoặc `POST /api/payments/{id}/sync` khi cần). Số tiền payOS phải là **số nguyên VND**.
6. **Hàng chỉ rời kho khi lần giao thành công.** Phiếu giao, phân công, xuất phát đều chưa trừ kho. Lần giao có giao được hàng
   thì server tự ghi xuất kho và cập nhật đơn.
7. **Ảnh bằng chứng: upload trước, gửi URL sau.** `POST /api/files/delivery-proofs` (multipart) → lấy `url` → gửi vào `proofImageUrl` /
   `evidenceImageUrl`. Server chỉ nhận URL do chính API đó trả về.
8. **Tài xế chỉ thấy phiếu được giao cho mình.** Phiếu của người khác → `404`. Tài xế không lập phiếu, không phân công, không xuất phát, không huỷ.
9. **Không có idempotency key.** Khoá nút (loading) từ lúc bấm đến khi có phản hồi: bấm đúp "Đặt hàng" sẽ tạo hai đơn, bấm đúp "Thanh toán" tạo hai link.
10. **409** = có thao tác khác vừa chạy cùng lúc → tải lại rồi cho thử lại. **422** = vi phạm quy tắc → hiện `detail`, nếu có `errors`
    thì gắn theo dòng. **503** = payOS hoặc kho ảnh tạm không dùng được → báo "thử lại sau", không phải lỗi người dùng.

---

## 1. Vai trò, app và đăng ký

| App | Vai trò (`role`) | Dùng trong luồng 2 |
|---|---|---|
| Web/mobile nông dân | `FARMER` | Hồ sơ, địa chỉ, giỏ hàng, đặt hàng, thanh toán payOS, theo dõi giao hàng |
| Web quản lý — Bán hàng | `SALES_STAFF` | Xác nhận đơn online, lập phiếu giao, phân công, đổi lô, xuất phát, huỷ phiếu, xử lý sự cố, xem địa chỉ khách |
| Web quản lý — Đại lý | `STORE_OWNER` | Mọi thứ của Bán hàng **và** báo cáo giao hàng |
| Web quản lý — Giao hàng | `DELIVERY_STAFF` | Danh sách phiếu của mình, bắt đầu / kết thúc lần giao, chụp ảnh, báo sự cố |
| Admin | `ADMIN` | Như Đại lý về quyền API. **Admin không phải thành viên cửa hàng nên không thực hiện được lần giao** (422) |

Đăng ký nông dân: `POST /api/auth/register` `{ fullName, phoneNumber, email?, password }` → `201` + token (giống đăng nhập).
Hồ sơ nông dân được tạo cùng lúc. Tài khoản tài xế do Đại lý/Admin tạo ở `POST /api/staff` với `role: "DELIVERY_STAFF"`.

---

## 2. Luồng tổng thể

```
 NÔNG DÂN                                   CỬA HÀNG (Bán hàng / Đại lý)              TÀI XẾ
 ────────                                   ────────────────────────────              ──────
 Hồ sơ + địa chỉ
 Giỏ hàng (giá tính lại mỗi lần xem)
 Đặt hàng (DELIVERY + địa chỉ) ─────────►  Đơn PENDING_CONFIRMATION
 [Thanh toán payOS] ── webhook ─────────►  Thanh toán PAID (tự động)
 [Huỷ đơn — chỉ khi chưa xác nhận]
                                            Xác nhận đơn (giữ hàng theo FEFO)  ← luồng 1, POST /api/orders/{id}/confirm
                                            Lập phiếu giao (1 đơn có thể nhiều phiếu = nhiều chuyến)
                                            Phân công tài xế → [Đổi lô] → Xuất phát ─────►  Phiếu OUT_FOR_DELIVERY
                                                                                            Bắt đầu lần giao
                                                                                            Upload ảnh → Kết thúc lần giao
                                                                                              ├ giao hết   → DELIVERED
                                            Xuất phát lại ◄──────────────────────────────── ├ giao một phần → PARTIALLY_DELIVERED
                                                                                              └ không giao được → RETRY_PENDING
                                            Xử lý sự cố ◄─────────────────────────────────  Báo sự cố
 Theo dõi giao hàng ◄─── trạng thái phiếu + các lần giao + ảnh
 Đơn COMPLETED khi mọi dòng đã giao đủ
```

Ba nguyên tắc giao diện phải thể hiện:
1. **Đơn, thanh toán và giao hàng là ba trạng thái riêng.** Một đơn có `status` (đơn), tóm tắt thanh toán (`GET /api/me/orders/{id}/payments`)
   và danh sách phiếu giao. Hiển thị cả ba, không gộp thành một nhãn.
2. **Một đơn có thể nhiều phiếu giao, một phiếu có thể nhiều lần giao.** UI phải hiện danh sách, không giả định "1 đơn = 1 lần giao".
3. **Lô hàng là một phần của nghiệp vụ** (màn quản lý và tài xế). Phiếu giao hiện lô và hạn dùng; nông dân **không** thấy lô.

Trạng thái để hiển thị:

| Đối tượng | Giá trị |
|---|---|
| Đơn (`status`) | `PENDING_CONFIRMATION` Chờ xác nhận · `CONFIRMED` Đã xác nhận · `PREPARING` Đang chuẩn bị · `READY_FOR_FULFILLMENT` Sẵn sàng · `PARTIALLY_FULFILLED` Đã giao một phần · `COMPLETED` Hoàn thành · `CANCELLED` Đã huỷ · `PARTIALLY_CANCELLED` Huỷ phần còn lại |
| Phiếu giao | `DRAFT` Nháp · `ASSIGNED` Đã phân công · `OUT_FOR_DELIVERY` Đang giao · `PARTIALLY_DELIVERED` Đã giao một phần · `RETRY_PENDING` Chờ giao lại · `DELIVERED` Đã giao · `CANCELLED` Đã huỷ |
| Lần giao | `IN_PROGRESS` Đang giao · `SUCCESS` Thành công · `PARTIAL_SUCCESS` Giao một phần · `FAILED` Không thành công · `CANCELLED` Đã huỷ |
| Thanh toán | `PENDING` Chờ thanh toán · `PAID` Đã thanh toán · `FAILED` Thất bại/hết hạn · `CANCELLED` Đã huỷ · `PARTIALLY_REFUNDED` / `REFUNDED` |
| Sự cố | `OPEN` Đang mở · `RESOLVED` Đã xử lý |

---

## 3. App nông dân (web + mobile)

| Mã | Màn hình | API chính | Trang hiện có |
|---|---|---|---|
| N1 | Hồ sơ cá nhân | `GET/PUT /api/me/profile` | `features/account/AccountPage` |
| N2 | Sổ địa chỉ | `/api/me/addresses…` | trong `AccountPage` / `CheckoutPage` |
| N3 | Giỏ hàng | `/api/me/cart…` | `features/cart/CartPage`, `context/CartContext` (bỏ `mockCart.ts`) |
| N4 | Đặt hàng (checkout) | `POST /api/me/orders` | `features/checkout/CheckoutPage` |
| N5 | Thanh toán payOS | `POST /api/me/payments/payos`, `GET /api/me/payments/{id}` | `OrderSuccessPage` + trang mới `/payments/payos/return` |
| N6 | Đơn của tôi + chi tiết + huỷ | `GET /api/me/orders…`, `POST /api/me/orders/{id}/cancel` | `features/order/OrdersPage`, `OrderDetailPage` (bỏ `mockOrders.ts`) |
| N7 | Theo dõi giao hàng | `GET /api/me/orders/{id}/deliveries` | trong `OrderDetailPage` |

### N1. Hồ sơ cá nhân

```http
GET /api/me/profile
PUT /api/me/profile   { "fullName": "Nguyễn Văn A", "dateOfBirth": "1980-05-01", "gender": "MALE" }
```

```json
{
  "farmerProfileId": "uuid", "userId": "uuid", "fullName": "Nguyễn Văn A", "phoneNumber": "0901234567", "email": null,
  "dateOfBirth": "1980-05-01", "gender": "MALE",
  "customerGroup": { "id": "uuid", "code": "REGULAR", "name": "Khách quen" },
  "createdAt": "2026-10-05T12:24:00Z"
}
```

- `gender`: `MALE` · `FEMALE` · `OTHER` · `null`. `dateOfBirth` không được ở tương lai.
- **Số điện thoại và email không sửa ở đây** (thuộc tài khoản; chưa có API đổi). Hiển thị dạng chỉ đọc.
- `customerGroup` quyết định bảng giá của khách; chỉ hiển thị, nông dân không đổi được.

### N2. Sổ địa chỉ

| Thao tác | API | Trả về |
|---|---|---|
| Danh sách | `GET /api/me/addresses` | mảng, **địa chỉ mặc định đứng đầu** |
| Thêm | `POST /api/me/addresses` | `201` địa chỉ vừa tạo |
| Sửa | `PUT /api/me/addresses/{id}` | `200` |
| Xoá | `DELETE /api/me/addresses/{id}` | `204` |
| Đặt mặc định | `POST /api/me/addresses/{id}/set-default` | `200` |

```json
{
  "recipientName": "Nguyễn Văn A", "recipientPhone": "0901234567",
  "addressLine": "Ấp 3", "ward": "Tân Phú", "district": "Cái Răng", "province": "Cần Thơ",
  "latitude": 10.0452, "longitude": 105.7469,
  "addressType": "FARM", "isDefault": false
}
```

Quy tắc FE cần biết:
- `addressType`: `HOME` · `FARM` · `OTHER` (bắt buộc). `province`, `addressLine`, người nhận, số điện thoại bắt buộc.
- **Địa chỉ đầu tiên tự thành mặc định.** Chỉ có một địa chỉ mặc định; `isDefault: true` hoặc "Đặt mặc định" chuyển cờ sang địa chỉ đó.
- `isDefault: false` **không** bỏ được cờ mặc định; xoá địa chỉ mặc định thì **không còn** địa chỉ mặc định (gợi ý người dùng chọn lại).
- Tối đa **10 địa chỉ**; thêm cái thứ 11 → `422`. Ẩn nút "Thêm" khi đã đủ 10.
- Sau mỗi thao tác, gọi lại `GET /api/me/addresses` (cờ mặc định có thể đổi ở địa chỉ khác).

### N3. Giỏ hàng

| Thao tác | API | Trả về |
|---|---|---|
| Xem | `GET /api/me/cart` | `CartResponse` (giỏ rỗng: `id: null`, `items: []`) |
| Thêm | `POST /api/me/cart/items` `{ storeProductId, productPackagingId, quantity }` | `200 CartResponse` |
| Đổi số lượng | `PUT /api/me/cart/items/{itemId}` `{ quantity }` | `200 CartResponse` |
| Xoá một dòng | `DELETE /api/me/cart/items/{itemId}` | `200 CartResponse` |
| Xoá cả giỏ | `DELETE /api/me/cart` | `204` |

```json
{
  "id": "uuid",
  "items": [
    { "id": "uuid", "storeProductId": "uuid", "productPackagingId": "uuid",
      "sku": "NPK-16168", "productName": "NPK 16-16-8", "packagingName": "Thùng 10 gói", "imageUrl": null,
      "quantity": 2, "unitPrice": 115000.00, "lineTotalAmount": 230000.00,
      "isAvailable": true, "unavailableReason": null }
  ],
  "subtotalAmount": 230000.00,
  "priceListId": "uuid"
}
```

- `storeProductId` và `productPackagingId` lấy từ catalog (`GET /api/catalog/products/{id}`, xem `FE_GUIDE_FLOW_1.md` M2).
- **Thêm cùng sản phẩm + quy cách lần nữa sẽ cộng dồn** số lượng vào dòng cũ. "Đổi số lượng" thì **thay** số lượng.
- Chỉ thêm được quy cách **đang bán và có giá**; ngược lại `422` (hiện `detail`).
- **Thay `CartContext` bằng `CartResponse` server trả về** sau mỗi thao tác: giá, tổng tiền đều do server tính.
- Dòng `isAvailable: false`: hiện mờ kèm lý do (`NOT_SELLABLE` "Sản phẩm ngừng bán", `NO_PRICE` "Chưa có giá"), `unitPrice` = `null`,
  không tính vào `subtotalAmount`; **nút Đặt hàng bị khoá** đến khi người dùng xoá dòng đó.
- Giỏ **không giữ hàng**; còn hàng hay không chỉ biết khi cửa hàng xác nhận đơn.

### N4. Đặt hàng (checkout)

```http
POST /api/me/orders
{
  "source": "FARMER_WEB",                 // app mobile gửi "FARMER_MOBILE"
  "settlementType": "FULL_PAYMENT",       // hoặc "CREDIT" (mua chịu — xem §8)
  "fulfillmentType": "DELIVERY",          // hoặc "PICKUP" (đến cửa hàng lấy)
  "addressId": "uuid",                    // DELIVERY: đúng một trong addressId hoặc deliveryAddress
  "deliveryAddress": null,
  "note": "Giao buổi sáng"
}
```

→ `201 OrderResponse` (cùng dạng với luồng 1: `orderNumber`, `status`, `items[]`, `totalAmount`, `deliveryAddress`…).

- Dòng hàng lấy từ giỏ hiện tại. Giỏ rỗng → `422`. Dòng không còn bán được → `422` có `errors: { "items[0]": [...] }` (theo thứ tự dòng trong giỏ).
- `DELIVERY` cần **đúng một** trong `addressId` (một địa chỉ trong sổ của mình) hoặc `deliveryAddress` (nhập tay, cùng dạng N2 nhưng không có
  `addressType`/`isDefault`). `PICKUP` không gửi địa chỉ. Sai → `400`.
- Đơn **chụp lại** tên, số điện thoại, địa chỉ, giá tại thời điểm đặt; sửa sổ địa chỉ sau đó không làm đổi đơn.
- Thành công → làm mới giỏ (giỏ đã đóng) → chuyển sang N5 (thanh toán) hoặc trang chi tiết đơn.

### N5. Thanh toán payOS

```
 [Chi tiết đơn] → POST /api/me/payments/payos ─► { paymentId, checkoutUrl, qrCode, expiresAt }
       │            lưu paymentId (sessionStorage) rồi chuyển trang sang checkoutUrl (hoặc hiện QR từ qrCode)
       ▼
 [payOS] người dùng chuyển khoản
       ▼
 trình duyệt quay về  <returnUrl>?code=…&id=…&status=…&orderCode=…   (hoặc cancelUrl)
       ▼
 [Trang kết quả] GET /api/me/payments/{paymentId}  ─► PAID ? "Đã thanh toán" : "Đang chờ xác nhận…"
                 (chờ vài giây, hỏi lại; quá thời hạn link thì POST /api/payments/{paymentId}/sync)
```

```http
POST /api/me/payments/payos
{ "paymentContext": "ORDER_PAYMENT", "orderId": "uuid", "amount": null }   // amount null = trả hết phần còn lại
```

```json
{
  "paymentId": "uuid", "paymentNumber": "PM-20261005-0003", "amount": 266000.00,
  "checkoutUrl": "https://pay.payos.vn/web/…", "qrCode": "00020101021238…", "providerOrderCode": 1759650123456789,
  "expiresAt": "2026-10-05T13:00:00Z", "status": "PENDING"
}
```

- `qrCode` là **chuỗi VietQR**, FE tự vẽ thành ảnh QR (thư viện `qrcode`). Link hết hạn sau 30 phút (`expiresAt`).
- **Số tiền phải là số nguyên VND.** Số lẻ (`10000.5`) → `422`; phần lẻ trả tiền mặt tại cửa hàng. Quá số còn phải trả → `422`.
- Mỗi đơn chỉ có **một link đang chờ**: tạo link mới thì server tự huỷ link cũ. Nếu link cũ vừa được trả → `409`: tải lại đơn trước khi cho trả tiếp.
- **Không tin trang `returnUrl`:** tham số `status=PAID` trên URL có thể bị sửa tay. Chỉ `GET /api/me/payments/{id}` (status `PAID`) mới là thật.
  Tiền thường được xác nhận trong vài giây nhờ webhook; nếu sau ~1 phút vẫn `PENDING`, gọi `POST /api/payments/{paymentId}/sync` một lần.
- Người dùng bỏ thanh toán: `POST /api/me/payments/{paymentId}/cancel` → `200 PaymentResponse` (`CANCELLED`). Không cần gọi khi link đã hết hạn.
- **503** = payOS tạm không dùng được (hoặc máy chủ chưa cấu hình payOS): hiện "Thanh toán online đang gián đoạn, vui lòng thử lại sau
  hoặc thanh toán tại cửa hàng".
- Tổng đã trả của một đơn: `GET /api/me/orders/{id}/payments` (luồng 1) — dùng để ẩn nút "Thanh toán" khi đã trả đủ.
- Trả nợ bằng payOS: `paymentContext: "DEBT_REPAYMENT"`, `amount` bắt buộc, không gửi `orderId` (phụ thuộc luồng 3, xem §8).

### N6. Đơn của tôi

| Thao tác | API |
|---|---|
| Danh sách | `GET /api/me/orders?status=&fromDate=&toDate=&page=&pageSize=` → `PagedResult<OrderListItem>` (mới nhất trước) |
| Chi tiết | `GET /api/me/orders/{id}` → `OrderResponse` |
| Huỷ | `POST /api/me/orders/{id}/cancel` `{ "reason": "Đổi ý" }` (lý do không bắt buộc) → `200 OrderResponse` |

- **Chỉ huỷ được khi `status = PENDING_CONFIRMATION`.** Các trạng thái khác → `422` "liên hệ cửa hàng"; ẩn nút Huỷ.
- Huỷ đơn đã trả payOS: server tự đóng link đang chờ và tạo **yêu cầu hoàn tiền** cho khoản đã trả (cửa hàng hoàn tay). Hiện thông báo
  "Cửa hàng sẽ liên hệ hoàn tiền".
- Chi tiết đơn nên có ba khối: thông tin đơn (`OrderResponse`), thanh toán (`/payments`), giao hàng (N7).

### N7. Theo dõi giao hàng

```http
GET /api/me/orders/{orderId}/deliveries
```

```json
[
  {
    "id": "uuid", "deliveryNumber": "DL-20261005-0001", "status": "PARTIALLY_DELIVERED",
    "scheduledAt": null, "dispatchedAt": "2026-10-05T12:30:00Z", "completedAt": null,
    "assignedTo": { "fullName": "Trần Văn B", "phoneNumber": "0912345678" },
    "items": [ { "orderItemId": "uuid", "productName": "NPK 16-16-8", "packagingName": "Thùng 10 gói",
                 "plannedQuantity": 2, "deliveredQuantity": 1, "remainingQuantity": 1 } ],
    "attempts": [ { "attemptNumber": 1, "status": "FAILED", "startedAt": "…", "completedAt": "…",
                    "receiverName": null, "proofImageUrl": null, "failureReasonCode": "CUSTOMER_ABSENT" } ]
  }
]
```

- Mảng rỗng = cửa hàng chưa lập phiếu giao (phiếu nháp không hiện cho khách). Số lượng ở đây là **số quy cách** (bao, thùng).
- Hiện số điện thoại tài xế để khách gọi; hiện ảnh `proofImageUrl` của lần giao thành công.
- `failureReasonCode` → nhãn: `CUSTOMER_ABSENT` Khách vắng nhà · `UNREACHABLE` Không liên lạc được · `CUSTOMER_REFUSED` Khách từ chối nhận ·
  `DAMAGED` Hàng hư hỏng · `WEATHER` Thời tiết · `VEHICLE_ISSUE` Sự cố xe · `ADDRESS_ISSUE` Sai/khó tìm địa chỉ · `OTHER` Khác.

---

## 4. Web quản lý: Bán hàng / Đại lý

| Mã | Màn hình | Ai dùng | API chính | Trang hiện có |
|---|---|---|---|---|
| Q1 | Đơn online chờ xác nhận | Bán hàng, Đại lý | `GET /api/orders?source=FARMER_WEB…`, `POST /api/orders/{id}/confirm` | `sales/orders/OrdersPage` |
| Q2 | Lập phiếu giao từ đơn | Bán hàng, Đại lý | `POST /api/deliveries`, `GET /api/orders/{id}/deliveries` | trong chi tiết đơn |
| Q3 | Danh sách phiếu giao | Bán hàng, Đại lý | `GET /api/deliveries` | `agent/deliveries/DeliveriesPage` |
| Q4 | Chi tiết phiếu: phân công, đổi lô, xuất phát, huỷ | Bán hàng, Đại lý | `/api/deliveries/{id}…` | `agent/delivery/DeliveryPage` |
| Q5 | Xử lý sự cố | Bán hàng, Đại lý | `/api/deliveries/{id}/incidents…` | trong Q4 |
| Q6 | Báo cáo giao hàng | **Đại lý** | `GET /api/reports/deliveries` | dashboard Đại lý |
| Q7 | Địa chỉ của khách | Bán hàng, Đại lý | `GET /api/customers/{farmerProfileId}/addresses` | chi tiết khách hàng |

### Q1. Đơn online chờ xác nhận

Đơn online là đơn luồng 1 có `source` `FARMER_WEB`/`FARMER_MOBILE`: dùng chung `GET /api/orders` (lọc `source`, `status=PENDING_CONFIRMATION`)
và **xác nhận** bằng `POST /api/orders/{id}/confirm` (giữ hàng theo FEFO, xem `FE_GUIDE_FLOW_1.md` M5). Chỉ sau khi xác nhận mới lập được phiếu giao.

### Q2. Lập phiếu giao

```http
POST /api/deliveries
{
  "orderId": "uuid",
  "items": [ { "orderItemId": "uuid", "plannedQuantity": 2 } ],   // số quy cách cho chuyến này
  "deliveryAddress": null,                                       // null = địa chỉ của đơn
  "scheduledAt": "2026-10-06T01:00:00Z",
  "note": "Gọi trước 15 phút"
}
```

→ `201 DeliveryResponse` (phiếu `DRAFT`, mã `DL-yyyyMMdd-NNNN`, **lô đã được chọn sẵn theo hạn dùng** từ hàng đang giữ của đơn).

- Đơn phải là `DELIVERY` và **đã xác nhận** (`CONFIRMED`, `PREPARING`, `READY_FOR_FULFILLMENT`, `PARTIALLY_FULFILLED`); ngược lại `422`.
- **Nhiều chuyến:** lập nhiều phiếu cho cùng đơn. Tổng `plannedQuantity` của các phiếu chưa huỷ không được vượt phần còn phải giao của dòng
  → `422` có `errors: { "items[i]": [...] }`. Gợi ý số lượng mặc định = phần còn lại của dòng (`remainingBaseQuantity / conversionToBase`)
  trừ phần đã nằm trên các phiếu khác (`GET /api/orders/{id}/deliveries`).
- Phiếu đã huỷ trả lại số lượng để lập phiếu mới.

### Q3. Danh sách phiếu giao

`GET /api/deliveries?status=&orderId=&assignedToUserId=&fromDate=&toDate=&search=&page=&pageSize=` → `PagedResult<DeliveryListItem>`.
`search`: mã phiếu, mã đơn, tên hoặc số điện thoại người nhận. `fromDate/toDate` lọc theo ngày hẹn giao (hoặc ngày tạo nếu không hẹn).

### Q4. Chi tiết phiếu

`GET /api/deliveries/{id}` → `DeliveryResponse` (dòng hàng, **lô + hạn dùng**, các lần giao kèm ảnh). Các nút theo trạng thái:

| Nút | API | Khi nào hiện |
|---|---|---|
| Phân công tài xế | `POST /{id}/assign` `{ "assignedToUserId": "uuid" }` | `DRAFT`, `ASSIGNED`, `PARTIALLY_DELIVERED`, `RETRY_PENDING` |
| Đổi lô của một dòng | `PUT /{id}/items/{itemId}/lots` `{ "lots": [ { "inventoryLotId": "uuid", "baseQuantity": 20 } ] }` | như trên (trước khi xuất phát) |
| Xuất phát | `POST /{id}/dispatch` | `ASSIGNED`, `PARTIALLY_DELIVERED`, `RETRY_PENDING` (phải có tài xế) |
| Huỷ lần giao đang chạy | `POST /{id}/attempts/{attemptId}/cancel` `{ "reason": "…" }` | có lần giao `IN_PROGRESS` |
| Huỷ phiếu | `POST /{id}/cancel` `{ "reason": "…" }` (bắt buộc lý do) | chưa `DELIVERED`/`CANCELLED`, không có lần giao đang chạy |

- **Danh sách tài xế:** `GET /api/staff?role=DELIVERY_STAFF&status=ACTIVE` → gửi `id` (user id) của tài xế. Người không phải tài xế → `422`.
- **Đổi lô:** tổng `baseQuantity` phải **bằng đúng** phần chưa giao của dòng (`remainingBaseQuantity`), lô phải cùng sản phẩm và còn hạn bán
  (danh sách lô: `GET /api/inventory/lots?storeProductId=&hasStock=true`). Lô cũ hiện trạng thái `RELEASED` (giữ làm lịch sử) — hiển thị mờ, không xoá.
- **Huỷ phiếu đã giao một phần:** phần đã giao giữ nguyên, phiếu thành `DELIVERED`, dòng thành `PARTIALLY_CANCELLED`; phần chưa giao trở về đơn
  (lập phiếu mới hoặc "huỷ phần còn lại" của đơn ở luồng 1).

### Q5. Sự cố

- Danh sách: `GET /api/deliveries/{id}/incidents`. Báo sự cố (Bán hàng cũng báo được): xem D3.
- Xử lý: `POST /api/deliveries/{id}/incidents/{incidentId}/resolve`
  `{ "resolutionType": "RETRY_DELIVERY", "resolutionNote": "Hẹn giao lại sáng mai", "relatedStockMovementId": null }`.
- `resolutionType`: `RETRY_DELIVERY` Giao lại · `REPLACE_GOODS` Đổi hàng · `RETURN_TO_STORE` Mang về kho · `WRITE_OFF` Huỷ hàng ·
  `CANCEL_REMAINDER` Huỷ phần còn lại · `NO_ACTION` Không cần xử lý · `OTHER` Khác.
- Cách xử lý làm **thay đổi kho** (hư hỏng, mất): tạo phiếu điều chỉnh kho ở luồng 4 trước, rồi gửi id của nó vào `relatedStockMovementId`
  (§8). API này **không tự trừ kho**.

### Q6. Báo cáo giao hàng (Đại lý)

`GET /api/reports/deliveries?fromDate=2026-10-01&toDate=2026-10-31&groupBy=STAFF` (`DAY` mặc định; tối đa 366 ngày)

```json
{
  "fromDate": "2026-10-01", "toDate": "2026-10-31", "groupBy": "STAFF",
  "rows": [ { "key": "uuid", "label": "Trần Văn B", "deliveries": 20, "attempts": 24,
              "successful": 18, "partial": 3, "failed": 3, "successRate": 0.75 } ],
  "failureReasons": [ { "code": "CUSTOMER_ABSENT", "count": 2 } ],
  "incidents": [ { "incidentType": "DAMAGED", "open": 1, "resolved": 3 } ],
  "totals": { "deliveries": 20, "attempts": 24, "successful": 18, "partial": 3, "failed": 3, "successRate": 0.75 }
}
```

`successRate` là tỉ lệ 0–1 (hiện %). Lần giao được tính theo **ngày hoàn thành**; lần giao bị huỷ không tính. `groupBy=DAY`: `key` = `yyyy-MM-dd`.

---

## 5. Web quản lý: Giao hàng (tài xế)

| Mã | Màn hình | API chính | Trang hiện có |
|---|---|---|---|
| D1 | Phiếu của tôi | `GET /api/deliveries` | `delivery/deliveries/DeliveriesPage`, `delivery/dashboard/DashboardPage` |
| D2 | Giao hàng: bắt đầu → chụp ảnh → kết thúc | `/api/deliveries/{id}/attempts…`, `POST /api/files/delivery-proofs` | `delivery/deliveries/DeliveryDetailPage` |
| D3 | Báo sự cố | `POST /api/deliveries/{id}/incidents` | trong D2 |

### D1. Phiếu của tôi

`GET /api/deliveries?status=OUT_FOR_DELIVERY` — server **tự lọc theo tài xế đang đăng nhập**, không cần gửi gì thêm.
Gợi ý tab: *Cần giao* (`OUT_FOR_DELIVERY`), *Chờ giao lại* (`RETRY_PENDING`, `PARTIALLY_DELIVERED` — chờ cửa hàng xuất phát lại), *Đã giao* (`DELIVERED`).
Chi tiết: `GET /api/deliveries/{id}` (địa chỉ, toạ độ để mở bản đồ, dòng hàng, lô).

### D2. Giao hàng

```
 Phiếu OUT_FOR_DELIVERY
   │ 1. POST /{id}/attempts  {}                 → lần giao IN_PROGRESS, mang toàn bộ hàng chưa giao
   │ 2. (đến nơi) chụp ảnh → POST /api/files/delivery-proofs (multipart, field "file") → { url }
   │ 3. POST /{id}/attempts/{attemptId}/complete
   ▼
 DELIVERED · PARTIALLY_DELIVERED · RETRY_PENDING
```

**Bắt đầu lần giao**

```http
POST /api/deliveries/{id}/attempts
{}                                             // hoặc chỉ mang một phần:
{ "items": [ { "allocationId": "uuid", "attemptedBaseQuantity": 10 } ] }
```

→ `201 DeliveryAttemptResponse` (`items[]` có `allocationId`, `lotNumber`, `attemptedBaseQuantity`). Mỗi phiếu chỉ một lần giao đang chạy; bấm lại → `422`.

**Upload ảnh bằng chứng**

```http
POST /api/files/delivery-proofs        Content-Type: multipart/form-data, field "file"
```

→ `201 { "url": "https://…/delivery-proofs/2026/10/….jpg", "storageKey": "…", "sizeBytes": 123456 }`. JPEG/PNG/WebP, **tối đa 5 MB**
(nén ảnh điện thoại trước khi gửi). Ảnh đã gắn vào lần giao/sự cố thì **không xoá được** (`422`).

**Kết thúc lần giao**

```http
POST /api/deliveries/{id}/attempts/{attemptId}/complete
{
  "items": [ { "allocationId": "uuid", "deliveredBaseQuantity": 10 } ],   // dòng không gửi = giao 0
  "receiverName": "Nguyễn Văn A",
  "proofImageUrl": "https://…/delivery-proofs/2026/10/….jpg",
  "failureReasonCode": null,
  "note": null
}
```

| Trường hợp | Bắt buộc | Kết quả |
|---|---|---|
| Giao hết | `items` đủ số đã mang, `receiverName`, `proofImageUrl` | lần giao `SUCCESS`; phiếu `DELIVERED` nếu không còn gì |
| Giao một phần | như trên + nên có `failureReasonCode` cho phần còn lại | `PARTIAL_SUCCESS`; phiếu `PARTIALLY_DELIVERED` |
| Không giao được | `failureReasonCode` (không cần ảnh, gửi `items` rỗng hoặc bỏ) | `FAILED`; phiếu `RETRY_PENDING`; **không trừ kho** |

- Số đã giao **≤ số đã mang** và tổng mỗi dòng phải là **số quy cách nguyên** (ví dụ thùng 10 gói: 10, 20… gói) → sai thì `422`.
  UI nên cho tài xế nhập **số thùng/bao** rồi tự nhân `conversionToBase`, chia vào các lô theo thứ tự trong `items`.
- Ảnh phải là URL do `POST /api/files/delivery-proofs` trả về; URL khác → `422`.
- Sau `PARTIALLY_DELIVERED`/`RETRY_PENDING`, **cửa hàng phải bấm Xuất phát lại** (Q4) rồi tài xế mới bắt đầu lần giao tiếp theo.
- Phản hồi là `DeliveryResponse` đầy đủ → thay state bằng bản trả về.

### D3. Báo sự cố

```http
POST /api/deliveries/{id}/incidents
{
  "incidentType": "DAMAGED",                // cùng danh sách mã với failureReasonCode (N7)
  "description": "Rách 1 bao trên đường",   // bắt buộc
  "deliveryAttemptId": "uuid",              // tuỳ chọn
  "allocationId": "uuid",                   // tuỳ chọn: lô bị ảnh hưởng
  "affectedBaseQuantity": 10,               // tuỳ chọn, ≤ số đã phân bổ của lô đó
  "evidenceImageUrl": "https://…/delivery-proofs/…"
}
```

→ `201 DeliveryIncidentResponse` (`status: OPEN`). Khách **từ chối nhận hàng trước khi giao** là sự cố `CUSTOMER_REFUSED`, không phải trả hàng.

---

## 6. Kịch bản đầu–cuối có JSON mẫu

Chạy thật trên DB dev ngày 2026-10-05 (dữ liệu có tiền tố `TEST-L2-…`). Sản phẩm: quy cách *Gói* (cơ sở, 12 000 ₫) và *Thùng 10 gói* (115 000 ₫).

**S1. Nông dân đặt 2 thùng + 3 gói, giao tận nơi; lần 1 khách vắng, lần 2 giao đủ**

| # | Ai | Gọi | Kết quả |
|---|---|---|---|
| 1 | Nông dân | `POST /api/auth/register` → đăng nhập | token `FARMER` |
| 2 | Nông dân | `POST /api/me/addresses` (`addressType: FARM`) | `201`, `isDefault: true` (địa chỉ đầu tiên) |
| 3 | Nông dân | `POST /api/me/cart/items` thùng ×1, rồi thùng ×1 | dòng thùng `quantity: 2`, `subtotalAmount: 230000` |
| 4 | Nông dân | `POST /api/me/cart/items` gói ×3 | `subtotalAmount: 266000`, 2 dòng |
| 5 | Nông dân | `POST /api/me/orders` `{ source: FARMER_WEB, settlementType: FULL_PAYMENT, fulfillmentType: DELIVERY, addressId }` | `201`, `OD-20261005-0016`, `PENDING_CONFIRMATION`, `totalAmount: 266000`; giỏ rỗng |
| 6 | Đại lý | `POST /api/orders/{id}/confirm` | `200`, đơn `CONFIRMED`, hàng được giữ |
| 7 | Đại lý | `POST /api/deliveries` `{ orderId, items: [ {thùng, 2}, {gói, 3} ] }` | `201`, `DL-20261005-0001` `DRAFT`, lô chọn sẵn: 20 + 3 gói |
| 8 | Đại lý | `POST /api/deliveries` lần nữa cùng số lượng | `422` (vượt phần còn phải giao) |
| 9 | Đại lý | `POST /{id}/assign` `{ assignedToUserId }` → `POST /{id}/dispatch` | `OUT_FOR_DELIVERY` |
| 10 | Tài xế | `GET /api/deliveries` | `totalCount: 1` (chỉ phiếu của mình) |
| 11 | Tài xế | `POST /{id}/attempts {}` → `POST …/complete { failureReasonCode: "CUSTOMER_ABSENT" }` | phiếu `RETRY_PENDING`, kho không đổi |
| 12 | Tài xế | `POST /{id}/incidents { incidentType: CUSTOMER_ABSENT, … }` | `201`, `OPEN` |
| 13 | Đại lý | `POST /{id}/dispatch` | `OUT_FOR_DELIVERY` |
| 14 | Tài xế | `POST /{id}/attempts {}` → upload ảnh → `POST …/complete { items: đủ, receiverName, proofImageUrl }` | phiếu `DELIVERED`, đơn **`COMPLETED`** |
| 15 | Nông dân | `GET /api/me/orders/{id}/deliveries` | 1 phiếu, lần giao `FAILED`, `SUCCESS` |
| 16 | Đại lý | `GET /api/reports/deliveries?…&groupBy=STAFF` | `attempts: 2, successful: 1, failed: 1, successRate: 0.5`, lý do `CUSTOMER_ABSENT: 1` |

Bước 14 — kết thúc lần giao (`DeliveryResponse` rút gọn):

```json
{
  "deliveryNumber": "DL-20261005-0001", "orderNumber": "OD-20261005-0016", "status": "DELIVERED",
  "assignedTo": { "userId": "uuid", "fullName": "TEST-L2 Tài xế", "phoneNumber": null },
  "items": [
    { "sku": "TEST-L2-…", "packagingName": "Thùng 10 gói", "plannedQuantity": 2, "plannedBaseQuantity": 20,
      "deliveredBaseQuantity": 20, "remainingBaseQuantity": 0, "status": "DELIVERED",
      "allocations": [ { "lotNumber": "TEST-L2-…-LOT", "expiryDate": "2027-04-03", "allocatedBaseQuantity": 20,
                         "deliveredBaseQuantity": 20, "releasedBaseQuantity": 0, "status": "DELIVERED" } ] }
  ],
  "attempts": [
    { "attemptNumber": 1, "status": "FAILED", "failureReasonCode": "CUSTOMER_ABSENT", "saleStockMovementId": null },
    { "attemptNumber": 2, "status": "SUCCESS", "receiverName": "Nguyễn Văn A",
      "proofImageUrl": "https://…/delivery-proofs/2026/10/….png", "saleStockMovementId": "uuid" }
  ]
}
```

**S2. Thanh toán payOS cho đơn** (cần máy chủ đã cấu hình payOS — §9)

1. `POST /api/me/payments/payos { paymentContext: ORDER_PAYMENT, orderId }` → `201`, lưu `paymentId`, mở `checkoutUrl`.
2. Người dùng trả tiền; payOS gọi webhook của server → payment `PAID`, tiền ghi vào đơn.
3. Trình duyệt về `returnUrl` → FE `GET /api/me/payments/{paymentId}` → `status: PAID` → hiện "Đã thanh toán".
4. Máy chủ chưa cấu hình payOS: bước 1 trả `503` — đây là cách hệ thống đang chạy ở dev hiện nay.

---

## 7. Lỗi và cách hiển thị

Dạng lỗi chung (RFC 7807) và hàm `api()` giống luồng 1 (`FE_GUIDE_FLOW_1.md` §5). Riêng luồng 2:

| Mã | Khi nào | FE làm gì |
|---|---|---|
| `400` | thiếu trường, sai enum (`addressType`, `gender`, `incidentType`…), số lượng ≤ 0, thiếu ảnh/người nhận khi đã giao hàng | hiện lỗi theo `errors` của từng trường |
| `401` | hết hạn token | xoá token, về đăng nhập |
| `403` | sai vai trò (nhân viên gọi `/api/me/…`, tài xế bấm Xuất phát…) hoặc tài khoản không có hồ sơ nông dân | ẩn chức năng theo vai trò |
| `404` | id của người khác (địa chỉ, dòng giỏ, đơn, thanh toán, phiếu không phải của tài xế) hoặc đã xoá | tải lại danh sách |
| `409` | thao tác trùng thời điểm; link payOS cũ vừa được trả | tải lại rồi cho thử lại |
| `422` | quy tắc nghiệp vụ: giỏ rỗng/dòng không bán được, quá 10 địa chỉ, huỷ đơn đã xác nhận, vượt số lượng phiếu, đổi lô sai tổng, sai trạng thái phiếu, số tiền lẻ, URL ảnh lạ, Admin bắt đầu lần giao… | hiện `detail`; nếu có `errors` (`items[i]`, `proofImageUrl`…) gắn vào đúng dòng/trường |
| `503` | payOS hoặc kho ảnh tạm không dùng được | "Dịch vụ tạm gián đoạn, vui lòng thử lại sau" |

---

## 8. Chưa làm được hoặc phụ thuộc luồng khác

| Chức năng | Tình trạng |
|---|---|
| Thanh toán payOS thật | Code xong; **máy chủ cần khoá payOS và URL webhook công khai** mới chạy được (hiện trả `503`). Màn hình vẫn làm được ngay theo N5 |
| Mua chịu (`settlementType: CREDIT`) | Theo luồng 3: khách phải có hồ sơ tín dụng còn hạn mức; không đủ điều kiện → `422 Credit refused: …` |
| Trả nợ bằng payOS | Theo luồng 3 (công nợ); không có nợ → `422` |
| Điều chỉnh kho khi xử lý sự cố | Theo luồng 4 (`POST /api/inventory/adjustments`); trước đó dùng `resolutionType` không đổi kho (`RETRY_DELIVERY`, `NO_ACTION`…) |
| Đổi số điện thoại/email, quên mật khẩu, OTP | Chưa có API (chờ quyết định về Auth) |
| Thông báo đẩy khi đơn đổi trạng thái | Chưa có; FE tự tải lại khi mở màn hình |
| Tra cứu payment theo `orderCode` | Chưa có; **lưu `paymentId` trước khi chuyển sang payOS** |

---

## 9. Chạy thử ở máy local

- Chạy API như luồng 1 (`FE_GUIDE_FLOW_1.md` §9). Dữ liệu mẫu (sản phẩm, bảng giá, tồn kho): script `scripts/SeedDemoData.cs` của BE, hoặc nhờ BE tạo.
- Tài khoản: tự đăng ký nông dân (`POST /api/auth/register`); tài xế do Đại lý tạo (`POST /api/staff`, `role: DELIVERY_STAFF`).
- `returnUrl`/`cancelUrl` của payOS ở dev: `http://localhost:5173/payments/payos/return` và `/payments/payos/cancel` → web nông dân cần hai route này
  (đổi được trong `appsettings.Development.json` của BE, mục `PayOS`).
- Webhook payOS cần URL HTTPS công khai (ngrok/cloudflared trỏ vào API) và khoá payOS trong User Secrets của máy chạy BE.

---

## 10. Phụ lục: kiểu TypeScript

```ts
export type AddressType = 'HOME' | 'FARM' | 'OTHER'
export interface AddressRequest {
  recipientName: string; recipientPhone: string; addressLine: string; province: string
  ward?: string | null; district?: string | null; latitude?: number | null; longitude?: number | null
  addressType: AddressType; isDefault?: boolean
}
export interface AddressResponse extends Required<Omit<AddressRequest, 'isDefault'>> { id: string; isDefault: boolean; createdAt: string }

export interface FarmerProfile {
  farmerProfileId: string; userId: string; fullName: string; phoneNumber: string | null; email: string | null
  dateOfBirth: string | null; gender: 'MALE' | 'FEMALE' | 'OTHER' | null
  customerGroup: { id: string; code: string; name: string } | null; createdAt: string
}

export interface CartItem {
  id: string; storeProductId: string; productPackagingId: string; sku: string; productName: string; packagingName: string
  imageUrl: string | null; quantity: number; unitPrice: number | null; lineTotalAmount: number | null
  isAvailable: boolean; unavailableReason: 'NOT_SELLABLE' | 'NO_PRICE' | null
}
export interface Cart { id: string | null; items: CartItem[]; subtotalAmount: number; priceListId: string | null }

export interface CheckoutRequest {
  source: 'FARMER_WEB' | 'FARMER_MOBILE'; settlementType: 'FULL_PAYMENT' | 'CREDIT'; fulfillmentType: 'PICKUP' | 'DELIVERY'
  addressId?: string | null; deliveryAddress?: Omit<AddressRequest, 'addressType' | 'isDefault'> | null; note?: string | null
}

export interface PayOsPaymentRequest { paymentContext: 'ORDER_PAYMENT' | 'DEBT_REPAYMENT'; orderId?: string | null; amount?: number | null }
export interface PayOsPaymentResponse {
  paymentId: string; paymentNumber: string; amount: number; checkoutUrl: string; qrCode: string | null
  providerOrderCode: number; expiresAt: string | null; status: 'PENDING'
}

export type DeliveryStatus = 'DRAFT' | 'ASSIGNED' | 'OUT_FOR_DELIVERY' | 'PARTIALLY_DELIVERED' | 'RETRY_PENDING' | 'DELIVERED' | 'CANCELLED'
export type AttemptStatus = 'IN_PROGRESS' | 'SUCCESS' | 'PARTIAL_SUCCESS' | 'FAILED' | 'CANCELLED'
export type FailureReason = 'CUSTOMER_ABSENT' | 'UNREACHABLE' | 'CUSTOMER_REFUSED' | 'DAMAGED' | 'WEATHER' | 'VEHICLE_ISSUE' | 'ADDRESS_ISSUE' | 'OTHER'
export interface StaffRef { userId: string; fullName: string; phoneNumber: string | null }

export interface DeliveryAllocation {
  id: string; inventoryLotId: string; lotNumber: string | null; expiryDate: string | null
  allocatedBaseQuantity: number; deliveredBaseQuantity: number; releasedBaseQuantity: number
  status: 'ALLOCATED' | 'PARTIALLY_DELIVERED' | 'DELIVERED' | 'RELEASED' | 'CANCELLED'
}
export interface DeliveryItem {
  id: string; orderItemId: string; sku: string; productName: string; packagingName: string
  plannedQuantity: number; plannedBaseQuantity: number; deliveredBaseQuantity: number; cancelledBaseQuantity: number
  remainingBaseQuantity: number; status: 'PENDING' | 'PARTIALLY_DELIVERED' | 'DELIVERED' | 'CANCELLED' | 'PARTIALLY_CANCELLED'
  allocations: DeliveryAllocation[]
}
export interface DeliveryAttempt {
  id: string; attemptNumber: number; status: AttemptStatus; attemptedBy: StaffRef; startedAt: string; completedAt: string | null
  receiverName: string | null; proofImageUrl: string | null; failureReasonCode: FailureReason | null; note: string | null
  saleStockMovementId: string | null
  items: { allocationId: string; inventoryLotId: string; lotNumber: string | null
           attemptedBaseQuantity: number; deliveredBaseQuantity: number; failedBaseQuantity: number }[]
}
export interface Delivery {
  id: string; deliveryNumber: string; orderId: string; orderNumber: string; status: DeliveryStatus; assignedTo: StaffRef | null
  deliveryAddress: { recipientName: string; recipientPhone: string; addressLine: string; ward: string | null; district: string | null
                     province: string; latitude: number | null; longitude: number | null }
  scheduledAt: string | null; dispatchedAt: string | null; completedAt: string | null; note: string | null
  createdBy: string; createdAt: string; cancelledBy: string | null; cancelledAt: string | null; cancelReason: string | null
  items: DeliveryItem[]; attempts: DeliveryAttempt[]
}
export interface DeliveryListItem {
  id: string; deliveryNumber: string; orderId: string; orderNumber: string; status: DeliveryStatus; assignedTo: StaffRef | null
  recipientName: string; province: string; scheduledAt: string | null; dispatchedAt: string | null; completedAt: string | null
  itemCount: number; createdAt: string
}

export type IncidentType = FailureReason
export type ResolutionType = 'RETRY_DELIVERY' | 'REPLACE_GOODS' | 'RETURN_TO_STORE' | 'WRITE_OFF' | 'CANCEL_REMAINDER' | 'NO_ACTION' | 'OTHER'
export interface DeliveryIncident {
  id: string; deliveryId: string; deliveryAttemptId: string | null; allocationId: string | null; incidentType: IncidentType
  affectedBaseQuantity: number | null; description: string; status: 'OPEN' | 'RESOLVED'; resolutionType: ResolutionType | null
  resolutionNote: string | null; evidenceImageUrl: string | null; relatedStockMovementId: string | null
  reportedBy: string; reportedAt: string; resolvedBy: string | null; resolvedAt: string | null
}

export interface MyDelivery {
  id: string; deliveryNumber: string; status: DeliveryStatus; scheduledAt: string | null; dispatchedAt: string | null
  completedAt: string | null; assignedTo: { fullName: string; phoneNumber: string | null } | null
  items: { orderItemId: string; productName: string; packagingName: string; plannedQuantity: number
           deliveredQuantity: number; remainingQuantity: number }[]
  attempts: { attemptNumber: number; status: AttemptStatus; startedAt: string; completedAt: string | null
              receiverName: string | null; proofImageUrl: string | null; failureReasonCode: FailureReason | null }[]
}
```
