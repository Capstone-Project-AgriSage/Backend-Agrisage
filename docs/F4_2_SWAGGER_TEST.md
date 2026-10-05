# Test F4.2 trên Swagger

Mở http://localhost:5206/swagger/index.html. Nếu API chưa chạy, tại thư mục repository:

```powershell
dotnet run --project src/AgriSage.Api --launch-profile http
```

## 1. Đăng nhập

Gọi `POST /api/auth/login` với `identifier` và `password` của tài khoản demo được nhóm cấp.
Copy `accessToken` trong response, bấm **Authorize**, dán token **không kèm tiền tố Bearer**.
Không đưa mật khẩu hoặc token vào file test được commit.

- SALES_STAFF: tạo, bắt đầu, nhập số đếm, refresh, hủy, xóa phiếu DRAFT và xem phiếu.
- STORE_OWNER / ADMIN: các thao tác trên, duyệt phiếu và điều chỉnh thủ công.
- Người duyệt phải khác mọi người đã đếm các dòng của phiếu.

## 2. Kiểm kê

1. Lấy `storeProductId` từ `GET /api/inventory/lots`; chọn sản phẩm dùng để test.
2. `POST /api/stocktakes`, thay placeholder bằng UUID thật:

```json
{
  "storeProductIds": ["UUID_STORE_PRODUCT"],
  "includeEmptyLots": false,
  "note": "Test F4.2"
}
```

Kỳ vọng `201`, `status: DRAFT`. Lưu `id` của phiếu và `items[].id`.
`items[].id` là ID dòng kiểm kê, khác `inventoryLotId`.

3. `POST /api/stocktakes/{id}/start`: kỳ vọng `200`, `IN_PROGRESS`.
4. `PUT /api/stocktakes/{id}/counts`: gửi tất cả các dòng cần đếm, ví dụ:

```json
{
  "counts": [
    {
      "itemId": "UUID_DONG_KIEM_KE",
      "countedQuantity": 8,
      "reasonCode": "LOST",
      "note": "Dem thuc te thieu 2 don vi"
    }
  ]
}
```

`countedQuantity` là số lượng thực tế theo đơn vị cơ sở. Ví dụ trên chỉ lệch -2 nếu
`systemQuantitySnapshot` là 10; hãy nhập số phù hợp với dữ liệu của bạn.
Dòng không lệch có thể bỏ `reasonCode`. Dòng lệch bắt buộc có lý do.
Nếu chọn `includeEmptyLots: true` và đếm tăng một lô có snapshot bằng 0, thêm `unitCost`.

5. Xem `GET /api/stocktakes/{id}`. Có thể lọc `onlyDifferences=true` hoặc `onlyUncounted=true`;
`totals` vẫn tính toàn phiếu.
6. Đăng nhập tài khoản STORE_OWNER khác tài khoản đã đếm, thay token trong **Authorize**.
7. `POST /api/stocktakes/{id}/complete`: kỳ vọng `200`, `COMPLETED`; `movements` có
ADJUSTMENT_IN và/hoặc ADJUSTMENT_OUT theo chiều lệch. Phiếu không lệch không tạo movement.
8. Dùng `GET /api/inventory/stock-movements/{id}` và `GET /api/inventory/lots/{id}`
để đối chiếu ledger và số tồn sau điều chỉnh.

## 3. Điều chỉnh thủ công

Đăng nhập STORE_OWNER / ADMIN. `POST /api/inventory/adjustments`:

```json
{
  "reasonCode": "MANUAL_CORRECTION",
  "note": "Dieu chinh lo test",
  "lines": [
    { "inventoryLotId": "UUID_LO", "quantityDeltaBase": 2, "unitCost": 10000 }
  ]
}
```

Kỳ vọng `201`, movement `POSTED`, `ADJUSTMENT_IN`. Đổi delta thành số âm để giảm:
giá vốn giảm lấy bình quân hiện tại, không lấy `unitCost` từ request. Mọi delta trong một
request phải cùng dấu. Lô đang trống cần `unitCost` khi tăng.

## 4. Các trường hợp cần kiểm tra

| Tình huống | Kỳ vọng |
|---|---|
| Không có token | 401 |
| SALES_STAFF duyệt hoặc điều chỉnh thủ công | 403 |
| Người đã đếm tự duyệt | 422 |
| Delta trộn dấu, delta 0, số đếm âm hoặc lý do không hợp lệ | 400 |
| ID phiếu/dòng/lô không tồn tại | 404 |
| Duyệt khi còn dòng chưa đếm | 422, chỉ rõ lô |
| Tăng lô snapshot trống mà thiếu giá vốn | 422 khi duyệt |
| Giảm khiến tồn thấp hơn số đã giữ chỗ | 422, không thay đổi một phần |
| Xóa phiếu DRAFT | 204; GET sau đó 404 |

Để kiểm tra stale: tạo và start phiếu, post một điều chỉnh cho lô đó, rồi nhập số đếm.
GET sẽ có `isStale: true`; complete trả 422. Gọi `refresh-stale` để chỉ chụp lại và xóa số đếm
các dòng bị cũ, sau đó đếm lại. Theo hợp đồng, kiểm tra stale có biên an toàn **5 phút trước
snapshot**; nếu movement vừa post, đợi hơn 5 phút rồi refresh và đếm lại để không gặp lại stale.
Movement post sau thời điểm đếm không làm dòng stale; khi duyệt, hệ thống áp dụng độ lệch
vào tồn hiện tại.

Các thao tác Swagger ghi trực tiếp vào database được cấu hình. Dùng lô test và ghi rõ lý do;
kiểm thử tự động PostgreSQL của F4.2 dùng transaction rollback để không để lại dữ liệu test.
