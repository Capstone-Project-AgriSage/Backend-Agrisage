# Test F4.4 — Sales returns

Mở http://localhost:5206/swagger/index.html. Đăng nhập qua `POST /api/auth/login`,
copy `accessToken` vào **Authorize**, không thêm tiền tố `Bearer`.

## Chuẩn bị

Chọn một đơn đã có hàng được bàn giao: PICKUP qua luồng bán tại quầy hoặc DELIVERY đã giao thành công.
Đơn chưa bàn giao không có số lượng được trả. Dùng đơn và lô dành cho test vì Swagger ghi vào database thật.

- SALES_STAFF / STORE_OWNER / ADMIN: yêu cầu trả, nhận hàng và kiểm tra hàng.
- STORE_OWNER / ADMIN: approve, reject và complete-inspection.
- FARMER: chỉ sử dụng `/api/me/...` cho đơn và phiếu của chính mình.

## Luồng nhân viên

1. `GET /api/orders/{id}/returnable` với ID đơn. Trong `items[].sources`, lấy nguồn còn
   `returnableBaseQuantity > 0` và ID của dòng đơn.
2. `POST /api/returns` cho PICKUP:

```json
{
  "orderId": "UUID_DON",
  "reasonSummary": "Khach tra hang sau khi nhan",
  "note": "Test F4.4",
  "items": [
    {
      "orderItemId": "UUID_DONG_DON",
      "originalStockMovementItemId": "UUID_NGUON_SALE",
      "returnedBaseQuantity": 3,
      "reasonCode": "QUALITY_ISSUE"
    }
  ]
}
```

Với DELIVERY, thay `originalStockMovementItemId` bằng `deliveryItemLotAllocationId` lấy
từ returnable. Mỗi dòng chỉ được có đúng một nguồn. Số lượng tính theo đơn vị cơ sở.
Kỳ vọng `201`, `REQUESTED`; lưu `id` phiếu và `items[].id`.

3. Khi còn REQUESTED, có thể thêm dòng qua `POST /api/returns/{id}/items` hoặc xóa dòng
   qua `DELETE /api/returns/{id}/items/{itemId}`. Xóa dòng trả về `200` cùng phiếu cập nhật.
4. Dùng token STORE_OWNER / ADMIN: `POST /api/returns/{id}/approve` → APPROVED.
5. `POST /api/returns/{id}/receive` → RECEIVED.
6. Với từng dòng, gọi `PUT /api/returns/{id}/items/{itemId}/inspection`:

```json
{
  "conditionStatus": "RESELLABLE",
  "inspectionNote": "Hang con nguyen ven"
}
```

RESELLABLE được RESTOCK; DAMAGED, EXPIRED và UNUSABLE được WRITE_OFF. WRITE_OFF không
trừ tồn thêm vì hàng đã xuất khi bán.

7. Dùng token STORE_OWNER / ADMIN: `POST /api/returns/{id}/complete-inspection`.
   Mọi dòng phải được kiểm tra trước. RESTOCK tạo một RETURN_IN về đúng lô ban đầu với giá
   vốn SALE ban đầu, không dùng giá vốn hiện tại.
8. Xem `totalReturnAmount`, `totalDebtAdjustment`, `totalRefundAmount` và các ID movement
   trong response. Dùng `GET /api/inventory/stock-movements/{id}` để đối chiếu.

Nếu còn tiền hoàn, trạng thái PARTIALLY_RESOLVED và sẽ tiếp tục bằng F4.5. Nếu không có tiền
cần hoàn, trạng thái COMPLETED. F4.4 không ghi nhận đã trả tiền ngoài hệ thống.

## Công nợ và giới hạn tích hợp

F4.4 gọi `IDebtReturnPosting` của F3.5 trong cùng transaction. Repo hiện đăng ký
`TemporaryDebtReturnPosting`, trả về 0, nên toàn bộ giá trị trả hàng đang chuyển sang tiền
cần hoàn. Các test 8 triệu → giảm nợ 8 triệu và 15 triệu → giảm nợ 10 triệu + hoàn 5 triệu
dùng bản giả lập của interface; chưa chứng minh việc ghi sổ công nợ thật của F3.5.

## Farmer

Dùng token FARMER và thay các route đọc/request bằng:

- `GET /api/me/orders/{id}/returnable`
- `POST /api/me/returns`
- `GET /api/me/returns` và `GET /api/me/returns/{id}`
- `POST /api/me/returns/{id}/cancel` với `{ "reason": "Khong con nhu cau" }`

Farmer chỉ hủy được REQUESTED. Truy cập đơn hoặc phiếu của người khác trả 404.

## Các kiểm tra lỗi

| Tình huống | Kỳ vọng |
|---|---|
| Không token | 401 |
| SALES_STAFF approve/reject/complete-inspection | 403 |
| Hai nguồn cùng lúc, không nguồn, số lượng ≤ 0, lý do không hợp lệ | 400 |
| Số lượng vượt phần đã giao còn được trả | 422 |
| Yêu cầu mới khi hàng đã nằm trong phiếu đang xử lý khác | 422 nếu vượt phần còn lại |
| Nguồn của đơn khác hoặc sai loại fulfillment | 422 |
| Thêm/xóa dòng sau REQUESTED | 422 |
| Complete-inspection khi còn dòng chưa kiểm tra | 422, chỉ rõ dòng |
| Gọi complete-inspection lần hai | 422, không tạo movement lần nữa |

Phiếu REJECTED/CANCELLED giải phóng số lượng để yêu cầu trả mới; phiếu đang xử lý và
COMPLETED vẫn được tính vào số đã trả. Reject/cancel cần body `{ "reason": "Ly do" }`.
Nhân viên có thể hủy REQUESTED hoặc APPROVED; reject chỉ áp dụng REQUESTED.
