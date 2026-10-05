# Hướng dẫn FE tích hợp API — Luồng L4: Tồn kho, Kiểm kê, Trả hàng và Hoàn tiền

Viết cho: đội FE (React) của các app **Đại lý (Agent)** và **Admin**.
Nguồn sự thật: `docs/reference/api-flows/FLOW_4_INVENTORY_RETURNS.md`.
Tài liệu này tóm tắt các luồng màn hình chính, gọi API nào, dữ liệu gửi/nhận, và lưu ý xử lý lỗi dành cho phía Frontend.

---

## 1. Màn hình Tổng quan kho & Cảnh báo (F4.1)

### 1.1 Tổng quan kho
Hiển thị danh sách sản phẩm trong kho, lượng tồn, và giá trị tồn.
- **API**: `GET /api/inventory/stock-summary`
- **Query**: `search` (SKU/tên), `categoryId`, `lowStockOnly` (true/false), `hasStock` (true/false), `pageIndex`, `pageSize`.
- **Response**: `PagedResult<StockSummaryItem>` (tồn khả dụng = `sellableAvailableBaseQuantity`, chỉ tính lô chưa hết hạn).

### 1.2 Cảnh báo (Sắp hết hạn, Hết hạn, Hết hàng)
- **API**: `GET /api/inventory/alerts`
- **Query**: `type` (`EXPIRING`, `EXPIRED`, `LOW_STOCK`), `withinDays` (mặc định 30).
- **Lưu ý**: Nút "Đánh dấu lô hết hạn" gọi `POST /api/inventory/lots/expire-due` (chuyển trạng thái các lô quá hạn thành EXPIRED). Chỉ gọi khi có quyền `Manage`.

---

## 2. Nhập hàng từ Excel (F4.3)

Quy trình: Tải mẫu -> Điền -> Preview -> Import.
- **Tải mẫu**: `GET /api/goods-receipts/import-template` (trả file `.xlsx`).
- **Preview**: `POST /api/goods-receipts/import/preview` (form-data: `file` + các trường header như `supplierId`). Màn hình hiển thị danh sách dòng hợp lệ và các lỗi từng dòng (`errors`).
- **Import (Lưu nháp)**: `POST /api/goods-receipts/import` (giống Preview). Trả về ID của phiếu nhập nháp. Sau đó FE chuyển sang màn hình Chi tiết phiếu nhập để staff xác nhận.

---

## 3. Kiểm kê kho (F4.2)

Chu trình: Tạo nháp -> Bắt đầu (Đang kiểm kê) -> Cập nhật số đếm (nhiều lần) -> Hoàn thành.

- **Tạo phiếu**: `POST /api/stocktakes` (`CreateStocktakeRequest`: danh sách `storeProductIds`, `includeEmptyLots`, `note`). -> Trạng thái DRAFT.
- **Bắt đầu**: `POST /api/stocktakes/{id}/start` -> Trạng thái IN_PROGRESS.
- **Nhập số liệu đếm**: `PUT /api/stocktakes/{id}/counts` (Gửi mảng `counts`: `itemId`, `countedQuantity`, `unitCost`, `reasonCode`). 
- **Làm mới dòng bị cũ (Stale)**: Nếu trong lúc đếm có giao dịch bán hàng, dòng lô đó bị gán `isStale: true`. FE hiển thị cảnh báo yêu cầu làm mới. Gọi `POST /api/stocktakes/{id}/refresh-stale` để lấy tồn mới nhất và đếm lại dòng đó.
- **Hoàn thành**: `POST /api/stocktakes/{id}/complete` (Quyền Manage). Nếu còn dòng stale hoặc chưa đếm, API sẽ báo 422.

Ngoài ra, **Điều chỉnh kho thủ công** (không qua kiểm kê) gọi: `POST /api/inventory/adjustments`.

---

## 4. Trả hàng (Sales Returns) (F4.4)

Chu trình: Yêu cầu trả -> Duyệt (Approve) / Từ chối (Reject) -> Nhận hàng (Receive) -> Kiểm tra tình trạng (Inspect) -> Hoàn tất kiểm tra.

- **Lấy danh sách hàng có thể trả của 1 đơn**: `GET /api/orders/{id}/returnable`
- **Tạo yêu cầu trả**: `POST /api/returns` (`CreateReturnRequest`: mảng `items` gồm `orderItemId`, `returnedBaseQuantity`, lý do).
- **Các bước phê duyệt (Quyền Manage)**: `POST /api/returns/{id}/approve` hoặc `reject`.
- **Nhận hàng về kho**: `POST /api/returns/{id}/receive`
- **Kiểm tra (Từng dòng)**: `PUT /api/returns/{id}/items/{itemId}/inspection` (Truyền `conditionStatus`: `RESELLABLE | DAMAGED | EXPIRED | UNUSABLE`).
- **Chốt kiểm tra**: `POST /api/returns/{id}/complete-inspection` -> API tự động tạo phiếu nhập lại hàng (nếu RESELLABLE) và tính toán `total_refund_amount` (số tiền cần hoàn).

---

## 5. Hoàn tiền (Refunds) (F4.5)

FE cần màn hình hiển thị danh sách các khoản cần hoàn (từ đơn bị huỷ hoặc đơn trả hàng). Staff chuyển khoản ngoài thực tế rồi lên hệ thống xác nhận.

**Hoàn tiền cho Trả hàng:**
- Yêu cầu hoàn: `POST /api/returns/{id}/refunds` (Nhập số tiền, phương thức `CASH | BANK_TRANSFER`).
- Hoàn tất (Đã chuyển): `POST /api/returns/{id}/refunds/{refundId}/complete` (Truyền `externalReference`, `proofFileUrl`).

**Hoàn tiền cho Đơn huỷ:**
- Lấy danh sách: `GET /api/orders/{id}/refunds`
- Hoàn tất: `POST /api/orders/{id}/refunds/{refundId}/complete`

Lưu ý: Up ảnh uỷ nhiệm chi gọi `POST /api/files/delivery-proofs` (lấy URL truyền vào `proofFileUrl`).

---

## 6. Thẻ kho & Báo cáo (F4.6)

- **Thẻ kho (Lịch sử giao dịch 1 lô/sản phẩm)**: `GET /api/inventory/stock-card` (Truyền `storeProductId`, `fromDate`, `toDate`).
- **Xuất Nhập Tồn**: `GET /api/reports/inventory-movement` (Tính tổng Nhập, Trả, Bán, Điều chỉnh, Tồn đầu, Tồn cuối).
- **Định giá kho**: `GET /api/reports/inventory-valuation` (Lượng tồn x Giá vốn trung bình).

---

## 7. Các mã lỗi thường gặp (HTTP 422)
- **Kiểm kê**: Đếm thiếu dòng, dòng bị Stale (có giao dịch khác xen vào), tồn kho bị âm.
- **Trả hàng**: Vượt quá số lượng khả dụng, chưa nhập tình trạng (Inspect) đã bấm Hoàn tất.
- **Hoàn tiền**: Số tiền hoàn vượt quá tổng số dư cần hoàn của đơn/phiếu trả hàng.
