# Snow Boarder — Lab 2

## Mở và chơi

Mở thư mục SnowBoarder bằng Unity 6000.6.2f1. Mở Assets/Scenes/SnowBoarder.unity (hoặc menu Snow Boarder > Open Game Scene), bấm Play rồi PLAY. Course được dựng khi chạy; scene trong Editor chỉ chứa bộ điều khiển tạo course.

Menu chính có PLAY (mở màn hình chọn màn), HOW TO PLAY (hướng dẫn điều khiển và nút bấm), OPTIONS và QUIT. Sau khi chọn PLAY, người chơi chọn một trong ba màn: Stage 1 EASY, Stage 2 NORMAL, Stage 3 HARD. Màn sau bị khoá cho tới khi vượt qua màn trước; tiến độ mở khoá được lưu qua PlayerPrefs.

- A / D hoặc mũi tên trái / phải: nghiêng người; xoay khi ở trên không.
- W / mũi tên lên: tăng tốc.
- S / mũi tên xuống: phanh.
- Space: nhảy; có khoảng đệm 0,12 giây sau khi rời mặt đất.
- Escape: tạm dừng / tiếp tục.

Menu Options chỉnh âm lượng và bật/tắt tuyết. Menu Snow Boarder > Build Windows Game tạo executable trong Build/Windows.

## Quyết định thiết kế

Game dùng góc nhìn ngang 2D và gồm ba màn (Stage 1-3) trên cùng một sườn núi dài ~690 đơn vị. Cả ba màn xuất phát cùng chỗ nhưng đích xa dần và mật độ chướng ngại tăng dần (EASY, NORMAL, HARD), mục tiêu thời gian khoảng 30 / 45 / 60 giây. Rigidbody2D chịu trọng lực, động lượng và lực đẩy dọc tiếp tuyến đường trượt; EdgeCollider2D có ma sát thấp như tuyết. Gia tốc và phanh tác động bằng lực, còn giới hạn tốc độ giúp tránh mất kiểm soát. Camera theo người chơi với độ trễ mềm.

Sprite người chơi được ghép từ Boarder_Top và Boarder_Bottom. Hai sprite gốc cùng mật độ pixel, nên thân được vẽ đúng theo tỉ lệ đó (chiều cao thân = chiều cao board × tỉ lệ kích thước sprite, đặt tại hông -0,266; 0,49) thay vì phóng to; ở menu ô vẽ cũng đổi sang 719; 364,5; 270,8; 181,8. Nhờ vậy áo và chân khớp nhau cả trong game lẫn menu. Cây, đá, mây và âm thanh Crash/Finish lấy từ LabAssets. Bông tuyết, boost, shield, cờ đích, cờ checkpoint, trái tim mạng, đồng hồ và mask xoá tròn (radial wipe) được tạo bằng mã vẽ texture khi chạy. Material lưu trong Resources để shader vẫn được giữ khi build.

HUD trong màn không còn thanh nền xanh đậm; chữ được vẽ kèm bóng đổ để nổi trên nền trời. HUD gồm điểm, tốc độ km/h kèm icon đồng hồ tốc độ, ba trái tim mạng (vẽ lại dạng bóng, có highlight) kèm số mạng, combo, đồng hồ bấm giờ kèm icon, màn hiện tại, số checkpoint đã qua, và hai icon boost/shield với radial wipe đếm ngược. Dòng hướng dẫn điều khiển đã được bỏ khỏi HUD và chỉ còn ở mục HOW TO PLAY. Người chơi có ba mạng. Va chạm cây/đá đưa người chơi về checkpoint gần nhất: nếu đang có khiên thì mất khiên (không mất mạng), nếu không có khiên thì mất một mạng. Khi đang trong thời gian bảo vệ, va chạm vẫn được đẩy về checkpoint nhưng không gia hạn thêm thời gian bảo vệ, nên cứ đâm vào vật cản là mất mạng dần chứ không lặp vô hạn mà không bị trừ. Rơi ngoài đường hoặc tiếp đất với board lệch quá 100 độ cũng gây crash và về checkpoint. Checkpoint là cột cờ trigger, kích hoạt theo tiến độ x (không trượt qua khi đang bay) và đổi màu khi đã kích hoạt.

