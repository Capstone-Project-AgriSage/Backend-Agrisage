# Test F4.5 — Refunds

Mở http://localhost:5206/swagger/index.html. Đăng nhập STORE_OWNER / ADMIN qua
`POST /api/auth/login`, copy `accessToken` vào **Authorize** không thêm tiền tố `Bearer`.
Mọi route ghi nhận hoàn tiền và xem hoàn tiền của đơn đã hủy đều yêu cầu Manage.

Tiền được nhân viên hoàn bên ngoài bằng tiền mặt hoặc chuyển khoản. API ghi nhận kết quả,
không gọi payOS để tự chuyển tiền. Dùng đơn test vì thao tác Swagger ghi vào database thật.

## 1. Hoàn tiền của phiếu trả hàng

Chuẩn bị một phiếu F4.4 đã complete-inspection, còn PARTIALLY_RESOLVED và có
`totalRefundAmount > 0`. Xem `GET /api/returns/{id}` để kiểm tra các refund đã có.

`POST /api/returns/{id}/refunds`:

```json
{
  "refundMethod": "BANK_TRANSFER",
  "amount": 100000,
  "originalPaymentId": "UUID_PAYMENT_CUA_DON",
  "externalReference": "RF-test-001",
  "note": "Hoan tien cho phieu tra hang test"
}
```

Thay số tiền bằng phần còn cần hoàn thực tế. `originalPaymentId` là optional;
nếu cung cấp thì phải là Payment đã thu tiền của đúng đơn. Lấy ID qua
`GET /api/orders/{id}/payments`. Method được chấp nhận: CASH, BANK_TRANSFER, OTHER_EXTERNAL.
Kỳ vọng `201`, PENDING; lưu `id` của refund.

Sau khi nhân viên đã hoàn tiền bên ngoài, gọi
`POST /api/returns/{id}/refunds/{refundId}/complete`:

```json
{
  "externalReference": "Ma-giao-dich-thuc-te",
  "proofFileUrl": "URL_ANH_DA_UPLOAD",
  "note": "Da hoan tien"
}
```

Ba trường đều optional; có thể gửi `{}`. Bỏ `proofFileUrl` nếu không có ảnh.
Kỳ vọng `200`, COMPLETED. Nếu refund liên kết Payment, Payment thành PARTIALLY_REFUNDED
hoặc REFUNDED dựa trên tổng tiền đã hoàn thành từ cả hai nguồn.
Khi đủ tiền cần hoàn và các dòng RESTOCK đã có movement, phiếu trả hàng tự thành COMPLETED.

## 2. Hoàn tiền của đơn đã hủy

F1.6 đã tạo PENDING refund khi hủy đơn có tiền chưa dùng. Với đơn CANCELLED hoặc
PARTIALLY_CANCELLED:

1. `GET /api/orders/{id}/refunds` để lấy refund có sẵn.
2. Sau khi trả tiền ngoài hệ thống, gọi
   `POST /api/orders/{id}/refunds/{refundId}/complete` với body như phần trên.
3. Xem trạng thái Payment qua `GET /api/payments/{id}`.

Nếu refund cũ FAILED/CANCELLED, tạo lần thử mới qua `POST /api/orders/{id}/refunds`:

```json
{
  "originalPaymentId": "UUID_PAYMENT",
  "refundMethod": "CASH",
  "amount": 100000,
  "note": "Thu lai sau khi refund cu that bai"
}
```

Số tiền không được vượt phần F1.6 đã giải phóng từ Payment đó. Phần đã dùng cho hàng giao
thành công không thuộc hạn mức hoàn do hủy đơn. Các lần thử FAILED/CANCELLED không cộng
thêm vào hạn mức gốc.

## 3. Fail, cancel và retry

Hai nguồn dùng cùng các action, chỉ thay prefix `returns` hoặc `orders`:

- `POST /api/{source}/{id}/refunds/{refundId}/fail`: `{ "note": "Chuyen khoan that bai" }`
- `POST /api/{source}/{id}/refunds/{refundId}/cancel`: `{ "reason": "Sai phuong thuc" }`

Chỉ PENDING đổi được sang COMPLETED/FAILED/CANCELLED. FAILED/CANCELLED giải phóng phần đã
giữ để tạo một refund mới. Không sửa hoặc complete lại refund đã kết thúc.

## 4. Ảnh chứng từ

1. Upload JPEG/PNG/WebP qua `POST /api/files/delivery-proofs` (multipart `file`, tối đa 5 MB).
2. Lấy `url` đưa vào `proofFileUrl` khi complete; giữ `storageKey` để kiểm tra delete.
3. `DELETE /api/files/delivery-proofs?key=STORAGE_KEY` cho ảnh đã được dùng phải trả 422.
   Guard cũng giữ ảnh đang được dùng bởi delivery attempt/incident và các tham chiếu lịch sử.

## 5. Các kiểm tra cần chạy

| Tình huống | Kỳ vọng |
|---|---|
| Không token | 401 |
| SALES_STAFF/FARMER/DELIVERY_STAFF gọi route Refund | 403 |
| Amount ≤ 0 hoặc có hơn 2 số lẻ, method không hợp lệ, cancel thiếu reason | 400 |
| Payment của đơn khác hoặc refund không thuộc parent trong URL | 404 |
| Payment chưa thu tiền | 422 |
| Tổng PENDING + COMPLETED vượt phần cần hoàn | 422 |
| Retry làm vượt số tiền đã giải phóng do hủy đơn | 422 |
| Complete/fail/cancel lần nữa trên bản terminal | 422 |
| Complete refund cuối cùng của phiếu trả hàng | Phiếu COMPLETED |
| Xóa ảnh đang dùng làm chứng từ | 422; storage không được gọi |

Farmer xem refund của đơn đã hủy qua `GET /api/me/orders/{id}/payments` trong trường
`refunds`; refund của phiếu trả hàng có trong `GET /api/me/returns/{id}`.

F3.5 vẫn dùng bản tạm giảm công nợ bằng 0 như khi triển khai F4.4. F4.5 chỉ hoàn phần
`totalRefundAmount` mà F4.4 đã xác định, không tự tính lại hoặc ghi sổ công nợ.
