# Dữ liệu mẫu bài viết và yêu cầu liên hệ

File `content-contact-demo.json` chứa dữ liệu giả lập bằng tiếng Việt, dùng thử các màn hình AgriSage.
Ảnh đính kèm trong `content-images/` được lấy từ các asset hiện có trong Farmer-Web.

## 6 bài viết

| Tiêu đề | Trạng thái | Ảnh |
|---|---|---|
| Chuẩn bị thông tin ruộng lúa trước vụ mới | PUBLISHED | rice-field.jpg |
| Cách chuẩn bị ảnh lá lúa để gửi Bác sĩ cây trồng AI | PUBLISHED | farmer-phone.jpg |
| Mẫu nhật ký quan sát ruộng lúa hằng tuần | PUBLISHED | rice-field.jpg |
| Kiểm tra thông tin vật tư trước khi nhận hàng | PUBLISHED | organic-fertilizer.jpg |
| Bản nháp: Chuẩn bị câu hỏi về nguồn nước đầu vụ | DRAFT | rice-field.jpg |
| Thông báo lưu trữ: Giới thiệu kênh hỗ trợ AgriSage | ARCHIVED | farmer-phone.jpg |

4 bài PUBLISHED xuất hiện ở trang chủ, Kiến thức và chi tiết bài viết của Farmer.
Màn quản lý hiển thị cả 6 bài. Mỗi bài có nội dung thực tế, tóm tắt và ảnh đại diện.

## 8 yêu cầu liên hệ

| Mã mẫu | Người gửi giả lập | Chủ đề | Trạng thái |
|---|---|---|---|
| CR01 | Nguyễn Văn An | Kỹ thuật canh tác | OPEN |
| CR02 | Trần Thị Bình | Đặt vật tư | IN_PROGRESS |
| CR03 | Lê Văn Cường | Sổ nợ | RESOLVED |
| CR04 | Phạm Thị Dung | Đặt vật tư | RESOLVED |
| CR05 | Võ Văn Đông | Khác | CLOSED |
| CR06 | Đặng Thị Giang | Kỹ thuật canh tác | IN_PROGRESS |
| CR07 | Bùi Văn Hải (khách) | Khác | OPEN |
| CR08 | Ngô Thị Hoa (khách) | Đặt vật tư | RESOLVED |

Tên người, số điện thoại và email đều là dữ liệu minh họa. Các yêu cầu đã giải quyết có phản hồi.
Bộ dữ liệu chỉ gồm bài viết và yêu cầu liên hệ; không tạo tài khoản, đơn hàng, thanh toán hay thay đổi công nợ.

## Dữ liệu đã nạp

Đã nạp 6 bài viết, 3 ảnh và 8 yêu cầu liên hệ vào backend qua API ngày 12/11/2026.
Các yêu cầu là khách gửi, xuất hiện ở màn quản lý và không thuộc lịch sử của tài khoản Farmer.
Bài viết đã xuất bản xuất hiện trên trang Kiến thức; bài nháp và lưu trữ chỉ xuất hiện trong quản lý.

Hai script tạm dùng để nạp và kiểm tra dữ liệu đã được dọn sau khi hoàn tất.
JSON và ảnh được giữ lại để tham khảo hoặc sử dụng từng mẫu qua API.

## Dùng từng mẫu trong Swagger/Postman

- Bài viết: upload file trong `imageFile` trước; lấy `url` gán vào `request.thumbnailUrl`; gửi riêng `request`
  tới POST `/api/admin/articles`. Sau đó POST `/api/admin/articles/{id}/status` với `{ "status": "PUBLISHED" }`
  hoặc trạng thái tương ứng trong `targetStatus`.
- Liên hệ: gửi riêng `request` tới POST `/api/contact-requests`; sau đó dùng tài khoản nhân viên PUT
  `/api/contact-requests/{id}` với `status` lấy từ `targetStatus` và `resolutionNote` trong mẫu.
- `imageFile`, `key`, `submitAs` và `targetStatus` là metadata mô tả mẫu, không phải trường của body tạo bản ghi.

