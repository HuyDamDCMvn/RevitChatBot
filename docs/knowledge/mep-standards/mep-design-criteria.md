# MEP Design Criteria / Tiêu chí thiết kế MEP

## 1. HVAC — Vận tốc gió trong ống (Duct Velocity)

| Ứng dụng | Vận tốc tối đa (m/s) | Ghi chú |
|---|---|---|
| Ống gió chính (Main Duct) | 8 - 12 | Khu vực không yêu cầu thấp tiếng ồn |
| Ống nhánh (Branch Duct) | 4 - 6 | Khu văn phòng, phòng khách sạn |
| Ống gió hồi (Return Duct) | 4 - 6 | Tương tự ống nhánh |
| Ống thải (Exhaust Duct) | 8 - 10 | Nhà vệ sinh, bếp |
| Ống tăng áp cầu thang | 10 - 15 | Hệ thống PCCC |
| Ống hút khói | 10 - 15 | Hệ thống PCCC |
| Miệng gió cấp (Supply Diffuser) | 2.5 - 4.0 | Tại cổ miệng gió |
| Miệng gió hồi (Return Grille) | 2.0 - 3.0 | Tại mặt grille |

### Ngưỡng cảnh báo:
- **Vàng (Warning)**: Vượt 80% vận tốc tối đa
- **Đỏ (Critical)**: Vượt 100% vận tốc tối đa
- Vận tốc quá cao gây: tiếng ồn, rung, tổn thất áp suất lớn, mài mòn

## 2. Plumbing — Vận tốc nước trong ống (Pipe Velocity)

| Ứng dụng | Vận tốc tối đa (m/s) | Ghi chú |
|---|---|---|
| Nước lạnh (CHW) ống chính | 1.5 - 3.0 | Ống > DN100 |
| Nước lạnh (CHW) ống nhánh | 1.0 - 1.5 | Ống ≤ DN100 |
| Nước nóng (HW) | 1.0 - 2.5 | Tương tự CHW |
| Nước giải nhiệt (CW) | 1.5 - 3.0 | Tương tự CHW |
| Nước cấp sinh hoạt (DW) | 1.0 - 2.5 | Ống chính |
| Nước sinh hoạt nhánh | 0.8 - 1.5 | Tới thiết bị |
| Ống thoát nước thải (SAN) | 0.7 - 2.5 | Tự chảy |
| Ống thoát nước mưa (STM) | 1.0 - 3.0 | Tự chảy |
| Ống chữa cháy (FP) | 3.0 - 5.0 | Khi hoạt động |

## 3. Độ dốc ống (Pipe Slope)

| Loại ống | Độ dốc tối thiểu | Ghi chú |
|---|---|---|
| Thoát nước thải (SAN) DN50-75 | 2.0% (20 mm/m) | Ống nhỏ dốc nhiều hơn |
| Thoát nước thải (SAN) DN100 | 1.0% (10 mm/m) | Tiêu chuẩn |
| Thoát nước thải (SAN) DN150+ | 0.5% (5 mm/m) | Ống lớn |
| Thoát nước mưa (STM) | 0.5 - 1.0% | Tùy kích thước |
| Nước ngưng (CON) | 1.0 - 2.0% | Đảm bảo thoát nước |
| Ống nước lạnh (CHW) | 0% (ngang) | Có bơm, không cần dốc |

### Ngưỡng cảnh báo:
- **Đỏ**: Ống thoát nước có độ dốc < giá trị tối thiểu
- **Vàng**: Ống thoát nước có độ dốc ngược (âm)

## 4. Độ cao thông thủy (Clearance Height)

| Khu vực | Độ cao tối thiểu (m) | Ghi chú |
|---|---|---|
| Hành lang | 2.40 | Dưới ống/duct thấp nhất |
| Văn phòng | 2.60 | Dưới trần giả |
| Lobby / Sảnh | 2.80 - 3.00 | Yêu cầu kiến trúc |
| Tầng hầm xe | 2.10 - 2.20 | Dưới dầm/ống |
| Khu kỹ thuật | 2.00 | Đủ bảo trì |
| Phòng máy | 2.50 | Access cho thiết bị |

## 5. Bảo ôn (Insulation)

### Ống nước lạnh (CHW 7-12°C):
| DN ống | Dày bảo ôn tối thiểu |
|---|---|
| DN15-25 | 25mm |
| DN32-50 | 30mm |
| DN65-100 | 40mm |
| DN125-200 | 50mm |
| DN250+ | 60mm |

### Ống gió lạnh (Supply Air):
- Bảo ôn tối thiểu: 25mm (trong nhà), 50mm (ngoài trời)
- Vật liệu: PE foam, cao su xốp, hoặc bông khoáng
- Hệ ống gió hồi trong không gian trần: có thể không cần bảo ôn

### Hệ thống KHÔNG cần bảo ôn:
- Ống gió thải (EA) trong nhà
- Ống gió hồi (RA) trong trần
- Ống nước thoát (SAN, STM)
- Ống chữa cháy (FP) — trừ vùng đóng băng

### Hệ thống BẮT BUỘC bảo ôn:
- Ống nước lạnh (CHW) — chống đọng sương
- Ống gió cấp lạnh (SA) — chống đọng sương, giảm tổn thất nhiệt
- Ống nước nóng (HW) — giảm tổn thất nhiệt
- Ống nước ngưng (CON) — chống đọng sương

## 6. Tiếng ồn (Noise Criteria)

| Loại phòng | NC tối đa | dB(A) tương đương |
|---|---|---|
| Phòng thu âm | NC-15 | 25 |
| Phòng ngủ khách sạn | NC-25 | 35 |
| Văn phòng riêng | NC-30 | 40 |
| Văn phòng mở | NC-35 | 45 |
| Phòng họp | NC-25 | 35 |
| Nhà hàng | NC-40 | 50 |
| Hành lang | NC-40 | 50 |
| Khu kỹ thuật | NC-50 | 60 |

## 7. Van chống cháy (Fire Damper) — Vị trí bắt buộc

- Ống gió xuyên tường chịu lửa (fire-rated wall)
- Ống gió xuyên sàn chịu lửa (fire-rated floor)
- Ống gió xuyên vách ngăn cháy (fire barrier)
- Ống gió xuyên vách ngăn khói (smoke barrier) → dùng smoke damper hoặc combination

### Kiểm tra trong model:
1. Tìm tất cả fire damper → check connected (có nối ống 2 đầu)
2. Tìm ống gió xuyên tường/sàn chịu lửa → check có fire damper
3. Fire damper phải cùng fire rating với tường/sàn

## 8. Khoảng cách Sprinkler

| Loại nguy hiểm | Spacing tối đa | Diện tích bảo vệ/đầu |
|---|---|---|
| Light Hazard | 4.6m × 4.6m | 21 m² |
| Ordinary Hazard 1 | 4.6m × 4.6m | 12 m² |
| Ordinary Hazard 2 | 4.6m × 4.6m | 12 m² |
| Extra Hazard | 3.7m × 3.7m | 9 m² |

- Khoảng cách từ tường: ≤ 1/2 spacing (tối đa 2.3m)
- Khoảng cách từ deflector đến trần: 25-300mm
- Khoảng cách từ đầu sprinkler đến vật cản: ≥ 3 lần khoảng cách ngang
