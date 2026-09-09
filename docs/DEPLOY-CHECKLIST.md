# 방송실 PC 배포 체크리스트

통합 zip: `Builded\BroadcastingApp_<version>.<build>_<yyyyMMdd>.zip`

압축 해제 예:

```
BroadcastingApp_<label>\
  Windows\          ← 포터블 앱 (CtrlOne-Windows-x64 등)
  Mac\              ← 추후
  Install-BroadcastApps.bat / .ps1
  VERSION.txt / README.txt
```

- [ ] zip 해제 후 `Install-BroadcastApps.bat`로 설치 폴더 선택 (기본 `%LOCALAPPDATA%\BroadcastingApp`) — **Windows\만** 복사
- [ ] 또는 `Windows\` 아래 각 앱 폴더를 직접 실행

## 공통

- [ ] Windows 10/11 x64 (Mac은 Bridge·CtrlOne·SDM·WL·FC 해당분)
- [ ] **권장:** `BroadcastNasBridge` (`17820`) — NAS 설정 1회 후 스케줄·일지·파일체크
- [ ] 포트: **17820(Bridge)** / 17821·17822·5187(레거시 폴백) / 17823(SR) / 5177(CtrlOne)
- [ ] 레거시 단독은 **브리지가 꺼져 있을 때만**. 브리지가 켜져 있으면 단독 실행해도 `/schedule` `/worklog` `/files`로 연결됨
- [ ] 레거시 정리 문서: [LEGACY-CLEANUP.md](LEGACY-CLEANUP.md)
- [ ] 레거시 단독 exe를 쓸 경우 SDM·WorkLog·FileChecker·탐색기 **같은 NAS 계정**

## BroadcastNasBridge (권장)

- [ ] `Windows\BroadcastNasBridge-Windows-x64\BroadcastNasBridge.exe` (또는 Start bat)
- [ ] http://127.0.0.1:17820 → NAS 설정 → 앱 카드
- [ ] 탭·시작 페이지를 모두 닫으면 약 5초 후 종료

## ScheduleDataManager (레거시 폴백)

- [ ] 가능하면 브리지 `/schedule` 사용. 단독: `BroadcastingSchedule.exe` → http://127.0.0.1:17821
- [ ] 공유 예: `Temp DATA\_data`

## WorkLog (레거시 폴백)

- [ ] 가능하면 브리지 `/worklog` 사용. 단독: `WorkLog.exe` → http://127.0.0.1:17822
- [ ] 공유 예: `Temp DATA\_data\_work_log`

## FileChecker (레거시 폴백)

- [ ] 가능하면 브리지 `/files` 사용. 단독: `FileCheckerFinder.exe` 또는 `Start-FileCheckerFinder.bat` **하나만**
- [ ] 설정에 스케줄 JSON 경로 지정 후 **Recording 일정 가져오기**
- [ ] 미디어 NAS: `Permanent DATA\H264_mp4 DATA` 등

## CtrlOne

- [ ] `Windows\CtrlOne-Windows-x64\CtrlOne.exe` → http://127.0.0.1:5177
- [ ] HyperDeck과 동일 LAN, 원격 제어·포트 9993

## ScheduleReader

- [ ] Python 3.11+ (Store 앱 실행 별칭의 python 끄기)
- [ ] `Windows\ScheduleReader-portable\Setup-And-Run.bat` 최초 1회 → 이후 `serve.bat`
- [ ] http://127.0.0.1:17823
- [ ] 월간 일정 `.xlsx` 불러오기 → 추출 → `schedule-data.json` → SDM 가져오기
