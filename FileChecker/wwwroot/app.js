const $ = (selector) => document.querySelector(selector);
const defaultNasSearchPath = 'Permanent DATA\\H264_mp4 DATA';
const COLLAPSE_KEY = 'fc.collapsedDates.v1';
const PLACE_LABELS = {
  None: '-',
  Main_Large: '본당 대강당',
  Main_Medium: '본당 중강당',
  Main_Small: '본당 소강당',
  Edu_Large: '교육원 대강당',
  Edu_Medium: '교육원 중강당',
  Edu_Small: '교육원 소강당',
  Edu_Nongseon: '교육원 농선부실',
  Edu_Siseon: '교육원 시선부실',
  Edu_Go2: '교육원 고2실',
  Etc: '기타',
};

const api = async (url, options) => {
  const response = await fetch(url, options);
  if (!response.ok) throw new Error((await response.json()).message || '요청에 실패했습니다.');
  return response.status === 204 ? null : response.json();
};
const esc = (value = '') =>
  String(value).replace(/[&<>"']/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
const dateText = (date) => {
  const d = new Date(`${date}T00:00:00`);
  const week = ['일', '월', '화', '수', '목', '금', '토'][d.getDay()];
  return `${d.getMonth() + 1}.${String(d.getDate()).padStart(2, '0')} (${week})`;
};
const todayIso = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};
const addDaysIso = (iso, delta) => {
  const d = new Date(`${iso}T00:00:00`);
  d.setDate(d.getDate() + delta);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};
const placeLabel = (place) => {
  if (!place || place === 'None') return '-';
  return PLACE_LABELS[place] || place;
};
const copyText = async (text) => {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text);
    return;
  }
  const area = document.createElement('textarea');
  area.value = text;
  area.style.position = 'fixed';
  area.style.opacity = '0';
  document.body.appendChild(area);
  area.select();
  document.execCommand('copy');
  area.remove();
};

const loadCollapsed = () => {
  try {
    const raw = JSON.parse(localStorage.getItem(COLLAPSE_KEY) || '{}');
    return raw && typeof raw === 'object' ? raw : {};
  } catch {
    return {};
  }
};
const saveCollapsed = (map) => localStorage.setItem(COLLAPSE_KEY, JSON.stringify(map));

let allSchedules = [];
let descending = false;
let collapsedDates = loadCollapsed();
const knownCollapseDates = new Set(Object.keys(collapsedDates));

const ensureCollapseDefaults = (dates, today) => {
  // 저장된 접기 상태는 유지하고, 새로 나타난 날짜만 기본값(오늘 외 접기) 적용
  let changed = false;
  for (const date of dates) {
    if (knownCollapseDates.has(date)) continue;
    knownCollapseDates.add(date);
    if (date !== today) {
      collapsedDates[date] = true;
      changed = true;
    }
  }
  if (changed) saveCollapsed(collapsedDates);
};

const statusIcon = (kind, on) => {
  const label = kind === 'song' ? '특송' : 'YouTube';
  const state = on ? 'yes' : 'no';
  const mark = on
    ? '<svg viewBox="0 0 16 16" aria-hidden="true"><path fill="currentColor" d="M6.5 11.2 3.2 7.9l1.1-1.1 2.2 2.2 4.4-4.4 1.1 1.1z"/></svg>'
    : '<svg viewBox="0 0 16 16" aria-hidden="true"><path fill="currentColor" d="M4.2 4.2 11.8 11.8M11.8 4.2 4.2 11.8" stroke="currentColor" stroke-width="1.8" fill="none"/></svg>';
  const glyph =
    kind === 'song'
      ? '<svg class="flag-glyph" viewBox="0 0 16 16" aria-hidden="true"><path fill="currentColor" d="M6 3.2v7.1a2.2 2.2 0 1 1-1.4-2V5.2L13 3.6v5.5a2.2 2.2 0 1 1-1.4-2V2.4z"/></svg>'
      : '<svg class="flag-glyph" viewBox="0 0 16 16" aria-hidden="true"><path fill="currentColor" d="M3.2 3.6h9.6c.8 0 1.2.9.7 1.5L9.8 8l3.7 2.9c.5.6.1 1.5-.7 1.5H3.2z"/></svg>';
  return `<span class="status-icon ${state}" title="${label}: ${on ? '있음' : '없음'}" aria-label="${label} ${on ? '있음' : '없음'}">${glyph}<span class="status-mark">${mark}</span></span>`;
};

