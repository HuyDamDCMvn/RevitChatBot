# Knowledge Base

Thư mục này chứa tài liệu tiêu chuẩn cho RAG (Retrieval-Augmented Generation) module.

## Cấu trúc

```
knowledge/
├── bim-standards/     # BIM Standards (LOD, naming convention, modeling guidelines, ...)
├── mep-standards/     # Tiêu chuẩn MEP (ASHRAE, SMACNA, TCVN, ...)
├── revit-api/         # Tài liệu Revit API
└── project-specs/     # Thông số dự án cụ thể
```

## Định dạng hỗ trợ

- `.txt` - Văn bản thuần
- `.md` - Markdown
- `.json` - JSON có cấu trúc (array of `{ "content": "...", "category": "...", "metadata": {...} }`)

## Hướng dẫn thêm tài liệu

1. Đặt file vào thư mục phù hợp
2. Chatbot sẽ tự động index khi khởi động
3. LLM sẽ tìm kiếm tài liệu liên quan khi trả lời câu hỏi

## Tiêu chuẩn cần bổ sung

### BIM Standards
- [ ] BIM Execution Plan (BEP)
- [ ] LOD Specification (Level of Development)
- [ ] Naming Convention / Classification (UniClass, OmniClass)
- [ ] Modeling Guidelines (MEP coordination, clash tolerance)
- [ ] QA/QC Checklist

### MEP Standards
- [ ] ASHRAE Handbook - HVAC Systems
- [ ] SMACNA - Duct Construction Standards
- [ ] TCVN về PCCC, cấp thoát nước
- [ ] IEC/IEEE - Electrical standards

### Khác
- [ ] Revit API 2025 reference notes
