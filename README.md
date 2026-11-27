# In không dây

Máy Windows x64 kết nối máy in USB nhận lệnh IPP của thiết bị trên LAN, quảng bá qua Bonjour/mDNS và dùng driver đã cài để in. Mã dựa trên [Easy Print Easy Scan](https://github.com/mnejjamohamed/easy-print-easy-scan), giấy phép MIT trong LICENSE.

## Chạy

Giải nén toàn bộ thư mục ứng dụng trên Windows x64. Với bản framework-dependent, cài .NET 8 Desktop Runtime và ASP.NET Core Runtime x64. Chạy PrintBridge.exe, mở Settings từ khay hệ thống, chọn máy in. Trên điện thoại cùng LAN, mở In trong ứng dụng và chọn tên máy in. Máy chủ cần bật và kết nối mạng.

Khi mở, ứng dụng đọc trạng thái spooler, danh sách hàng đợi, tệp driver, thiết bị PnP, profile mạng và quy tắc firewall của ứng dụng. Lỗi được hiện trong hộp thoại. Chọn **Kiểm tra máy in và mạng LAN** trong menu khay để xem lại, kể cả khi mọi kiểm tra đều đạt. Chỉ dùng Configure Firewall trên LAN tin cậy. Dashboard quản trị mở trên localhost. Kiểm tra từ host không xác nhận được multicast qua router, quy tắc chặn của bên thứ ba hoặc khả năng ra giấy. Hãy thử một trang trên thiết bị khách.

## Build và kiểm thử

Dùng .NET SDK 8. Chạy các lệnh từ thư mục gốc.

```powershell
dotnet run --project tests/Routing
dotnet publish src/PrintBridge/PrintBridge.csproj -c Release -r win-x64 --self-contained true -o publish
```

## Phạm vi đã kiểm tra

Các test định tuyến dùng spooler giả. Chưa đo độ trễ A/B và chưa chạy ma trận iPhone, Android, macOS, Windows với máy in USB thực tế. Bridge có đường xử lý PDF, ảnh và URF. PWG Raster còn thiếu. Khả năng tương thích IPP Everywhere/Mopria chưa được xác minh. Lịch sử lệnh in hiện giữ tài liệu trong RAM để in lại. Dùng trên LAN tin cậy vì IPP hiện chưa xác thực và chưa bật TLS.

Trên Windows, thử lần lượt spooler dừng, hàng đợi đã xóa, quy tắc firewall thiếu, cổng IPP bận, máy in offline và máy hoạt động bình thường. Kiểm tra cả khi chạy tool bằng tài khoản thường. Đo thời điểm mở hộp thoại, nhận lệnh và ra tờ đầu.
