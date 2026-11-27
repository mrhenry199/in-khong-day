# In không dây

Máy Windows x64 kết nối máy in USB nhận lệnh IPP của thiết bị trên LAN, quảng bá qua Bonjour/mDNS và dùng driver đã cài để in. Mã dựa trên [Easy Print Easy Scan](https://github.com/mnejjamohamed/easy-print-easy-scan), giấy phép MIT trong LICENSE.

## Chạy

Giải nén gói standalone Windows x64 rồi chạy PrintBridge.exe. Gói đi kèm .NET, Windows Desktop và ASP.NET Core. Bảng thiết lập mở lần đầu để kiểm tra driver, chọn máy in và cấu hình firewall. Nhấn Hoàn tất để lưu. Mở lại bảng từ menu khay hệ thống. Trên điện thoại cùng LAN, mở In trong ứng dụng và chọn tên máy in. Máy chủ cần bật và kết nối mạng.

Khi mở, ứng dụng đọc trạng thái spooler, danh sách hàng đợi, tệp driver, thiết bị PnP, profile mạng và quy tắc firewall của ứng dụng. Lỗi được hiện trong hộp thoại. Chọn **Kiểm tra máy in và mạng LAN** trong menu khay để xem lại, kể cả khi mọi kiểm tra đều đạt. Nếu máy chủ dùng profile Public trên LAN tin cậy, chọn Configure Firewall → Public, kiểm tra IPv4 máy chủ, bấm Quét máy trong LAN, chọn đúng IP của máy khách rồi bấm Chấp nhận. Để iPhone/iPad tự tìm thấy máy in, giữ chọn “Cho iPhone tìm máy in”. Tùy chọn này mở UDP 5353 chỉ trên card mạng máy chủ và các thiết bị trong LocalSubnet. iPhone phải ở cùng Wi-Fi/LAN, không phải Wi-Fi khách có client isolation. Có thể nhập IP trực tiếp khi thiết bị chưa xuất hiện. Danh sách dùng ping và bảng ARP; với mạng lớn chỉ dò chủ động tối đa một dải /24. Quy tắc chỉ cho phép TCP/IPP cho PrintBridge.exe trên card mạng và cặp địa chỉ đã chọn. Thêm máy in trên máy khách qua URL IPP trong báo cáo kiểm tra. Nếu đổi IP hoặc di chuyển file exe, chạy lại thiết lập firewall với thông tin mới. Dashboard quản trị mở trên localhost. Kiểm tra từ host không xác nhận được multicast qua router, quy tắc chặn của bên thứ ba hoặc khả năng ra giấy. Hãy thử một trang trên thiết bị khách.

## In từ thiết bị khác

Trên máy chủ, in thử tại chỗ rồi bật Share cho đúng máy in và bấm Save & Apply. Giữ ứng dụng chạy. Thiết bị khách có thể dùng Wi-Fi hoặc Ethernet cùng LAN.

| Thiết bị khách | Cách in |
| --- | --- |
| iPhone/iPad | Mở PDF hoặc ảnh, chọn Chia sẻ → In → Chọn máy in → In. |
| Android | Bật Dịch vụ in mặc định trong Cài đặt → In. Mở tài liệu, chọn In rồi chọn máy in trong danh sách. |
| Windows 11 | Cài đặt → Bluetooth & thiết bị → Máy in & máy quét → Thêm thiết bị. Khi đã thêm, dùng Ctrl+P trong ứng dụng. |
| macOS | Cài đặt hệ thống → Máy in & máy quét → Thêm máy in. Chọn máy in vừa xuất hiện rồi dùng ⌘P. |

Nếu Windows không tự phát hiện, mở PowerShell trên máy khách và chạy lệnh dưới với địa chỉ thực tế. Lấy IP và cổng tại Network Address trong Settings của máy chủ. Tên hàng đợi lấy từ cột Windows Printer, thay dấu cách bằng dấu gạch dưới. Ví dụ:

```powershell
Add-Printer -IppURL "ipp://192.168.1.20:631/printers/Canon_LBP2900"
```

Nếu không thấy máy in, kiểm tra host đang thức, cảnh báo trong khay hệ thống, firewall, và Wi-Fi khách có cô lập thiết bị không. Máy in xuất hiện nhưng gửi lệnh lỗi có thể liên quan đến định dạng mà hệ điều hành gửi.

## Build và kiểm thử

Dùng .NET SDK 8. Chạy các lệnh từ thư mục gốc.

```powershell
dotnet run --project tests/Routing
dotnet publish src/PrintBridge/PrintBridge.csproj -c Release -r win-x64 --self-contained true -o publish
```

## Phạm vi đã kiểm tra

Các test định tuyến dùng spooler giả. Chưa đo độ trễ A/B và chưa chạy ma trận iPhone, Android, macOS, Windows với máy in USB thực tế. Bridge có đường xử lý PDF, ảnh và URF. PWG Raster còn thiếu. Khả năng tương thích IPP Everywhere/Mopria chưa được xác minh. Lịch sử lệnh in hiện giữ tài liệu trong RAM để in lại. Dùng trên LAN tin cậy vì IPP hiện chưa xác thực và chưa bật TLS.

Trên Windows, thử lần lượt spooler dừng, hàng đợi đã xóa, quy tắc firewall thiếu, cổng IPP bận, máy in offline và máy hoạt động bình thường. Kiểm tra cả khi chạy tool bằng tài khoản thường. Đo thời điểm mở hộp thoại, nhận lệnh và ra tờ đầu.
