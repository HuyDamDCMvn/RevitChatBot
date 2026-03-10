# MEP Calculation Formulas / Công thức tính toán MEP

## 1. Duct Sizing — Tính kích thước ống gió

### Diện tích tiết diện cần thiết
```
A = Q / V

A = diện tích (m²)
Q = lưu lượng gió (m³/s)
V = vận tốc (m/s)
```

### Ống gió chữ nhật (W × H)
```
A = W × H (m²)

Với tỷ lệ cạnh (aspect ratio) ≤ 4:1
W = sqrt(A × AR)
H = A / W

AR = aspect ratio = W/H (thường 2:1 hoặc 3:1)
```

### Đường kính tương đương (ống tròn ↔ chữ nhật)
```
De = 1.3 × (W × H)^0.625 / (W + H)^0.25

De = đường kính tương đương (mm)
W = chiều rộng (mm)
H = chiều cao (mm)
```

### Kích thước ống gió tiêu chuẩn (mm)
```
100, 125, 150, 200, 250, 300, 350, 400, 450, 500,
550, 600, 650, 700, 750, 800, 900, 1000, 1100, 1200,
1400, 1500, 1600, 1800, 2000
```

## 2. Pipe Sizing — Tính kích thước ống nước

### Đường kính ống
```
d = sqrt(4Q / (π × V))

d = đường kính trong (m)
Q = lưu lượng (m³/s)
V = vận tốc (m/s)
```

### DN tiêu chuẩn (mm)
```
DN15, DN20, DN25, DN32, DN40, DN50, DN65, DN80,
DN100, DN125, DN150, DN200, DN250, DN300, DN350,
DN400, DN450, DN500, DN600
```

## 3. Pressure Loss — Tổn thất áp suất

### Darcy-Weisbach (ống gió & ống nước)
```
ΔP = f × (L/D) × (ρ × V²/2)

ΔP = tổn thất áp suất (Pa)
f = hệ số ma sát (Moody diagram)
L = chiều dài ống (m)
D = đường kính thủy lực (m)
ρ = mật độ lưu chất (kg/m³)
V = vận tốc (m/s)
```

### Đường kính thủy lực
```
Ống tròn: Dh = D
Ống chữ nhật: Dh = 2WH / (W + H)
```

### Hazen-Williams (ống nước)
```
V = 0.849 × C × R^0.63 × S^0.54

V = vận tốc (m/s)
C = hệ số HW (thép: 120, đồng: 140, nhựa: 150)
R = bán kính thủy lực (m) = D/4 cho ống tròn
S = gradient áp suất = ΔP / (ρgL)
```

### Tổn thất cục bộ (phụ kiện)
```
ΔP_fitting = K × (ρ × V²/2)

K = hệ số tổn thất cục bộ
Co 90°: K = 0.3-1.5 (tùy loại)
Tee nhánh: K = 0.5-1.8
Van cổng (mở): K = 0.1-0.2
Van bướm (mở): K = 0.3-0.5
```

## 4. Heat Load — Tải nhiệt

### Tải lạnh
```
Q = m × Cp × ΔT

Q = công suất (kW)
m = lưu lượng khối lượng (kg/s)
Cp = nhiệt dung riêng (kJ/kg°C)
    Nước: 4.186, Không khí: 1.005
ΔT = chênh lệch nhiệt độ (°C)
    CHW: 5°C (7→12°C), AHU coil: 8-12°C
```

### Lưu lượng nước lạnh từ tải
```
m = Q / (Cp × ΔT)
Flow (L/s) = Q(kW) / (4.186 × ΔT)

Ví dụ: FCU 10kW, ΔT=5°C
→ Flow = 10 / (4.186 × 5) = 0.478 L/s
```

### Lưu lượng gió từ tải
```
Q_air = Q_cool / (ρ × Cp × ΔT)

ρ_air = 1.2 kg/m³
Cp_air = 1.005 kJ/kg°C
ΔT = T_room - T_supply (thường 8-12°C)

Ví dụ: Q = 10kW, ΔT=10°C
→ Q_air = 10 / (1.2 × 1.005 × 10) = 0.83 m³/s = 830 L/s
```

## 5. Unit Conversions — Chuyển đổi đơn vị

### Lưu lượng gió
```
1 m³/s = 1000 L/s = 3600 m³/h = 2119 CFM
1 CFM = 0.472 L/s = 1.699 m³/h
1 L/s = 2.119 CFM = 3.6 m³/h
```

### Lưu lượng nước
```
1 m³/s = 1000 L/s = 15850 GPM
1 L/s = 15.85 GPM = 3.6 m³/h
1 GPM = 0.0631 L/s
```

### Áp suất
```
1 Pa = 0.004 inWG = 0.102 mmWG
1 inWG = 249 Pa = 25.4 mmWG
1 kPa = 4.015 inWG = 0.145 psi
1 bar = 100 kPa = 14.5 psi = 10.2 mWG
```

### Chiều dài
```
1 ft = 0.3048 m = 304.8 mm
1 m = 3.281 ft
1 inch = 25.4 mm
```

### Công suất
```
1 RT (Refrigeration Ton) = 3.517 kW = 12000 BTU/h
1 kW = 3412 BTU/h = 0.284 RT
1 HP = 0.746 kW
```

### Nhiệt độ
```
°F = °C × 9/5 + 32
°C = (°F - 32) × 5/9
```

## 6. Sprinkler Calculation — Tính toán Sprinkler

### Lưu lượng tại đầu sprinkler
```
Q = K × sqrt(P)

Q = lưu lượng (L/min)
K = K-factor (phổ biến: K80, K115, K160)
P = áp suất tại đầu phun (bar)
```

### Diện tích phun (Design Area)
```
Light Hazard: 139 m² (1500 ft²)
Ordinary Hazard 1: 139 m² (1500 ft²)
Ordinary Hazard 2: 139 m² (1500 ft²)
Extra Hazard: 232 m² (2500 ft²)
```

### Mật độ phun
```
Light Hazard: 4.1 mm/min (0.10 GPM/ft²)
OH-1: 6.1 mm/min (0.15 GPM/ft²)
OH-2: 8.2 mm/min (0.20 GPM/ft²)
```

### Số đầu sprinkler trong design area
```
N = Design Area / Coverage per head
Light Hazard: N = 139/21 ≈ 7 heads minimum
```
