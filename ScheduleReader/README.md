# ScheduleReader

교회 **월간 일정 엑셀**을 읽어 [ScheduleDataManager](../ScheduleDataManager/)용 JSON으로 준비합니다.

ASP.NET Core 호스트 + 브라우저 UI (포트 **17823**). Python 런타임이 필요 없습니다.

## 실행 (개발)

```powershell
cd d:\Projects\ScheduleReader
dotnet run --project ScheduleReader.csproj
```

또는 `serve.bat` (빌드된 exe가 있을 때).

브라우저: http://127.0.0.1:17823

1. **일정 엑셀 불러오기** (`.xlsx` / `.xlsm`)
2. 시트 선택·미리보기
3. **일정 추출**
4. **일정 데이터 내보내기** → `schedule-data.json` (top-level 배열)

내보내는 필드: `scheduleDate`, `releaseDate`, `startTime`, `scheduleTitle`, `place`, `sermonSpeaker`, `preparation`, `preparationNote`/`preaparationNote`, `color`, `colorMode`. BSS 라이브 `/schedule/`·`/worklog/` 및 임원빈 `/materials/` 가져오기와 맞춥니다. `color` 기본값은 `auto` 입니다.

### 지원하는 엑셀 형태

| 형태 | 설명 |
| --- | --- |
| **표(헤더)** | `날짜`·`제목` 열 (+ `강사`/`시간`/`장소`/`종료일`/`비고`) |
| **달력 격자** | `일~토` 요일 헤더 + 칸에 날짜번호와 일정 텍스트 (시트/상단에 `2026년 9월` 권장) |
| **도형/텍스트상자** | drawing 도형·텍스트상자 글자 + 앵커 아래 날짜 칸 |

## 배포

상위 `D:\Projects\Build-BroadcastApps.bat`로 방송실 앱과 함께 self-contained 단일 exe로 패키징합니다.

- Windows: `ScheduleReader-Windows-x64\ScheduleReader.exe`
- Mac: `ScheduleReader-macOS-arm64` / `x64` (동일 브라우저 호스트)

## 레거시

이전 Python 소스는 `schedule_reader/`에 남아 있습니다. 신규 사용·배포는 C# exe만 사용하세요.

## 메모

- `.xls`(구형)은 미지원 — `.xlsx`로 저장 후 사용.
- 할 일: [`TODO.md`](TODO.md)
