# Snow Boarder

Game trượt tuyết 2D được xây dựng bằng Unity.

## Bắt đầu

1. Mở project bằng Unity **6000.6.2f1**.
2. Mở `Assets/Scenes/SnowBoarder.unity`.
3. Nhấn **Play**, sau đó chọn **PLAY** trong menu game.

Màn chơi được tạo khi chạy. Để tạo bản Windows, chọn **Snow Boarder > Build Windows Game** trong Unity Editor.

## Điều khiển

- **A/D** hoặc **←/→**: nghiêng người; xoay khi đang trên không.
- **W** hoặc **↑**: tăng tốc.
- **S** hoặc **↓**: phanh.
- **Space**: nhảy.
- **Escape**: tạm dừng hoặc tiếp tục.

## Nội dung

Game có ba màn với độ khó tăng dần. Thu thập snowflake và vật phẩm, thực hiện trick, vượt chướng ngại vật và checkpoint để đạt điểm cao. Tiến độ mở khóa màn và tùy chọn được lưu bằng PlayerPrefs.

## Kiểm thử

Bộ Play Mode test nằm trong `Assets/Tests/SnowBoarderPlayTests.cs`. Trong Unity, mở **Window > General > Test Runner**, chọn **PlayMode** và chạy `FullRunLifecycle`.

## Tài nguyên

Asset gốc được giữ trong `Assets/LabAssets`; các bản dùng lúc chạy nằm trong `Assets/Resources`. Không đưa `Library`, `Temp`, `obj` hoặc `Logs` vào bản nộp; Unity có thể tạo lại các thư mục này.
