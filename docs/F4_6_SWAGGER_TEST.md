# F4.6 — Test thẻ kho và báo cáo tồn kho

Mở http://localhost:5206/swagger/index.html, đăng nhập qua API Auth và Authorize bằng access token.
STORE_OWNER/ADMIN dùng được cả 3 API; SALES_STAFF chỉ xem thẻ kho. Các API này chỉ đọc dữ liệu.

## 1. Lấy ID sản phẩm

Gọi `GET /api/inventory/stock-summary` và lấy `storeProductId` của sản phẩm đã có nhập/xuất kho.
Nếu cần lọc một lô, lấy `inventoryLotId` từ API danh sách lô của sản phẩm.
ID sản phẩm danh mục (`productId`) khác với `storeProductId`.

## 2. Thẻ kho — StockCard

Chọn `GET /api/inventory/stock-card` → Try it out:

- `storeProductId`: ID vừa lấy, bắt buộc.
- `inventoryLotId`: để trống để xem tất cả lô; điền để xem riêng một lô của đúng sản phẩm.
- `fromDate`, `toDate`: ngày dạng `2026-10-05`, bắt buộc; cả hai ngày đều được tính, tối đa 366 ngày.

Execute → `200`. Kiểm tra:

```text
openingBaseQuantity + Σ(inBaseQuantity - outBaseQuantity) = closingBaseQuantity
```

Mỗi dòng có số dư sau biến động, giá vốn, loại biến động và chứng từ nguồn nếu có.
`reference: null` là điều chỉnh không có chứng từ nguồn. `lotNumber` có thể null nếu lô không có mã.
Chỉ lấy POSTED; ngày dựa trên thời điểm ghi sổ `postedAt` theo giờ Việt Nam, không phải `occurredAt`.

Với `toDate` là hôm nay và dữ liệu không tiếp tục thay đổi trong lúc đối chiếu,
`closingBaseQuantity` phải bằng `onHandBaseQuantity` trong stock-summary (hoặc tồn của lô đã lọc).

## 3. Xuất–nhập–tồn — InventoryReports

Chọn `GET /api/reports/inventory-movement`, nhập cùng `fromDate` và `toDate` như thẻ kho.
`categoryId` optional; để trống để lấy toàn bộ sản phẩm của cửa hàng đang hoạt động.

Execute → `200`; tìm dòng theo `storeProductId`. Đối chiếu:

```text
openingQuantity = openingBaseQuantity trên thẻ kho toàn sản phẩm
closingQuantity = closingBaseQuantity trên thẻ kho toàn sản phẩm
openingQuantity + stockIn.quantity + returnIn.quantity + adjustmentIn.quantity
  + sale.quantity + adjustmentOut.quantity + reversal.quantity = closingQuantity
```

Mỗi nhóm biến động có `{ quantity, value }`: số lượng theo đơn vị cơ sở và giá vốn, cùng dấu.
SALE/ADJUSTMENT_OUT thường âm; REVERSAL có thể âm hoặc dương.
`openingValue`/`closingValue` là giá vốn tồn đầu/cuối kỳ, không phải doanh thu bán hàng.
Giá trị được cộng từ snapshot rồi làm tròn 2 chữ số; cộng các nhóm đã làm tròn có thể lệch một xu so với
giá trị cuối kỳ đã làm tròn từ tổng gốc. `totals` cộng các giá trị đã hiển thị của tất cả dòng sản phẩm.

## 4. Định giá tồn kho hiện tại

Chọn `GET /api/reports/inventory-valuation`; `categoryId` optional. Execute → `200`.

- `onHandBaseQuantity`: tồn vật lý, bao gồm cả phần đã giữ chỗ.
- `stockValue`: tổng giá vốn các lô; bao gồm hàng bị khóa/cách ly/hết hạn còn trong kho.
- `averageUnitCost`: giá vốn bình quân có trọng số; null khi tồn bằng 0.
- `expiredValue`: giá vốn của lô có ngày hết hạn trước hôm nay theo giờ Việt Nam; lô hết hạn hôm nay chưa thuộc mục này.
- `byCategory`: tổng theo danh mục; `totals`: tổng toàn bộ dòng hiển thị.

So sánh `stockValue` từng sản phẩm với stock-summary tại cùng thời điểm, làm tròn 2 chữ số.
Không dùng định giá hiện tại để so với cuối kỳ của một ngày quá khứ.

## 5. Kiểm tra lỗi và quyền

- Bỏ ngày hoặc ngày kết thúc trước ngày bắt đầu, khoảng trên 366 ngày, ID sai định dạng → `400`.
- `storeProductId` không tồn tại/khác cửa hàng, lô không thuộc sản phẩm → `404`.
- Không token → `401`; SALES_STAFF gọi hai báo cáo → `403`.
- Danh mục chưa có dữ liệu → `200`, rows rỗng và tổng bằng 0.

Không cần tạo thêm dữ liệu để thử nếu đã có phiếu nhập/điều chỉnh/trả hàng từ F4.1–F4.5.
Kiểm thử tự động PostgreSQL dùng fixture trong transaction và luôn rollback.
