# CtrlOne 카드 레이아웃 수정 가이드

장비 카드는 가로로 **A | B | C | D | E** 다섯 구역입니다.

| 구역 | 역할 | CSS 클래스 |
|------|------|------------|
| **A** | 연결 상태, 이름, IP, 모델, 이름변경/삭제 | `.card-col-a` |
| **B** | 현재 상태 (녹화중/중지/미리보기 + ON AIR) | `.card-col-b` `.mode-status` |
| **C** | 저장장치 슬롯 + 하단 슬롯 선택 | `.card-col-c` |
| **D** | 기능 버튼 (재생/녹화/…) | `.card-col-d` `.transport-btns` |
| **E** | 순서 ▲▼ | `.card-col-e` `.order-btns` |

---

## 추천 수정 방법 (2가지)

### 1) 변수 파일만 고치기 (가장 단순)

편집 파일: **`wwwroot/css/layout.css`**

열 폭·간격·상태 글자 크기 등이 CSS 변수로 모여 있습니다.  
숫자만 바꾼 뒤 **`빌드실행.bat`** 으로 다시 게시하면 exe에 반영됩니다.

예:

```css
--col-b-fr: 1fr;           /* B(상태) 열을 더 넓게 */
--mode-font-size: 1.35rem; /* 상태 글자 더 크게 */
--transport-cols: 3;       /* 기능 버튼 한 줄에 3개 */
```

### 2) 레이아웃 에디터로 미리보기

앱 실행 중: **http://127.0.0.1:5177/layout-editor.html**  
또는 데모 배너의 **레이아웃 조절** 링크

슬라이더로 조절 → **CSS 복사** → `wwwroot/css/layout.css` 의 `:root { … }` 에 붙여넣기 → 빌드

오프라인 확인: `demo-preview/index.html` (빌드 후 생성)

---

## 변수 목록 (`layout.css`)

| 변수 | 의미 |
|------|------|
| `--col-a-min` / `--col-a-fr` | A열 최소폭 / 비율 |
| `--col-b-min` / `--col-b-fr` | B열 (상태) |
| `--col-c-min` / `--col-c-fr` | C열 (저장장치) |
| `--col-d-min` / `--col-d-fr` | D열 (기능 버튼) |
| `--col-e-width` | E열 고정 폭 |
| `--card-gap-row` / `--card-gap-col` | 카드 안 세로/가로 간격 |
| `--card-padding-y` / `--card-padding-x` | 카드 안쪽 여백 |
| `--mode-min-height` | 상태 배지 최소 높이 |
| `--mode-font-size` | 상태 글자 크기 |
| `--mode-padding-y` / `--mode-padding-x` | 상태 배지 패딩 |
| `--mode-radius` | 상태 배지 모서리 |
| `--transport-cols` | 기능 버튼 한 줄 개수 |

세부 스타일(색, 슬롯 바 등)은 `wwwroot/css/app.css` 에 있습니다.  
구조(HTML)를 바꿀 때는 `wwwroot/js/app.js` / `demo.js` 의 `cardHtml()` 을 수정합니다.

---

## 반영 확인

1. `빌드실행.bat` 실행  
2. 대시보드 또는 `demo.html` 에서 카드 확인  
3. 브라우저 강력 새로고침(Ctrl+F5) — 임베드 UI는 재빌드가 필요함
