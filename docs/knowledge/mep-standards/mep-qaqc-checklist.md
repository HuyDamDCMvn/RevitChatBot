# MEP QA/QC Checklist / Checklist kiểm tra chất lượng MEP

## Workflow kiểm tra tổng quát

```
GetProjectContext → CheckConnections → CheckInsulationCoverage
→ CheckFireDampers → CheckElevationConflicts → CheckDuctVelocity
→ CheckPipeSlope → CheckClashes → CheckOversizedElements → GenerateReport
```

## 1. Kết nối (Connections)

### Tool: `CheckConnections`
- [ ] Tất cả ống gió có đầu nối kín (không có open connector)
- [ ] Tất cả ống nước có đầu nối kín
- [ ] Thiết bị (FCU, AHU) được nối vào hệ thống
- [ ] Phụ kiện (fitting) kết nối đúng 2 đầu

### Mức độ nghiêm trọng:
- **Critical**: Thiết bị chính không nối (AHU, Chiller, Pump)
- **Major**: Ống chính bị hở đầu
- **Minor**: Ống nhánh cuối chưa nối miệng gió/thiết bị

## 2. Gán hệ thống (System Assignment)

### Tool: `GetMepSystems` + `QueryDucts/QueryPipes`
- [ ] Mọi ống gió thuộc một hệ thống (SA, RA, EA, FA)
- [ ] Mọi ống nước thuộc một hệ thống (CHW, HW, SAN, STM, FP)
- [ ] Không có phần tử "(Unassigned)" hoặc "Default"
- [ ] Tên hệ thống theo quy ước dự án

## 3. Bảo ôn (Insulation)

### Tool: `CheckInsulationCoverage`
- [ ] Ống nước lạnh (CHW) có bảo ôn 100%
- [ ] Ống gió cấp (SA) có bảo ôn
- [ ] Ống nước nóng (HW) có bảo ôn
- [ ] Ống nước ngưng (CON) có bảo ôn
- [ ] Độ dày bảo ôn đúng theo tiêu chuẩn

### Ngoại lệ (không cần bảo ôn):
- Ống gió thải (EA) trong nhà
- Ống thoát nước (SAN, STM)
- Ống chữa cháy (FP) trong nhà

## 4. Van chống cháy (Fire Damper)

### Tool: `CheckFireDampers`
- [ ] Fire damper tại mọi vị trí ống xuyên tường/sàn chịu lửa
- [ ] Fire damper kết nối đúng 2 đầu (connected)
- [ ] Loại fire damper phù hợp (1h hoặc 2h fire rating)
- [ ] Không bị thiếu fire damper tại vị trí bắt buộc

## 5. Độ cao thông thủy (Clearance)

### Tool: `CheckElevationConflicts`
- [ ] Hành lang: ≥ 2.40m dưới ống/duct thấp nhất
- [ ] Tầng hầm: ≥ 2.10m dưới dầm + ống
- [ ] Khu văn phòng: ≥ 2.60m dưới trần giả
- [ ] Không có ống/duct nằm dưới dầm kết cấu mà vướng lối đi

## 6. Vận tốc gió (Duct Velocity)

### Tool: `CheckDuctVelocity`
- [ ] Ống chính: ≤ 12 m/s
- [ ] Ống nhánh: ≤ 6 m/s (khu yên tĩnh), ≤ 8 m/s (khu kỹ thuật)
- [ ] Ống thải: ≤ 10 m/s
- [ ] Không có ống gió vận tốc > 15 m/s

## 7. Độ dốc ống (Pipe Slope)

### Tool: `CheckPipeSlope`
- [ ] Ống thoát SAN DN100: ≥ 1.0%
- [ ] Ống thoát SAN DN50-75: ≥ 2.0%
- [ ] Ống nước ngưng CON: ≥ 1.0%
- [ ] Không có ống thoát nước dốc ngược (negative slope)

## 8. Va chạm (Clash Detection)

### Tool: `CheckClashes`
- [ ] Duct vs Pipe: không va chạm bounding box
- [ ] Pipe vs Pipe: các hệ thống khác nhau không chồng
- [ ] MEP vs Structure: ống không xuyên dầm/cột không đúng vị trí

### Thứ tự ưu tiên khi giải quyết clash:
1. Structure (không di chuyển)
2. Gravity drain pipes (SAN, STM — cần độ dốc)
3. Large ducts (khó di chuyển)
4. Small pipes (dễ di chuyển nhất)

## 9. Kích thước (Sizing)

### Tool: `CheckOversizedElements` + `GetDuctSummary` + `GetPipeSummary`
- [ ] Ống gió không vượt quá kích thước tối đa cho phép
- [ ] Ống nước không vượt quá DN tối đa
- [ ] Kích thước nhất quán trên cùng nhánh (không giảm rồi tăng)

## 10. Tham số bắt buộc (Required Parameters)

### Tool: `ComplianceCheck`
- [ ] System Type: tất cả phần tử có system type
- [ ] Level: tất cả phần tử gán đúng tầng
- [ ] Size: tất cả ống có kích thước
- [ ] Mark/Tag: phần tử có mã nhận dạng (nếu yêu cầu)
- [ ] Comments: ghi chú theo yêu cầu dự án

## Báo cáo kết quả

### Tool: `GenerateReport`
- Tổng hợp tất cả kết quả kiểm tra
- Phân loại: Critical / Major / Minor
- Liệt kê Element ID để dễ navigate trong Revit
- Thống kê tỷ lệ hoàn thành (completion rate)
