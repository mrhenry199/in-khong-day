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

### Điện thoại thấy máy in nhưng báo "Ngoại tuyến"

Điện thoại tìm máy in qua Bonjour (UDP 5353) nhưng in qua IPP (TCP 631). Thấy mà không in được nghĩa là TCP 631 không tới được PrintBridge. Mở **Kiểm tra máy in và mạng LAN** trong khay và xử lý theo các dòng `[!]`:

1. **Firewall đang CHẶN PrintBridge**: xảy ra khi bấm *Cancel* ở hộp thoại Windows Security Alert lần đầu chạy. Vào Configure Firewall → *Cho phép in* (Public) hoặc *Chia sẻ cả LAN* (Private); PrintBridge xóa quy tắc chặn của chính nó.
2. **Yêu cầu in từ IP … bị chặn**: danh sách thiết bị được phép đang bật mà IP điện thoại chưa có. Chọn IP đánh dấu *Vừa gửi lệnh in và bị chặn* → *Cho phép in*.
3. **IP thay đổi**: đặt DHCP reservation cho máy chủ và điện thoại trên router. Trên iPhone, đặt *Cài đặt → Wi-Fi → ⓘ → Địa chỉ Wi-Fi riêng tư* = *Cố định* (hoặc Tắt) cho mạng này; chế độ *Xoay vòng* làm IP đổi và mất quyền in. Khi IP máy chủ đổi, PrintBridge tự quảng bá lại địa chỉ mới sau vài giây.
4. Máy in Windows tạm dừng, hết giấy, kẹt giấy hoặc mất kết nối USB giờ được báo đúng trạng thái trên điện thoại.

### Bản in giống khi in tại máy chủ

PDF và AirPrint raster được đặt đúng kích thước thật lên khổ giấy (như *Actual size* / *Shrink oversized pages*), không thêm lề 1 inch. Khổ giấy lấy theo lựa chọn của máy khách, nếu không có thì theo kích thước trang tài liệu; trang ngang tự xoay giấy. PDF được dựng ở 300 DPI (600 DPI khi chọn chất lượng cao). Ảnh JPEG từ điện thoại được xoay theo EXIF và vừa vùng in. Nếu driver không có khổ giấy tương ứng, dùng khổ mặc định của hàng đợi và thu nhỏ cho vừa.

## Gỡ cài đặt

Chọn **Gỡ PrintBridge khỏi máy này...** trong menu khay, hoặc chạy `PrintBridge.exe --uninstall`. Thao tác xóa:

- quy tắc Windows Firewall do PrintBridge tạo, cùng quy tắc cho/chặn Windows tự tạo cho PrintBridge.exe (cần UAC);
- danh sách thiết bị được phép tại `HKLM\SOFTWARE\PrintBridge`;
- mục tự khởi động `HKCU\...\Run\PrintBridge`;
- cấu hình `%ProgramData%\PrintBridge` và thư viện giải nén `%TEMP%\.net\PrintBridge`.

Máy in, driver, hàng đợi và cài đặt spooler của Windows không bị thay đổi. Sau đó xóa PrintBridge.exe. Từ mã nguồn có thể dùng `scripts\uninstall.bat`. Lịch sử lệnh in chỉ nằm trong RAM (tối đa 200 lệnh, giữ tài liệu của 20 lệnh gần nhất để in lại) và mất khi thoát.

## Build và kiểm thử

Dùng .NET SDK 8. Chạy các lệnh từ thư mục gốc.

```powershell
dotnet run --project tests/Routing
powershell -NoProfile -ExecutionPolicy Bypass -File tests/PublicFirewall.Tests.ps1
dotnet publish src/PrintBridge/PrintBridge.csproj -c Release -r win-x64 --self-contained true -o publish
```

Nếu máy chỉ có runtime .NET mới hơn 8, đặt `DOTNET_ROLL_FORWARD=Major` khi chạy test. Một số antivirus cách ly `PrintBridge.dll` ngay sau khi biên dịch (nhận nhầm do ứng dụng sửa firewall và mở cổng mạng); thêm thư mục mã nguồn vào danh sách loại trừ nếu build báo thiếu `obj\...\PrintBridge.dll`.

## Phạm vi đã kiểm tra

Các test định tuyến dùng spooler giả. Chưa đo độ trễ A/B và chưa chạy ma trận iPhone, Android, macOS, Windows với máy in USB thực tế. Bridge có đường xử lý PDF, ảnh và URF. PWG Raster còn thiếu. Khả năng tương thích IPP Everywhere/Mopria chưa được xác minh. Dùng trên LAN tin cậy vì IPP hiện chưa xác thực và chưa bật TLS. Khi mới chạy, chỉ máy in vật lý được bật chia sẻ; Microsoft Print to PDF, XPS, OneNote, Fax không được quảng bá trừ khi bật thủ công.

Trên Windows, thử lần lượt spooler dừng, hàng đợi đã xóa, quy tắc firewall thiếu, cổng IPP bận, máy in offline và máy hoạt động bình thường. Kiểm tra cả khi chạy tool bằng tài khoản thường. Đo thời điểm mở hộp thoại, nhận lệnh và ra tờ đầu.

### iPhone và quyền in theo thiết bị

Trong **Configure Firewall**, chọn IPv4 của máy chủ rồi bấm **Bật tìm máy in qua Bonjour**. Mở **Chia sẻ → In → Chọn máy in** trên iPhone cùng LAN, sau đó bấm **Quét thiết bị LAN** trên máy chủ. Danh sách kết hợp kết quả quét, bảng ARP và các IP vừa gửi truy vấn Bonjour. Nếu điện thoại chưa xuất hiện, nhập IPv4 ở **Cài đặt → Wi-Fi → ⓘ** trên iPhone.

Bấm **Cho phép in** để bật danh sách IP được duyệt cho toàn bộ máy chủ. PrintBridge kiểm tra IP nguồn của mỗi yêu cầu IPP. **Gỡ quyền in** có hiệu lực cho yêu cầu tiếp theo. IP đã quản lý vẫn xuất hiện khi thiết bị ngoại tuyến. Nếu DHCP đổi IP, gỡ địa chỉ cũ và thêm địa chỉ mới. Quyền trong ứng dụng và khả năng đi qua router/firewall là hai điều kiện riêng.

Khi in từ LAN, PrintBridge gửi việc in xuống Windows ở chế độ nền và ghi vào **Print Jobs**. Máy chủ vẫn có thể mở hàng đợi thủ công. Cửa sổ trạng thái do phần mềm của hãng máy in điều khiển có thể có tùy chọn riêng.