const renderScheduleRow = (item, today) => {
  const complete =
    item.localAudioFound &&
    item.localVideoFound &&
    item.localHighQualityFound &&
    item.nasAudioFound &&
    item.nasVideoFound &&
    item.nasHighQualityFound;
  const dateClass = item.date < today ? 'past' : item.date === today ? 'today' : 'future';
  const check = (label, found, path) =>
    `<span class="check ${found ? 'yes' : ''}">${label} ${found ? '✓' : '·'}${
      found ? `<button class="path-button" data-path="${esc(path)}">경로</button>` : ''
    }</span>`;
  return `<article class="schedule ${complete ? 'complete' : ''} ${dateClass}">
    <div class="schedule-main">
      <div class="schedule-meta">
        <span class="meta-place">${esc(placeLabel(item.place))}</span>
        <span class="meta-title">${esc(item.description || '')}</span>
        <span class="meta-instructor">${item.hasInstructor && item.instructor ? esc(item.instructor) : ''}</span>
      </div>
      <div class="base-filename"><span>기본파일명</span><code>${esc(item.baseFileName)}</code><button class="copy-button" data-action="copy" data-copy="${esc(item.baseFileName)}">복사</button></div>
    </div>
    <div class="flags">${statusIcon('song', item.specialSong)}${statusIcon('tube', item.youTube)}</div>
    <div class="checks">
      <div class="check-group"><small>로컬</small>${check('음성', item.localAudioFound, item.localAudioPath)}${check('일반', item.localVideoFound, item.localVideoPath)}${check('고화질', item.localHighQualityFound, item.localHighQualityPath)}</div>
      <div class="check-group"><small>NAS</small>${check('음성', item.nasAudioFound, item.nasAudioPath)}${check('일반', item.nasVideoFound, item.nasVideoPath)}${check('고화질', item.nasHighQualityFound, item.nasHighQualityPath)}</div>
    </div>
  </article>`;
};

const renderSchedules = () => {
  const mode = $('#period-mode').value;
  const month = $('#filter-month').value;
  const from = $('#filter-from').value;
  const to = $('#filter-to').value;
  const key = $('#sort-key').value;
  const today = todayIso();
  let schedules = allSchedules.filter((item) =>
    mode === 'month'
      ? !month || item.date.startsWith(month)
      : (!from || item.date >= from) && (!to || item.date <= to),
  );
  const status = (item, prefix) =>
    Number(item[`${prefix}AudioFound`]) +
    Number(item[`${prefix}VideoFound`]) +
    Number(item[`${prefix}HighQualityFound`]);
  schedules.sort((a, b) => {
    const left =
      key === 'date'
        ? a.date
        : key === 'description'
          ? a.description
          : key === 'place'
            ? placeLabel(a.place)
            : status(a, key === 'localStatus' ? 'local' : 'nas');
    const right =
      key === 'date'
        ? b.date
        : key === 'description'
          ? b.description
          : key === 'place'
            ? placeLabel(b.place)
            : status(b, key === 'localStatus' ? 'local' : 'nas');
    if (left === right) return a.date.localeCompare(b.date) * (descending ? -1 : 1);
    return (left > right ? 1 : -1) * (descending ? -1 : 1);
  });

  $('#summary-count').textContent = schedules.length;

  const byDate = new Map();
  for (const item of schedules) {
    if (!byDate.has(item.date)) byDate.set(item.date, []);
    byDate.get(item.date).push(item);
  }
  const dates = [...byDate.keys()].sort((a, b) => (a > b ? 1 : -1) * (descending ? -1 : 1));
  ensureCollapseDefaults(dates, today);

  $('#schedule-list').innerHTML = dates
    .map((date) => {
      const items = byDate.get(date);
      const collapsed = Boolean(collapsedDates[date]);
      return `<section class="date-group ${collapsed ? 'is-collapsed' : ''}" data-date="${esc(date)}">
        <button type="button" class="date-group-toggle" data-toggle-date="${esc(date)}" aria-expanded="${collapsed ? 'false' : 'true'}">
          <span class="chevron" aria-hidden="true"></span>
          <strong>${dateText(date)}</strong>
          <small>${items.length}건</small>
        </button>
        <div class="date-group-body">${items.map((item) => renderScheduleRow(item, today)).join('')}</div>
      </section>`;
    })
    .join('');

  $('#empty-state').classList.toggle('hidden', schedules.length > 0);
};

