# 방송실 PC 배포 체크리스트

통합 zip: `Builded\BroadcastingApp_<version>.<build>_<yyyyMMdd>.zip`

## 공통

- [ ] Windows 10/11 x64
- [ ] 포트 충돌 없음: 17821(SDM) / 17822(WorkLog) / 17823(ScheduleReader) / 5177(CtrlOne) / 5187(FileChecker)
- [ ] **SDM · WorkLog · FileChecker · 탐색기** 모두 **같은 NAS 계정** 사용 (Windows는 서버당 계정 1세트)

## ScheduleDataManager

- [ ] `BroadcastingSchedule.exe` 실행 → http://127.0.0.1:17821
- [ ] 공유 예: `Temp DATA\_data`

## WorkLog

- [ ] `WorkLog.exe` → http://127.0.0.1:17822
- [ ] 공유 예: `Temp DATA\_data\_work_log`

## FileChecker

- [ ] `FileCheckerFinder.exe` 또는 `Start-FileCheckerFinder.bat` **하나만** 실행
- [ ] 설정에 스케줄 JSON 경로 지정 후 **Recording 일정 가져오기**
- [ ] 미디어 NAS: `Permanent DATA\H264_mp4 DATA` 등

## CtrlOne

- [ ] `CtrlOne.exe` → http://127.0.0.1:5177
- [ ] HyperDeck과 동일 LAN, 원격 제어·포트 9993

## ScheduleReader

- [ ] Python 3.11+ (Store 앱 실행 별칭의 python 끄기)
- [ ] `Setup-And-Run.bat` 최초 1회 → 이후 `serve.bat`
- [ ] http://127.0.0.1:17823
- [ ] `models/` 포함 zip은 용량이 큼 — 없으면 첫 OCR 시 다운로드
