# Agent Settings (직접 수정)

이 파일을 바꾸면 이후 에이전트 작업이 여기 값을 따른다.
`true` / `false` 와 목록만 고치면 된다. Rule 본문은 `.cursor/rules/` 에 있다.

## 언어 / 소통

- `response_language`: `ko`
- `explain_changes`: `true`
- `ask_before_large_refactors`: `true`
- `commit_message_language`: `ko`
- `code_comment_language`: `ko`
- `prefer_plain_korean`: `true`
    - 커밋 메시지·코드 주석은 **쉬운 한글**로 쓴다 (왜 바꿨는지 / 무엇을 하는지).
    - 식별자(변수·함수·JSON 필드·API 경로)·외부 프로토콜 용어는 기존 영문 그대로 둔다.
    - 불필요한 영문 커밋 스타일(Conventional Commits 강제 등)은 쓰지 않는다.

## 작업 범위

- `only_requested_work`: `true`
- `no_drive_by_refactors`: `true`
- `no_unrelated_markdown_docs`: `true`
- `commit_only_when_asked`: `true`
- `push_only_when_asked`: `true`
- `todo_memo_file`: `할일.md`
    - 사용자가 「할일 해줘」「그거 해줘」「메모 해줘」 등으로 요청하면 이 파일을 읽고, 체크되지 않은(`- [ ]`) 항목을 처리한다.
    - 끝난 항목은 `- [x]` 로 바꾸거나 「끝난 것」으로 옮긴다.
    - 비어 있거나 모호하면 짧게 확인한 뒤 진행한다.

## UI / 검증

- `verify_web_ui_in_browser`: `true`
- `verify_empty_and_error_states`: `true`

## 보안

- `never_store_nas_password`: `false`
- `store_nas_credentials_encrypted_when_remembered`: `true`
- `bridge_bind_localhost_only`: `true`
- `do_not_log_credentials`: `true`

## 데이터 모델 (호환성)

웹 JSON 필드명은 외부 C# 모델과 맞춰 유지한다. 오타 필드도 바꾸지 않는다.

- `id`: 문자열 UUID
- `scheduleDate` / `releaseDate`: `YYYY-MM-DD`
- `startTime`: `HHmm` 정수 (예: `1030`). 10분 단위. C# `hour * 10 + minute / 10` 과 다름
- `scheduleTitle`, `accessoryInfo`, `sermonSpeaker`: 문자열
- `place`: `None` | `Main_Large` | `Main_Medium` | `Main_Small` | `Edu_Large` | `Edu_Medium` | `Edu_Small` | `Edu_Nongseon` | `Edu_Siseon` | `Edu_Go2` | `Etc`
- `preparation`: 문자열 배열. `Recording` | `Praise` | `Streaming` | `OtherDisplay`
- `preaparationNote`: 철자 그대로 유지 (의도적 호환 필드)
- `officialDocument` (선택): `_official_documents` 폴더 안 이미지 파일명. 링크 목록은 `_official_documents/links.json`
- `color` (선택): `orange` | `green` | `yellow` | `teal` | `blue` | `coral` | `sage` | `sand` | `slate`. 없으면 `orange`
- 공유사항: NAS 루트의 `_shared_notice.json` (`text`, `updatedAt`, `lock`, `history`). `history`는 수정 시점 기록 최대 10개(최신 먼저). `_`로 시작하는 JSON은 일정 파일 목록에서 제외.

기간으로 새 일정을 만들면 날짜마다 항목을 만들고, 모든 항목의 `releaseDate` 는 기간 마지막 날이다.
기존 항목 수정은 기간 UI가 열려 있어도 **그 항목 하나만** 갱신한다.

공유 폴더에 JSON이 여러 개일 때 자동 선택 우선순위:
1. 이 PC에 기억된 최근 사용 파일 (`prefs.last.file` / 현재 연결 파일)
2. 이름이 정확히 `schedule-data.json` (대소문자 무시), 그다음 `scheduledata.json`, `schedule.json`
3. 이름에 `schedule` 이 포함된 첫 파일
4. 가나다(로케일) 정렬 첫 파일

## 제품 제약

- 배포 단위는 Windows `BroadcastingSchedule.exe` / macOS `BroadcastingSchedule` (웹은 브리지에 임베드).
- 브리지 API는 `http://127.0.0.1:17821` 만 사용한다.
- 형제 앱 포트: WorkLog 17822, ScheduleReader 17823, CtrlOne 5177 (겹치면 안 됨).
- 같은 NAS를 WorkLog과 동시에 쓸 수 있게, Windows SMB 연결 시 기존 세션을 함부로 끊지 않는다.
- Windows에서 연결 실패 시에도 `net use /delete`·`WNetCancelConnection2`로 호스트 전체 세션을 강제 정리하지 않는다 (형제 앱 보호).
- 자격 증명 충돌(1219)이면 사용자에게 동일 NAS 계정 사용을 안내한다.
- 프론트는 프레임워크 없이 `index.html` / `app.js` / `styles.css` 를 유지한다.
- 브라우저 검증이 안 되면 가능한 대체 수단으로 확인하고, 못 한 범위를 명시한다.