const showListLoading = (message) => {
  const list = $('#schedule-list');
  if (list) {
    list.innerHTML = `<div class="list-loading" role="status">${esc(message)}</div>`;
  }
  $('#empty-state')?.classList.add('hidden');
};

const refresh = async (forceScan = false) => {
  showListLoading(forceScan ? '파일을 다시 확인하는 중…' : '목록을 불러오는 중…');
  try {
    allSchedules = await api(`/api/schedules?scan=${forceScan}`);
    $('#summary-date').textContent = new Intl.DateTimeFormat('ko-KR', { year: 'numeric', month: 'long' }).format(new Date());
    renderSchedules();
  } catch (error) {
    showListLoading('목록을 불러오지 못했습니다. 새로고침을 다시 시도해 주세요.');
    throw error;
  }
};

const openModal = (type) => {
  $('#modal-content').innerHTML = type === 'settings' ? settingsForm() : scheduleForm();
  $('#modal').dataset.type = type;
  $('#modal').classList.remove('hidden');
  if (type === 'settings') loadSettings();
};

const hourOptions = () =>
  Array.from({ length: 24 }, (_, hour) => {
    const v = String(hour).padStart(2, '0');
    return `<option value="${v}">${v}시</option>`;
  }).join('');
const minuteOptions = () =>
  [0, 10, 20, 30, 40, 50]
    .map((minute) => {
      const v = String(minute).padStart(2, '0');
      return `<option value="${v}">${v}분</option>`;
    })
    .join('');
const placeOptions = () =>
  Object.entries(PLACE_LABELS)
    .map(([id, label]) => `<option value="${id}">${esc(label)}</option>`)
    .join('');

const scheduleForm = () =>
  `<h2>새 스케줄 등록</h2><p class="lead">영상 렌더링 작업의 기준 정보를 남겨주세요.</p><form id="schedule-form"><div class="form-grid"><div class="field"><label>날짜 *</label><input name="date" type="date" required></div><div class="field"><label>제목 *</label><input name="description" placeholder="예: 주일예배 2부" required></div><div class="field"><label>장소</label><select name="place">${placeOptions()}</select></div><div class="field full"><label>메모</label><textarea name="memo" placeholder="작업에 필요한 메모를 남겨주세요."></textarea></div></div><div class="optional"><div class="toggle-row"><input id="has-start" type="checkbox"><label for="has-start">시작시간 사용</label><select name="startHour" disabled><option value="">시 선택</option>${hourOptions()}</select><select name="startMinute" disabled><option value="">분 선택</option>${minuteOptions()}</select></div><div class="toggle-row"><input id="has-instructor" type="checkbox"><label for="has-instructor">강사이름 사용</label><input name="instructor" placeholder="강사 이름" disabled></div><div class="toggle-row"><input name="specialSong" type="checkbox"><label>특송 있음</label><input name="youTube" type="checkbox"><label>YouTube 송출</label></div></div><div class="actions"><button type="button" class="secondary" data-action="close">취소</button><button class="primary">등록하기</button></div></form>`;

const settingsForm = () =>
  `<h2>파일 확인 설정</h2><p class="lead">작업 파일을 찾을 기준 경로를 지정합니다. Windows는 FileCheckerFinder.exe(또는 Start-FileCheckerFinder.bat), macOS는 FileCheckerFinder 바이너리로 실행할 수 있습니다.</p><form id="settings-form"><div class="form-grid"><div class="field full"><label>로컬경로</label><div class="input-action"><input name="localPath" placeholder="D:\\Render\\Completed 또는 /Users/.../Render"><button type="button" class="secondary" data-action="select-local">폴더 선택 (Windows)</button></div><small>macOS에서는 경로를 직접 입력하세요.</small></div><div class="field full"><label>NAS IP 주소</label><div class="ip-input"><input class="ip-segment" maxlength="3" inputmode="numeric"><b>.</b><input class="ip-segment" maxlength="3" inputmode="numeric"><b>.</b><input class="ip-segment" maxlength="3" inputmode="numeric"><b>.</b><input class="ip-segment" maxlength="3" inputmode="numeric"><input name="nasIp" type="hidden"></div></div><div class="field"><label>검색 시작 경로</label><div class="input-action"><input name="nasSearchPath" placeholder="공유폴더\\video\\completed"><button type="button" class="secondary" data-action="default-nas-path">기본값</button></div></div><div class="field"><label>NAS 계정</label><input name="nasUsername"></div><div class="field"><label>NAS 비밀번호</label><input name="nasPassword" type="password"></div><div class="field full"><label>스케줄 JSON 경로 (SDM 공용)</label><input name="scheduleJsonPath" placeholder="\\\\NAS\\Temp DATA\\_data\\schedule-data.json 또는 /Volumes/.../schedule-data.json"><small>preparation에 Recording이 있는 일정만 작업 목록으로 가져옵니다.</small></div></div><div class="nas-test"><button type="button" class="secondary" data-action="nas-test">NAS 접속 테스트</button><span id="nas-test-result"></span></div><div class="data-tools"><strong>스케줄 데이터</strong><button type="button" class="secondary" data-action="sync-schedule-data">Recording 일정 가져오기</button><button type="button" class="secondary" data-action="export-schedules">전체 내보내기</button><button type="button" class="secondary" data-action="import-schedules">전체 불러오기</button><input id="schedule-import" type="file" accept="application/json,.json" hidden></div><div class="actions"><button type="button" class="secondary" data-action="close">취소</button><button class="primary">설정 저장</button></div></form>`;

