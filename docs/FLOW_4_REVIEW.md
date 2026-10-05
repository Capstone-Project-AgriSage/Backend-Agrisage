# Kiểm duyệt Flow 4 trước khi tích hợp main

Ngày kiểm duyệt: 2026-10-05. Nhánh: `feature/f4-6-inventory-reports`.
Base đã cập nhật đến `origin/main` tại `ddea6b4` bằng fast-forward; chưa commit/push phần L4.

## Kết luận

F4.1, F4.2, F4.4, F4.5 và F4.6 đã có API, validator, xử lý nghiệp vụ và kiểm thử.
F4.3 do lead triển khai sẵn, đã kiểm tra lại cùng phần nhập kho.
Mục 9 là tiêu chí kiểm thử bắt buộc; mục 10 là quyết định cần tuân thủ, không phải tính năng bổ sung.

**Chưa xác nhận hoàn tất luồng tài chính tích hợp:** DI vẫn đăng ký `TemporaryDebtReturnPosting`
cho `IDebtReturnPosting` (F3.5), luôn trả về 0. Với đơn còn công nợ, hiện toàn bộ giá trị trả hàng
được tính vào tiền cần hoàn. Hai ví dụ giảm nợ 8 triệu / 15 triệu đã được kiểm thử bằng step giả,
chưa chứng minh ghi sổ DebtTransaction thực tế. Phải tích hợp F3.5 và chạy lại các ví dụ với ledger
thật trước khi chấp nhận toàn bộ luồng trả hàng cho đơn có công nợ.

Các kiểm tra dưới đây giảm rủi ro trong phạm vi đã chạy; không phải cam kết phần mềm không thể có lỗi.

## Các lỗi đã sửa trong lần kiểm duyệt này

1. Bộ lọc ngày của danh sách kiểm kho/trả hàng nhận ngày `0001-01-01`, làm phép đổi UTC+7
   có thể tràn miền DateTime. Validator giờ từ chối bằng 400; HTTP test bao phủ cả hai danh sách.
2. F1.6 tạo refund khi hủy phần còn lại chưa kiểm tra tổng refund đã giữ chỗ/hoàn tất từ F4.5.
   Với nhiều Payment, một Payment có thể đã được dùng cho refund trả hàng mà vẫn bị yêu cầu hoàn
   thêm do hủy đơn. Step hủy đơn giờ kiểm tra PENDING + COMPLETED trên mỗi Payment trước khi
   giải phóng phân bổ/tạo refund; trả 422 nếu vượt khả năng hoàn tiền. Test bao phủ cả refund trả
   hàng PENDING và COMPLETED, xác nhận không lưu hủy đơn hoặc thay đổi phân bổ khi bị từ chối.

## Đối chiếu mục 9

| Task | Bằng chứng đã kiểm tra |
|---|---|
| F4.1 | Tồn vật lý/tồn bán được, low-stock, cửa sổ ngày Việt Nam, expire-due đúng trạng thái và idempotent |
| F4.2 | Dấu và giá vốn điều chỉnh; không giảm dưới reserved; chưa đếm/stale thì không hoàn tất; chỉ refresh dòng stale và phải đếm lại; người đếm khác người duyệt |
| F4.3 | Template → preview → import; lỗi theo cột; không tạo phiếu một phần; dùng quy tắc nhập thủ công; giới hạn workbook/file/rows |
| F4.4 | In-flight chiếm hạn mức trả; đúng nguồn giao/nhận; làm tròn giá trị trả; nhập lại lô gốc/COGS gốc; WRITE_OFF không tăng tồn; quyền sở hữu Farmer; bước giảm nợ được kiểm bằng fake, còn chờ F3.5 thật |
| F4.5 | Hạn mức theo return/Payment; thử lại sau FAILED/CANCELLED; hoàn tất return sau refund đủ; trạng thái Payment; giữ ảnh chứng từ; rollback khi SaveChanges lỗi; tích hợp hủy đơn và hạn mức nhiều Payment |
| F4.6 | Đầu kỳ + biến động = cuối kỳ = tồn lô; ranh giới ngày Việt Nam; dấu giá vốn; tổng dòng/báo cáo; định giá từ balances, nhiều lô khác giá; loại dữ liệu cửa hàng khác/đã xóa |

