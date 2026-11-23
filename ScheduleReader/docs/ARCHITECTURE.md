# ScheduleReader 구조 (엑셀 파이프라인)

## 흐름

1. 브라우저/CLI에서 `.xlsx` 업로드
2. `excel.session` — 임시 파일 + 시트 목록
3. `excel.extract` — 헤더 표에서 `RawEvent` → `expand_and_normalize` → SDM `ScheduleItem[]`
4. JSON 내보내기 (`schedule-data.json`)

## 패키지

| 경로 | 역할 |
|------|------|
| `schedule_reader/web/` | FastAPI + static UI |
| `schedule_reader/excel/` | 세션·미리보기·추출 |
| `schedule_reader/parser/normalize.py` | SDM 항목 정규화 |
| `schedule_reader/export/` | JSON 기록 |
| `config/filters.yaml`, `place_map.yaml` | (선택) 필터·장소 맵 — 표 추출에서도 place 매핑에 사용 |

## API

- `POST /api/workbook` — 엑셀 업로드
- `POST /api/workbook/sheet` — 시트 전환
- `POST /api/extract` — 추출
- `GET /api/export?sessionId=` — JSON 다운로드 (추출 성공 시)

## 미구현

- 달력 격자형 엑셀
- 이미지 OCR (폐기)