Tốc độ trượt thường đã giảm còn khoảng 60% (giới hạn ~11,5 đơn vị/giây, boost ~20). Lực xoay khi ở trên không cũng giảm nên cú lộn không còn quá nhanh. Trigger của vật phẩm (khiên, boost) được nới rộng nên người chơi không trượt qua mà không nhận.

Điểm di chuyển tăng theo vận tốc tiến. Snowflake cho 100 điểm cơ bản. Xoay đủ vòng khi nhảy cho 300 điểm/vòng; nhảy lâu hơn 0,7 giây và tiếp đất an toàn cho 75 điểm. Thu vật phẩm hoặc hoàn thành trick trong cửa sổ năm giây nối combo; multiplier tăng đến x8. Crash xóa combo. Về đích thưởng 500 điểm cho mỗi mạng còn lại.

Ba màn dùng chung điểm xuất phát nhưng dài dần theo độ khó (EASY ~30 giây, NORMAL ~45 giây, HARD ~60 giây); càng khó càng nhiều cây/đá và càng ít checkpoint (1, 2 và 3 cờ). Boost tăng lực và tốc độ trong năm giây, hiển thị bằng icon có radial wipe đếm ngược. Shield chống một lần va chạm vật cản trong bốn giây; khi đang có khiên mà chạm cây/đá thì mất khiên và quay về checkpoint gần nhất. Trigger về đích kết thúc màn hiện tại: hiện màn hình "STAGE CLEARED", mở khoá màn kế tiếp và cho chuyển sang màn đó; màn ba kết thúc bằng màn hình thắng. Điểm, mạng và combo được giữ nguyên qua các màn. RIDE AGAIN khôi phục collectible và chơi lại màn hiện tại; vào lại màn sẽ đặt lại toàn bộ checkpoint.

## Khó khăn và cách xử lý

Địa hình gợn sóng cần một collider liên tục để tránh board mắc ở ranh giới từng đoạn. Mesh và EdgeCollider dùng cùng một hàm độ cao. Tính điểm trick dựa trên tổng góc xoay trong thời gian bay, sau đó kiểm tra góc tiếp đất. Rigidbody có continuous collision detection để giảm xuyên chướng ngại vật. Nhập liệu dùng Input System tương thích thiết lập sẵn của project.

## Tính năng thêm

Lưu điểm cao nhất, lựa chọn âm lượng/tuyết và tiến độ mở khoá màn qua PlayerPrefs; pause/resume; hiệu ứng tuyết rơi; camera theo mượt; màn hình HOW TO PLAY liệt kê phím và nút; màn hình chọn màn có khoá/mở khoá; ba màn độ khó tăng dần; checkpoint; icon boost/shield với radial wipe; đồng hồ bấm giờ; mạng hiển thị bằng ba trái tim vẽ bằng mã. Bài không triển khai các mục optional chọn nhân vật, time trial hoặc đổi trang phục.

## Kiểm thử

Bộ kiểm tra nằm trong Assets/Tests/SnowBoarderPlayTests.cs và không có trong bản build game. Mở Window > General > Test Runner, chọn PlayMode, chạy FullRunLifecycle. Kiểm tra tải asset/âm thanh, chuyển động vật lý, điểm, combo, shield, crash, hết mạng, restart, tiến ba màn (LevelComplete rồi Won) và trigger đích.

## Nộp bài

Nộp Assets, Packages, ProjectSettings và tài liệu này. Không cần nộp Library, Temp, obj hoặc Logs. Nguồn asset gốc được giữ trong Assets/LabAssets; bản dùng lúc chạy nằm trong Resources.


