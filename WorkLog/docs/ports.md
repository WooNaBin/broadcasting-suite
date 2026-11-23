# 로컬 포트 예약표

네 프로그램이 동시에 실행될 수 있으므로 **포트는 겹치지 않게** 고정한다.

| 프로그램 | 저장소 | 포트 | URL |
|----------|--------|------|-----|
| 스케줄 관리자 (Broadcasting Schedule) | schedule-data-manager | **17821** | http://127.0.0.1:17821 |
| WorkLog (업무일지) | WorkLog | **17822** | http://127.0.0.1:17822 |
| ScheduleReader (일정표 OCR) | ScheduleReader | **17823** | http://127.0.0.1:17823 |
| CtrlOne | CtrlOne | **5177** | http://127.0.0.1:5177 |

## NAS 공유

위 세 개(스케줄 관리자 · WorkLog · ScheduleReader 결과 import)는 같은 NAS의 스케줄 JSON을 공유한다.

- 스케줄 JSON: `_data` (WorkLog는 **읽기만**)
- 업무일지: `_data\_work_log` (WorkLog만 쓰기)
- WorkLog / 스케줄 관리자는 연결 시 SMB 세션을 **함부로 끊지 않음** (동시 실행 대비)
- 연결 실패 시에도 호스트 전체 `net use /delete` 강제 정리를 하지 않음 (형제 앱 보호, 2026-09-08)
- FileChecker는 `Permanent DATA` 등을 스캔하지만 **같은 NAS IP면 Windows 계정은 하나** — 세 앱 계정 통일 권장
- WorkLog 쓰기는 `X-File-Revision` 으로 동시 수정 충돌을 감지
- 상위 개요 문서: `D:\Projects\PROJECTS.md`