const readIp = () => [...document.querySelectorAll('.ip-segment')].map((input) => input.value).join('.');
const writeIp = (value) =>
  String(value || '')
    .split('.')
    .forEach((part, index) => {
      const input = document.querySelectorAll('.ip-segment')[index];
      if (input) input.value = part;
    });
const loadSettings = async () => {
  const settings = await api('/api/settings');
  Object.entries(settings).forEach(([key, value]) => {
    const input = document.querySelector(`[name="${key}"]`);
    if (input && key !== 'nasIp') input.value = value || '';
  });
  writeIp(settings.nasIp);
};

const syncPeriodUi = () => {
  const mode = $('#period-mode').value;
  $('#month-filter').classList.toggle('hidden', mode !== 'month');
  $('#range-filter').classList.toggle('hidden', mode !== 'range');
};

const initDefaultFilters = () => {
  const today = todayIso();
  $('#period-mode').value = 'range';
  $('#filter-from').value = addDaysIso(today, -7);
  $('#filter-to').value = today;
  $('#filter-month').value = today.slice(0, 7);
  syncPeriodUi();
};

document.addEventListener('click', async (event) => {
  const toggleDate = event.target.closest?.('[data-toggle-date]')?.dataset?.toggleDate;
  if (toggleDate) {
    collapsedDates[toggleDate] = !collapsedDates[toggleDate];
    if (!collapsedDates[toggleDate]) delete collapsedDates[toggleDate];
    saveCollapsed(collapsedDates);
    const group = document.querySelector(`.date-group[data-date="${CSS.escape(toggleDate)}"]`);
    const btn = group?.querySelector('.date-group-toggle');
    const collapsed = Boolean(collapsedDates[toggleDate]);
    group?.classList.toggle('is-collapsed', collapsed);
    btn?.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
    return;
  }

  const action = event.target.dataset.action;
  if (action === 'schedule') openModal('schedule');
  if (action === 'settings') openModal('settings');
  if (action === 'refresh') {
    try {
      await refresh(true);
      toast('파일을 다시 확인했습니다.');
    } catch (error) {
      toast(error.message || '새로고침에 실패했습니다.');
    }
  }
  if (action === 'close') $('#modal').classList.add('hidden');
  if (action === 'copy') {
    try {
      await copyText(event.target.dataset.copy);
      toast('기본파일명을 복사했습니다.');
    } catch {
      toast('복사에 실패했습니다. 텍스트를 직접 선택해 복사해주세요.');
    }
  }
  if (action === 'export-schedules') window.location.href = '/api/schedules/export';
  if (action === 'import-schedules') $('#schedule-import').click();
  if (action === 'sync-schedule-data') {
    try {
      const result = await api('/api/schedules/sync-from-schedule-data', { method: 'POST' });
      await refresh(true);
      toast(result.message || `Recording 일정 ${result.count}개 가져옴`);
    } catch (error) {
      toast(error.message);
    }
  }
  if (action === 'open-location') {
    try {
      await api('/api/files/open-location', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: event.target.dataset.openPath }),
      });
    } catch (error) {
      toast(error.message);
    }
  }
  if (action === 'default-nas-path') document.querySelector('[name="nasSearchPath"]').value = defaultNasSearchPath;
  if (action === 'select-local') {
    try {
      const result = await api('/api/settings/select-local', { method: 'POST' });
      if (result?.path) document.querySelector('[name="localPath"]').value = result.path;
    } catch (error) {
      toast(error.message);
    }
  }
  if (action === 'nas-test') {
    const form = document.querySelector('#settings-form');
    const settings = Object.fromEntries(new FormData(form));
    settings.nasIp = readIp();
    const result = await api('/api/settings/nas-test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    });
    $('#nas-test-result').textContent = result.message;
    $('#nas-test-result').className = result.success ? 'success' : 'failure';
  }
  const path = event.target.dataset.path;
  if (path) openPathModal(path);
});