## Đối chiếu mục 10

| Quyết định | Trạng thái |
|---|---|
| C-D2, F-D4 | Hủy đơn yêu cầu refund PENDING qua Order; L4 xử lý refund, không tự gọi hoàn tiền payOS |
| C-D3 | Guard kiểm tra URL ảnh trong refunds và delivery records, kể cả lịch sử đã soft-delete; chặn xóa ảnh được tham chiếu |
| C-D5 | Operate/Manage và Farmer đúng ở controller; kiểm kho không cho người đếm tự hoàn tất |
| C-D6 | Snapshot lấy dưới khóa balance; truy vấn stale đúng khoảng thời gian; refresh xóa count của dòng stale |
| D5 | Manual adjustments sẵn cho L2; việc liên kết incident thật thuộc F2.6 |
| F-D5 | Hai báo cáo tồn kho Manage-only, truy vấn đọc AsNoTracking, tổng hợp trong SQL |
| F-D6 | Expire-due chỉ chuyển ACTIVE quá hạn; kiểm tra bán/reserve vẫn dựa vào ngày hết hạn |
| F-D7 | ClosedXML nằm trong Infrastructure/Spreadsheets, giữ implementation F4.3 của lead |

## Kiểm tra kỹ thuật và bảo mật

- Controller chỉ nhận request, phân quyền và gọi Application; API trả DTO.
- Stock changes có movement; bước dùng chung không SaveChanges/commit độc lập.
- Mutation khóa aggregate/lot/payment theo thứ tự; refund dùng hạn mức chung giữa hai nguồn.
- Truy vấn đọc dùng AsNoTracking, có CancellationToken và scope cửa hàng đang hoạt động.
- Soft delete/semantic cancel được giữ; không thay schema, không thêm migration.
- Formatter chuẩn hóa whitespace riêng các file C# thay đổi; diff whitespace check qua.
- `appsettings.Local.json` và log/build outputs bị gitignore. Scan bằng các secret local thật không
  tìm thấy chúng trong file tracked/untracked có thể commit; không in giá trị secret khi kiểm tra.
- Secret đã xuất hiện trong ảnh chat trước đây: nhóm cần rotate DB password/JWT signing key/Storage
  secret liên quan và cập nhật cấu hình local/deploy. Kiểm tra gitignore không thay thế việc rotate.

## Xác minh

- Build: 0 warnings, 0 errors.
- Toàn bộ offline suite: 571 unit tests, 591 integration/HTTP tests qua; real DB/storage tests opt-in
  được bỏ qua ở lượt này.
- PostgreSQL tổng hợp: 68 tests nhập kho/import, inventory, stocktake, returns/refunds, hủy đơn qua.
- Sau bản sửa hạn mức hủy đơn: 24 tests refund/hủy đơn qua, bao gồm 1 test mới. Tổng cộng 69 test
  PostgreSQL liên quan khác nhau đã được chạy; 23 test trong nhóm cuối được chạy lại sau bản sửa.
- Mọi fixture PostgreSQL nằm trong transaction rollback. Không chạy script seed hoặc ghi dữ liệu demo.
- Không thực hiện stress test nhiều phiên DB độc lập; chưa kiểm thử luồng debt-first với F3.5 thật
  hoặc luồng delivery incident thật của F2.6.

## Bước tích hợp

Push nhánh feature và mở PR vào main để lead review các thay đổi dùng chung (Payments, Domain,
row locks, file guard). Không force-push hoặc bỏ qua kiểm tra của nhóm.
PR phải nêu rõ phụ thuộc F3.5 còn dùng placeholder; không mô tả debt-first đã chạy thực tế.
Để chấp nhận toàn bộ Flow 4, cần thay step F3.5 bằng implementation thật, kiểm tra các DebtTransaction
và liên kết từng dòng trả hàng, rồi chạy demo xuyên luồng trên môi trường dev.
