# Agent Settings (직접 수정)

이 파일을 바꾸면 이후 에이전트 작업이 여기 값을 따른다.
`true` / `false` 와 목록만 고치면 된다. Rule 본문은 `.cursor/rules/` 에 있다.

## 언어 / 소통

- `response_language`: `ko`
- `explain_changes`: `true`
- `ask_before_large_refactors`: `true`
- `commit_message_language`: `ko`
- `code_comment_language`: `ko`
- `prefer_korean_identifiers_in_docs`: `true`

## 작업 범위

- `only_requested_work`: `true`
- `no_drive_by_refactors`: `true`
- `no_unrelated_markdown_docs`: `true`
- `commit_only_when_asked`: `true`
- `push_only_when_asked`: `true`
- `todo_file`: `docs/TODO.md` — 고칠 것·할 일·메모. 「할 일 해줘」「N번 해줘」면 이 파일을 보고 처리 후 체크.

## UI / 검증

- `verify_web_ui_in_browser`: `true`
- `verify_empty_and_error_states`: `true`

## 보안

- `never_store_nas_password`: `false`
- `store_nas_credentials_encrypted_when_remembered`: `true`
- `bridge_bind_localhost_only`: `true`
- `do_not_log_credentials`: `true`

## NAS / 경로

- 일지 루트(접속 share 하위): `Temp DATA\_data\_work_log`
- 스케줄 JSON: 상위 `_data` — **읽기 전용** (일지 앱이 쓰지 않음)
- 일지: `work-logs/YYYY-MM.json`
- 규칙: `defaults.json` (v2: `titleRules` + `templateButtons`)
- 프로필: `profiles.json` (고윤석 / 김종희 / 임원빈)
- 감사(UI 명칭「로그」): `audit/YYYY-MM.jsonl`

## 제품 제약

- Phase 1: **NAS에 서버/설정을 켤 필요 없음**. PC의 `WorkLog.exe`만 실행하고 SMB로 공유 폴더에 접속한다.
- 「연결 정보 기억」이 켜져 있으면 실행 시 자동 SMB 접속 + 마지막 프로필로 일지 화면 진입을 시도한다.
- Phase 1 배포: `WorkLog.exe` 하나. 웹(`www/`)은 LocalBridge에 임베드.
- LocalBridge: `http://127.0.0.1:17822` (스케줄 관리자 17821 · ScheduleReader 17823 · CtrlOne 5177 과 충돌 금지 — `docs/ports.md`)
- 배포 기본 출력: `D:\Projects\Builded\WorkLog` (`package-portable.ps1` / `package-mac.ps1`)
- 프론트: 프레임워크 없이 `index.html` / `app.js` / `styles.css` / `templates.js`
- Phase 2: `nas-api` — 동일 REST 계약, `DATA_ROOT` 마운트
- 프로필 UI에는 **표시 이름만** (영문 id 숨김)
- 스케줄 연동 필드의 오타 `preaparationNote` 는 호환을 위해 유지
- 같은 NAS를 쓰는 앱과 동시 실행 시 SMB 세션을 함부로 끊지 않음. 일지 쓰기는 revision 충돌 감지.
- Windows Connect 실패 시에도 호스트 전체 SMB 세션을 강제 삭제하지 않음 (형제 앱 보호).
## 커밋 메시지 (한글)

`commit_message_language` 이 `ko` 이면:

- 제목 1줄: 무엇을/왜 바꿨는지 한글로 짧게 (예: `인쇄 설정 화면에 동반 날짜 선택 추가`)
- 본문이 필요하면 한글로 이유·영향 범위만. **간결한 체언/명사형**으로 끝낸다 (예: `~반영.`, `~수정.`, `~추가.`). `~한다.`, `~했다.` 같은 서술형은 쓰지 않는다.
- Conventional Commit 영문 prefix(`feat:`) 강제하지 않음. 팀이 원하면 `기능:` / `수정:` / `문서:` 정도는 허용
- 사용자가 커밋을 요청했을 때만 커밋 (`commit_only_when_asked`)

## 코드 주석 (한글)

`code_comment_language` 이 `ko` 이면:

- 새로 다는 주석·XML doc·중요 블록 설명은 **한글** 우선
- 식별자(함수명·변수명·JSON 필드·API 경로)는 기존 영문/camelCase 유지 (호환)
- 자명한 한 줄 코드에 주석을 달지 않음. 「왜」를 설명할 때만
- 사용자에게 보이는 UI 문구는 한글 유지