document.addEventListener('change', async (event) => {
  if (event.target.id === 'schedule-import' && event.target.files[0]) {
    try {
      const formData = new FormData();
      formData.append('file', event.target.files[0]);
      const result = await api('/api/schedules/import', { method: 'POST', body: formData });
      $('#modal').classList.add('hidden');
      await refresh();
      toast(`${result.count}개의 스케줄을 불러왔습니다.`);
    } catch (error) {
      toast(error.message);
    }
    event.target.value = '';
  }
});

const openPathModal = (path) => {
  $('#modal-content').innerHTML = `<h2>파일 위치</h2><p class="lead">확인된 영상 파일의 전체 경로입니다.</p><div class="path-view">${esc(path)}</div><div class="actions"><button class="secondary" data-action="open-location" data-open-path="${esc(path)}">위치열기</button><button class="primary" data-action="close">닫기</button></div>`;
  $('#modal').classList.remove('hidden');
};

['period-mode', 'filter-month', 'filter-from', 'filter-to', 'sort-key'].forEach((id) =>
  document.addEventListener('change', (event) => {
    if (event.target.id !== id) return;
    if (id === 'period-mode') syncPeriodUi();
    renderSchedules();
  }),
);

const syncSortDirectionLabel = () => {
  // 버튼은 현재 정렬이 아니라 「클릭 시 전환될」 방향을 표시
  const btn = $('#sort-direction');
  if (!btn) return;
  btn.textContent = descending ? '↑ 오름차순' : '↓ 내림차순';
  btn.title = descending ? '클릭하면 오름차순으로 바꿉니다' : '클릭하면 내림차순으로 바꿉니다';
};

$('#sort-direction').addEventListener('click', () => {
  descending = !descending;
  syncSortDirectionLabel();
  renderSchedules();
});

document.addEventListener('change', (event) => {
  if (event.target.id === 'has-start') {
    document.querySelector('[name="startHour"]').disabled = !event.target.checked;
    document.querySelector('[name="startMinute"]').disabled = !event.target.checked;
  }
  if (event.target.id === 'has-instructor') document.querySelector('[name="instructor"]').disabled = !event.target.checked;
});

document.addEventListener('submit', async (event) => {
  event.preventDefault();
  const form = event.target;
  const data = Object.fromEntries(new FormData(form));
  try {
    if (form.id === 'schedule-form') {
      const hasStartTime = form.querySelector('#has-start').checked;
      const startTime = hasStartTime && data.startHour && data.startMinute ? `${data.startHour}:${data.startMinute}` : null;
      await api('/api/schedules', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          date: data.date,
          description: data.description,
          memo: data.memo,
          place: data.place || 'None',
          hasStartTime,
          startTime,
          hasInstructor: form.querySelector('#has-instructor').checked,
          instructor: data.instructor || null,
          specialSong: form.querySelector('[name="specialSong"]').checked,
          youTube: form.querySelector('[name="youTube"]').checked,
        }),
      });
      toast('스케줄을 등록했습니다.');
    } else {
      data.nasIp = readIp();
      await api('/api/settings', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) });
      toast('설정을 저장했습니다.');
    }
    $('#modal').classList.add('hidden');
    await refresh();
  } catch (error) {
    toast(error.message);
  }
});

document.addEventListener('input', (event) => {
  if (event.target.classList.contains('ip-segment')) {
    event.target.value = event.target.value.replace(/\D/g, '').slice(0, 3);
    if (event.target.value.length === 3) event.target.nextElementSibling?.nextElementSibling?.focus();
  }
});

const toast = (message) => {
  $('#toast').textContent = message;
  $('#toast').classList.add('show');
  setTimeout(() => $('#toast').classList.remove('show'), 2400);
};

initDefaultFilters();
syncSortDirectionLabel();
refresh().catch((error) => toast(error?.message || '서버에 연결할 수 없습니다.'));
