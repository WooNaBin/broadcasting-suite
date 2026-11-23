# WorkLog NAS API (Phase 2)

Mac / 모바일 / 브라우저용 HTTP API입니다. Windows LocalBridge와 **같은 REST 계약**을 사용합니다.
.NET 10 런타임 (Linux/Mac/Windows).

## 환경 변수

| 변수 | 설명 |
|------|------|
| `DATA_ROOT` | `_work_log` 폴더 절대 경로 |
| `SCHEDULE_ROOT` | 스케줄 JSON이 있는 `_data` (기본: DATA_ROOT 부모) |
| `PORT` | 기본 `17822` |

## 로컬 실행

```powershell
$env:DATA_ROOT = "D:\nas-mount\Temp DATA\_data\_work_log"
$env:SCHEDULE_ROOT = "D:\nas-mount\Temp DATA\_data"
dotnet run --project nas-api/WorkLogNasApi.csproj
```

브라우저에서 `http://localhost:17822` → 로그인에서 **원격 API** 선택.

## Docker (Synology 등, 저장소 루트에서 빌드)

```bash
docker build -f nas-api/Dockerfile -t worklog-nas-api .
docker run -d --name worklog-api -p 17822:17822 \
  -e DATA_ROOT=/data/_work_log \
  -e SCHEDULE_ROOT=/data \
  -v "/volume1/Temp DATA/_data:/data" \
  worklog-nas-api
```

내부 LAN 전용 권장. 필요 시 리버스 프록시 인증을 추가하세요.
