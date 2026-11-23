# WorkLog JSON 스키마

NAS 기본 경로 (접속 share/하위경로):

`Temp DATA\_data\_work_log`

## 디렉터리

| 경로 | 설명 |
|------|------|
| `work-logs/YYYY-MM.json` | 월별 업무일지 |
| `defaults.json` | 초안 기본값 (요일/제목/장소) · `specialNotesMax` |
| `special-notes.json` | 방송실 특이사항 메모 (날짜 무관, N개) |
| `profiles.json` | 사용자 프로필 |
| `print-signs.json` | 인쇄 직인란 이름 · 특이사항 카드 제목 · 줄 양식 |
| `audit/YYYY-MM.jsonl` | 감사 로그 (한 줄 = 한 이벤트) |

스케줄 JSON은 상위 `_data` 폴더에서 **읽기 전용**. 일지 앱은 스케줄 파일을 쓰지 않는다.

## profiles.json

```json
{
  "version": 1,
  "profiles": [
    { "id": "ko_yunseok", "displayName": "고윤석" },
    {
      "id": "kim_jonghee",
      "displayName": "김종희",
      "pin": {
        "algo": "pbkdf2-sha256",
        "iterations": 100000,
        "salt": "<base64>",
        "hash": "<base64>"
      }
    },
    { "id": "lim_wonbin", "displayName": "임원빈" }
  ]
}
```

- `id` / `displayName` 필수. `pin`은 선택 — 없으면 최초 선택 시 설정 모달
- PIN은 숫자 1–8자리. 평문 저장 금지. Web Crypto PBKDF2-SHA256(iterations 100000) 해시만 보관
- prefs·audit에는 PIN/해시를 넣지 않음

## defaults.json

```json
{
  "version": 2,
  "specialNotesMax": 10,
  "titleRules": [
    {
      "id": "rule-daejundo",
      "conditionType": "schedule_title",
      "value": "대전도or전도집회",
      "record": "{schedule_title} {place_label} 방송 및 유튜브 송출 준비.",
      "templates": ["BibleSeminar_Main"]
    }
  ],
  "templateButtons": {
    "Sermon_Sunday_Primary": { "appendLog": "주일 말씀 방송 준비", "appendTarget": "A" }
  },
  "customTemplates": [
    { "id": "custom_abc", "label": "사용자 정의 행사" }
  ]
}
```

- `titleRules.conditionType`: `schedule_title` | `accessory_info` | `preparation_note`
- `titleRules.value`: 키워드 표현식. `집회or지원`(하나라도), `집회and지원`(모두). `or`가 `and`보다 먼저 분리됨
- `titleRules.appendTarget`: `"A"` | `"B"` (기본 `"A"`). 기본 일정 기록 시 문구가 들어갈 일지 칸
- `templateButtons.appendTarget`: `"A"` | `"B"` (기본 `"A"`). 템플릿 칩을 켤 때 추가 기록이 들어갈 일지 칸
- `customTemplates`: 사용자가 추가한 업무 템플릿 (버튼·규칙에서 함께 사용)
- `specialNotesMax`: 방송실 특이사항 메모 최대 개수 (1–50, 기본 10)
- 플레이스홀더: `{schedule_title}` `{place}` `{place_label}` `{start_time}` `{sermon_speaker}` `{accessory_info}` `{preparation_note}` `{date}`
- v1(`byWeekday` / `byScheduleTitle`)은 UI 로드 시 자동 마이그레이션

## print-signs.json

```json
{
  "version": 3,
  "names": ["김종희", "임원빈", "방송실장", "총무", "봉사회장"],
  "notesCardTitle": "기타 특이사항 (장소별 방송장비, 차량, 기타 등등)",
  "printCardStyle": "bullet"
}
```

- `printCardStyle`: `bullet` | `number` | `line` | `card` (업무일지 줄 양식, NAS 공유)

## special-notes.json

```json
{
  "version": 1,
  "notes": [
    {
      "id": "uuid",
      "text": "본당 카메라 펌웨어 점검",
      "updatedAt": "2026-09-10T12:00:00+09:00",
      "updatedBy": "lim_wonbin"
    }
  ]
}
```

날짜와 무관한 방송실 특이사항. 인쇄 시 4번째 칸에서 「특이사항」을 고르면 **가장 최근(updatedAt) 비어 있지 않은 메모**가 회색 카드로 들어간다.

## work-logs/YYYY-MM.json

```json
{
  "version": 1,
  "entries": [
    {
      "id": "uuid",
      "date": "2026-09-07",
      "templates": ["Sermon_Sunday_Primary"],
      "workTemplateNote": "",
      "log": "방송 준비",
      "logA": "방송 준비",
      "logB": "장비 정리",
      "isPrinted": false,
      "sourceScheduleIds": [],
      "createdBy": "kim_jonghee",
      "updatedBy": "kim_jonghee",
      "createdAt": "2026-09-07T09:00:00+09:00",
      "updatedAt": "2026-09-07T09:15:00+09:00"
    }
  ]
}
```

- `logA` / `logB`: 인쇄 카드 좌·우 칸. 구버전은 `log`만 있으며 로드 시 `logA`로 이전.
- `log`: 하위 호환용으로 `logA`와 동일하게 유지.
## audit/YYYY-MM.jsonl

```json
{"ts":"2026-09-07T09:15:00+09:00","actor":"ko_yunseok","action":"update","entryId":"...","date":"2026-09-07","summary":"log 수정"}
```

## 템플릿 카탈로그 (UI)

`Sermon_Sunday_Primary`, `Sermon_Sunday_Secondary`, `Sermon_Wednesday`, `Sermon_Thursday`,
`AM_Sunday_Senior`, `AM_Sunday_Galilee`, `BibleAcademy`,
`Asc_All`, `Asc_Leader_All`, `Asc_Leader_Sister`, `Asc_Youth`, `Asc_Brother`, `Asc_Sister`, `Asc_Senior`,
`Wedding`, `BibleSeminar_Main`, `BibleSeminar_Part_L`, `BibleSeminar_Part_M`, `BibleSeminar_Part_S`, `BibleSeminar_Youth`,
`Retreat_Summer`, `Retreat_Autumn`, `Retreat_Winter`, `Retreat_Teacher`, `Retreat_Winter_Youth`, `Retreat_Media`, `Retreat_University`,
`SamuelSchool`

미등록 문자열도 `templates` 배열에 허용한다.

## LocalBridge / NAS API

- Bridge: `http://127.0.0.1:17822`
- 파일 I/O: `X-File-Revision` / `X-Expected-Revision` (409 Conflict)
- Phase 2 NAS API는 동일 REST 계약 사용
