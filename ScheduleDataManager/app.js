const STORAGE_KEY = "schedule-data-manager";
const NAS_PREF_KEY = "schedule-data-manager-nas";
const NAS_DIRTY_KEY = "schedule-data-manager-nas-dirty";
const NAS_KEY_MATERIAL = "schedule-data-manager-nas-key";
const DAY_NAMES = ["일", "월", "화", "수", "목", "금", "토"];
const PLACE_LABELS = {
  None: "-",
  Main_Large: "본당 대강당",
  Main_Medium: "본당 중강당",
  Main_Small: "본당 소강당",
  Edu_Large: "교육원 대강당",
  Edu_Medium: "교육원 중강당",
  Edu_Small: "교육원 소강당",
  Edu_Nongseon: "교육원 농선부실",
  Edu_Siseon: "교육원 시선부실",
  Edu_Go2: "교육원 고2실",
  Etc: "기타",
};
const PREP_LABELS = {
  Recording: "녹화",
  Praise: "특송준비",
  Streaming: "스트리밍준비",
  OtherDisplay: "외부입력 준비",
};
const PRESET_STORAGE_KEY = "schedule-data-manager-presets";
const PRESETS_FILE = "_schedule-presets.json";
const PRESET_LOCAL_MIGRATED_KEY = "schedule-data-manager-presets-migrated";
/** @type {{ id: string, name: string, builtin?: boolean, fields: string[], values: object }[]} */
let userPresetsCache = null;
let presetsRevision = "";
let presetsDirty = false;
/** 이번 세션에서 삭제한 프리셋 id — 409 병합·리프레시 시 부활 방지 */
const presetDeletedIds = new Set();
let presetManageMode = false;
/** @type {string | null} */
let editingPresetId = null;
let presetDragId = null;
const SPEAKER_ROSTER_STORAGE_KEY = "schedule-data-manager-speaker-roster";
const SPEAKER_ROSTER_FILE = "_speaker-roster.json";
const SPEAKER_ROSTER_LOCAL_MIGRATED_KEY = "schedule-data-manager-speaker-roster-migrated";
const SPEAKER_ROSTER_LABELS = { gwangju: "명단" };
/** @type {{ gwangju: string[] } | null} */
let speakerRosterCache = null;
let speakerRosterRevision = "";
let speakerRosterDirty = false;
/** 이번 세션에서 삭제한 명단 이름 — 409 병합 시 부활 방지 */
const speakerRosterDeletedNames = new Set();
const COLOR_RULES_STORAGE_KEY = "schedule-data-manager-color-rules";
const COLOR_RULES_FILE = "_schedule-colors.json";
const FONT_RULES_STORAGE_KEY = "schedule-data-manager-font-rules";
const FONT_RULES_FILE = "_schedule-fonts.json";
const DEFAULT_TITLE_FONT_COLOR = "#18201f";
const DEFAULT_SPEAKER_FONT_COLOR = "#71807b";
const CALENDAR_FILTERS_STORAGE_KEY = "schedule-data-manager-calendar-filters";
const IMPORT_EXCLUDE_STORAGE_KEY = "schedule-data-manager-import-excludes";
const MISC_SETTINGS_STORAGE_KEY = "schedule-data-manager-misc";
const HOLIDAY_BG_STORAGE_KEY = "schedule-data-manager-holiday-bg";
const PERIOD_COLOR_MODE_CUSTOM = "custom";
const PERIOD_COLOR_MODE_TITLE = "title";
const PERIOD_COLOR_MODE_SPEAKER = "speaker";
const DEFAULT_PERIOD_COLOR = DEFAULT_SPEAKER_FONT_COLOR;
const DEFAULT_HOLIDAY_BG_COLOR = "#f8ebe8";
const COLOR_MODE_NONE = "none";
const COLOR_MODE_AUTO = "auto";
const COLOR_MODE_CUSTOM = "custom";
const DEFAULT_COLOR_MODE = COLOR_MODE_AUTO;
/** @type {{ id: string, pattern: string, color: string }[] | null} */
let colorRulesCache = null;
let colorRulesRevision = "";
let colorRulesDirty = false;
/** 자동 모드·조건 미매칭 시 배경. null이면 없음(투명) */
let colorAutoDefault = null;
let colorAutoDefaultLoaded = false;
const COLOR_AUTO_DEFAULT_STORAGE_KEY = "schedule-data-manager-color-auto-default";
/** @type {{ id: string, pattern: string, titleColor: string, speakerColor: string }[] | null} */
let fontRulesCache = null;
let fontRulesRevision = "";
let fontRulesDirty = false;
let fontDefaults = {
  titleColor: DEFAULT_TITLE_FONT_COLOR,
  speakerColor: DEFAULT_SPEAKER_FONT_COLOR,
};
let fontDefaultsLoaded = false;
/** @type {{ filters: { id: string, pattern: string }[], checked: Record<string, boolean>, otherChecked: boolean } | null} */
let calendarFiltersState = null;
/** @type {{ id: string, pattern: string }[] | null} */
let importExcludeCache = null;
/** @type {{ showPeriodDates: boolean, periodColorMode: string, periodColor: string } | null} */
let miscSettingsCache = null;
/** @type {{ enabled: boolean, color: string } | null} */
let holidayBgCache = null;
const LIST_RANGE_STORAGE_KEY = "schedule-data-manager-list-range";
const SCHEDULE_COLORS = [
  { id: "orange", label: "오렌지", accent: "#e87542", soft: "#fff1e6" },
  { id: "green", label: "그린", accent: "#4e8d6b", soft: "#e8f4ec" },
  { id: "yellow", label: "옐로", accent: "#d4a017", soft: "#fff6d9" },
  { id: "teal", label: "틸", accent: "#3d8b8b", soft: "#e4f3f3" },
  { id: "blue", label: "블루", accent: "#4a7fb5", soft: "#e7f0f8" },
  { id: "coral", label: "코랄", accent: "#d96b6b", soft: "#fdeeee" },
  { id: "sage", label: "세이지", accent: "#6f8f72", soft: "#eef4ee" },
  { id: "sand", label: "샌드", accent: "#b08958", soft: "#f6efe4" },
  { id: "slate", label: "슬레이트", accent: "#6b7c85", soft: "#eef1f3" },
];
const DEFAULT_SCHEDULE_COLOR = "orange";
const PRESET_FIELD_LABELS = {
  startTime: "시간",
  scheduleTitle: "제목",
  place: "장소",
  sermonSpeaker: "강사명",
  accessoryInfo: "부가 정보",
  preaparationNote: "설명",
  preparation: "사전준비",
  color: "색상",
};
const API_BASE = "";
function isBridgeHosted() {
  try {
    const path = location.pathname || "";
    return path === "/schedule" || path.startsWith("/schedule/") || API_BASE === "/schedule";
  } catch {
    return API_BASE === "/schedule";
  }
}
const BRIDGE_HEALTH_MS = 5000;
/** 로그인·NAS 연결 화면의 공유 폴더명 기본값 (placeholder 예시와 동일) */
const DEFAULT_SHARE_PATH = "Temp DATA\\_data";
let schedules = loadSchedules();
let activePresetId = null;
let editingId = null;
let inputMode = "single";
let nasHandle = null;
let nasConfig = null;
let sharedFileHandle = null;
let bridgeAlive = true;
let bridgeConnected = false;
let linkedFileName = "";
let linkedFileRevision = "";
let schedulesBase = [];
let lastSaveAt = null;
let lastSaveOk = null;
let connectionRoot = "";
let bridgeWatchTimer = null;
let healthCheckInFlight = false;
let appEntered = false;
let cryptoKeyPromise = null;
let autoSaveTimer = null;
let autoSaveInFlight = false;
let autoSaveQueued = false;
let pendingOfficialDocument = "";
let pendingScheduleId = "";
const AUTO_SAVE_DELAY_MS = 800;
const OFFICIAL_DOC_FOLDER = "_official_documents";

const $ = (id) => document.getElementById(id);

function bytesToBase64(bytes) {
  let binary = "";
  bytes.forEach((b) => {
    binary += String.fromCharCode(b);
  });
  return btoa(binary);
}

function base64ToBytes(value) {
  return Uint8Array.from(atob(value), (c) => c.charCodeAt(0));
}

async function getDeviceCryptoKey() {
  if (!cryptoKeyPromise) {
    cryptoKeyPromise = (async () => {
      const saved = localStorage.getItem(NAS_KEY_MATERIAL);
      if (saved) {
        return crypto.subtle.importKey(
          "raw",
          base64ToBytes(saved),
          { name: "AES-GCM" },
          false,
          ["encrypt", "decrypt"],
        );
      }
      const key = await crypto.subtle.generateKey(
        { name: "AES-GCM", length: 256 },
        true,
        ["encrypt", "decrypt"],
      );
      const raw = new Uint8Array(await crypto.subtle.exportKey("raw", key));
      localStorage.setItem(NAS_KEY_MATERIAL, bytesToBase64(raw));
      return crypto.subtle.importKey(
        "raw",
        raw,
        { name: "AES-GCM" },
        false,
        ["encrypt", "decrypt"],
      );
    })();
  }
  return cryptoKeyPromise;
}

async function encryptSecret(plainText) {
  const key = await getDeviceCryptoKey();
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const encoded = new TextEncoder().encode(plainText);
  const cipher = new Uint8Array(
    await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, encoded),
  );
  return {
    v: 1,
    iv: bytesToBase64(iv),
    data: bytesToBase64(cipher),
  };
}

async function decryptSecret(payload) {
  if (!payload?.iv || !payload?.data) return "";
  const key = await getDeviceCryptoKey();
  const plain = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: base64ToBytes(payload.iv) },
    key,
    base64ToBytes(payload.data),
  );
  return new TextDecoder().decode(plain);
}

function loadNasPrefs() {
  try {
    const data = JSON.parse(localStorage.getItem(NAS_PREF_KEY) || "{}");
    return {
      hosts: Array.isArray(data.hosts) ? data.hosts.filter(Boolean) : [],
      last: data.last && typeof data.last === "object" ? data.last : null,
    };
  } catch {
    return { hosts: [], last: null };
  }
}

function saveNasPrefs(prefs) {
  localStorage.setItem(
    NAS_PREF_KEY,
    JSON.stringify({
      hosts: (prefs.hosts || []).slice(0, 12),
      last: prefs.last || null,
    }),
  );
}

function rememberHost(host) {
  const prefs = loadNasPrefs();
  const next = [host, ...prefs.hosts.filter((item) => item !== host)].slice(
    0,
    12,
  );
  prefs.hosts = next;
  saveNasPrefs(prefs);
  renderHostOptions();
}

function renderHostOptions() {
  const list = $("loginHostOptions");
  if (!list) return;
  const prefs = loadNasPrefs();
  list.innerHTML = prefs.hosts
    .map((host) => `<option value="${escapeHtml(host)}"></option>`)
    .join("");
}

async function persistCredentials({
  host,
  share,
  username,
  password,
  remember,
  autoEnter,
  file,
}) {
  rememberHost(host);
  const latest = loadNasPrefs();
  const previousFile = latest.last?.file || "";
  if (remember) {
    latest.last = {
      host,
      share,
      username,
      remember: true,
      autoEnter: Boolean(autoEnter),
      passwordEnc: await encryptSecret(password),
      file: file || previousFile || "",
    };
  } else {
    latest.last = {
      host,
      share,
      username,
      remember: false,
      autoEnter: false,
      passwordEnc: null,
      file: file || previousFile || "",
    };
  }
  saveNasPrefs(latest);
}

function rememberPreferredFile(fileName) {
  if (!fileName) return;
  const prefs = loadNasPrefs();
  if (!prefs.last) {
    prefs.last = { file: fileName };
  } else {
    prefs.last.file = fileName;
  }
  saveNasPrefs(prefs);
}

async function persistActiveScheduleFile(fileName) {
  if (!fileName || !bridgeAlive) return;
  try {
    await fetch(`${API_BASE}/api/active-schedule-file`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: fileName }),
    });
  } catch {
    // 브리지 구버전·오프라인은 무시 (로컬 기억은 유지)
  }
}

const PREFERRED_JSON_NAMES = [
  "schedule-data.json",
  "scheduledata.json",
  "schedule.json",
];

function pickPreferredJsonFile(files) {
  if (!Array.isArray(files) || !files.length) return "";
  const prefs = loadNasPrefs();
  const remembered = prefs.last?.file || linkedFileName || "";
  if (remembered && files.includes(remembered)) return remembered;
  const lowerMap = files.map((name) => ({
    name,
    key: name.toLowerCase(),
  }));
  for (const preferred of PREFERRED_JSON_NAMES) {
    const hit = lowerMap.find((item) => item.key === preferred);
    if (hit) return hit.name;
  }
  const soft = lowerMap.find((item) => item.key.includes("schedule"));
  if (soft) return soft.name;
  return [...files].sort((a, b) => a.localeCompare(b, "ko"))[0];
}

function applyDefaultSharePath({ force = false } = {}) {
  const loginShare = $("loginShare");
  const nasShare = $("nasShare");
  if (loginShare && (force || !loginShare.value.trim())) {
    loginShare.value = DEFAULT_SHARE_PATH;
  }
  if (nasShare && (force || !nasShare.value.trim())) {
    nasShare.value = DEFAULT_SHARE_PATH;
  }
}

async function fillLoginFromPrefs() {
  const prefs = loadNasPrefs();
  renderHostOptions();
  const last = prefs.last;
  const autoEl = $("loginAutoEnter");
  if (last) {
    $("loginHost").value = last.host || "";
    $("loginShare").value = last.share?.trim()
      ? last.share
      : DEFAULT_SHARE_PATH;
    $("loginUser").value = last.username || "";
    $("loginRemember").checked = Boolean(last.remember && last.passwordEnc);
    if (autoEl) {
      autoEl.disabled = !$("loginRemember").checked;
      autoEl.checked = Boolean(last.remember && last.passwordEnc && last.autoEnter);
    }
    $("nasHost").value = last.host || "";
    $("nasShare").value = last.share?.trim()
      ? last.share
      : DEFAULT_SHARE_PATH;
    $("nasUser").value = last.username || "";
    $("nasRemember").checked = Boolean(last.remember && last.passwordEnc);
    if (last.remember && last.passwordEnc) {
      try {
        const password = await decryptSecret(last.passwordEnc);
        $("loginPassword").value = password;
        $("nasPassword").value = password;
      } catch {
        $("loginPassword").value = "";
        $("nasPassword").value = "";
        $("loginRemember").checked = false;
        $("nasRemember").checked = false;
        if (autoEl) {
          autoEl.checked = false;
          autoEl.disabled = true;
        }
      }
    }
  } else {
    applyDefaultSharePath({ force: true });
    if (autoEl) {
      autoEl.checked = false;
      autoEl.disabled = true;
    }
  }
  applyLoginHostModeDefaults();
  syncLanDeviceSelection($("loginHost").value.trim());
}

let loginHostMode = "scan";
let lanDevicesCache = [];
let lanScanInFlight = false;
let lanScanCooldownUntil = 0;
let lanScanCooldownTimer = null;
const LAN_SCAN_COOLDOWN_MS = 2500;

function getSavedLoginHost() {
  return (loadNasPrefs().last?.host || "").trim();
}

function applyLoginHostModeDefaults() {
  const savedHost = getSavedLoginHost();
  const savedTab = $("hostModeSaved");
  const prefs = loadNasPrefs();
  if (savedHost && savedTab) {
    savedTab.classList.remove("hidden");
    if ($("loginSavedHostLabel")) $("loginSavedHostLabel").textContent = savedHost;
    if ($("loginSavedHostMeta")) {
      const share = prefs.last?.share || "";
      $("loginSavedHostMeta").textContent = share
        ? `공유 폴더: ${share} · 수정할 수 없습니다`
        : "마지막으로 저장한 NAS 주소입니다. 수정할 수 없습니다.";
    }
    setLoginHostMode("saved", { skipAutoScan: true });
  } else {
    if (savedTab) savedTab.classList.add("hidden");
    setLoginHostMode("scan", { skipAutoScan: false });
  }
}

function setLoginHostMode(mode, { skipAutoScan = false } = {}) {
  const savedHost = getSavedLoginHost();
  if (mode === "saved" && !savedHost) mode = "scan";
  if (mode !== "saved" && mode !== "scan" && mode !== "manual") mode = "scan";
  loginHostMode = mode;

  document.querySelectorAll(".host-mode").forEach((button) => {
    const active = button.dataset.hostMode === loginHostMode;
    button.classList.toggle("active", active);
    button.setAttribute("aria-selected", active ? "true" : "false");
  });
  $("hostSavedPanel")?.classList.toggle("hidden", loginHostMode !== "saved");
  $("hostManualPanel")?.classList.toggle("hidden", loginHostMode !== "manual");
  $("hostScanPanel")?.classList.toggle("hidden", loginHostMode !== "scan");
  if ($("loginHost")) $("loginHost").required = loginHostMode === "manual";

  if (loginHostMode === "saved") {
    $("loginHost").value = savedHost;
    if ($("nasHost")) $("nasHost").value = savedHost;
  }

  if (
    loginHostMode === "scan" &&
    !skipAutoScan &&
    !lanDevicesCache.length &&
    !lanScanInFlight
  ) {
    void scanLanDevices();
  }
}

function syncLanDeviceSelection(selectedIp) {
  document.querySelectorAll(".lan-device").forEach((button) => {
    button.classList.toggle("active", button.dataset.ip === selectedIp);
  });
}

function isSynologyLanDevice(device) {
  if (!device) return false;
  if (device.isSynology) return true;
  const text = [
    device.vendorHint,
    device.displayName,
    device.hostname,
    device.netbiosName,
  ]
    .filter(Boolean)
    .join(" ");
  return /synology|시놀로지|diskstation/i.test(text);
}

function sortLanDevices(devices) {
  return [...devices].sort((a, b) => {
    const synA = isSynologyLanDevice(a) ? 1 : 0;
    const synB = isSynologyLanDevice(b) ? 1 : 0;
    if (synB !== synA) return synB - synA;
    const nasA = a.likelyNas ? 1 : 0;
    const nasB = b.likelyNas ? 1 : 0;
    if (nasB !== nasA) return nasB - nasA;
    const smbA = a.smbOpen ? 1 : 0;
    const smbB = b.smbOpen ? 1 : 0;
    if (smbB !== smbA) return smbB - smbA;
    return String(a.ip || "").localeCompare(String(b.ip || ""), undefined, {
      numeric: true,
    });
  });
}

function renderLanDeviceList(devices) {
  const list = $("lanDeviceList");
  if (!list) return;
  if (!devices.length) {
    list.innerHTML =
      '<div class="lan-device-empty">같은 네트워크에서 장치를 찾지 못했습니다. 직접 입력을 이용하거나 다시 검색하세요.</div>';
    return;
  }
  const selected = $("loginHost").value.trim();
  const ordered = sortLanDevices(devices);
  list.innerHTML = ordered
    .map((device) => {
      const ip = device.ip || "";
      const synology = isSynologyLanDevice(device);
      const displayName =
        device.displayName ||
        device.netbiosName ||
        device.hostname ||
        (synology ? "시놀로지 NAS" : null) ||
        device.vendorHint ||
        `장치 ${ip}`;
      const meta = [
        ip,
        device.hostname && device.hostname !== displayName
          ? device.hostname
          : null,
        device.vendorHint &&
        !String(displayName).includes(device.vendorHint) &&
        !(synology && /시놀로지|synology/i.test(device.vendorHint))
          ? device.vendorHint
          : null,
        device.smbOpen ? "SMB" : null,
        device.mac ? `MAC ${device.mac}` : null,
      ]
        .filter(Boolean)
        .join(" · ");
      const badge = synology
        ? '<span class="lan-device-badge lan-device-badge-synology">시놀로지 NAS</span>'
        : device.likelyNas
          ? '<span class="lan-device-badge">NAS 후보</span>'
          : "";
      return `<button type="button" class="lan-device${synology ? " lan-device-synology" : ""}${selected === ip ? " active" : ""}" data-ip="${escapeHtml(ip)}" role="option" aria-selected="${selected === ip ? "true" : "false"}">
        <span class="lan-device-name">${escapeHtml(displayName)}</span>
        ${badge}
        <span class="lan-device-meta">${escapeHtml(meta)}</span>
      </button>`;
    })
    .join("");
}

function setLanScanButtonBusy(busy, message) {
  const button = $("lanScanButton");
  const state = $("lanScanState");
  if (button) {
    button.disabled = Boolean(busy) || Date.now() < lanScanCooldownUntil;
    if (busy) button.setAttribute("aria-busy", "true");
    else button.removeAttribute("aria-busy");
  }
  if (state && message) state.textContent = message;
}

async function scanLanDevices() {
  const state = $("lanScanState");
  const button = $("lanScanButton");
  const now = Date.now();

  if (lanScanInFlight) {
    if (state) state.textContent = "이미 검색 중입니다…";
    return;
  }
  if (now < lanScanCooldownUntil) {
    const waitSec = Math.max(1, Math.ceil((lanScanCooldownUntil - now) / 1000));
    if (state) state.textContent = `${waitSec}초 후 다시 검색할 수 있습니다`;
    return;
  }

  lanScanInFlight = true;
  setLanScanButtonBusy(true, "네트워크 검색 중… (수 초 소요)");

  try {
    if (!bridgeAlive) {
      const alive = await checkBridgeHealth({ quiet: true });
      if (!alive) {
        setLanScanButtonBusy(false, bridgeDownMessage());
        showToast(bridgeDownMessage());
        return;
      }
    }
    const response = await fetch(`${API_BASE}/api/lan-devices`, {
      cache: "no-store",
    });
    if (!response.ok) throw new Error(await readErrorMessage(response));
    const devices = await response.json();
    lanDevicesCache = Array.isArray(devices) ? sortLanDevices(devices) : [];
    renderLanDeviceList(lanDevicesCache);
    const synologyCount = lanDevicesCache.filter((item) =>
      isSynologyLanDevice(item),
    ).length;
    const nasCount = lanDevicesCache.filter(
      (item) => item.likelyNas && !isSynologyLanDevice(item),
    ).length;
    const parts = [`${lanDevicesCache.length}대 발견`];
    if (synologyCount) parts.push(`시놀로지 ${synologyCount}`);
    if (nasCount) parts.push(`NAS 후보 ${nasCount}`);
    const doneMessage = lanDevicesCache.length ? parts.join(" · ") : "장치 없음";
    if (state) state.textContent = doneMessage;
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    lanDevicesCache = [];
    renderLanDeviceList([]);
    if (state) {
      state.textContent = isBridgeDownError(error)
        ? bridgeDownMessage()
        : `검색 실패: ${error.message}`;
    }
    showToast(`네트워크 검색 실패: ${error.message}`);
  } finally {
    lanScanInFlight = false;
    lanScanCooldownUntil = Date.now() + LAN_SCAN_COOLDOWN_MS;
    if (button) button.disabled = true;
    if (lanScanCooldownTimer) clearTimeout(lanScanCooldownTimer);
    lanScanCooldownTimer = setTimeout(() => {
      if (!lanScanInFlight && button) {
        button.disabled = false;
        button.removeAttribute("aria-busy");
      }
      const current = $("lanScanState")?.textContent || "";
      if (
        current.includes("검색 중") ||
        current.includes("다시 검색할 수 있습니다")
      ) {
        /* keep result message if already set */
      }
    }, LAN_SCAN_COOLDOWN_MS);
  }
}

function setLoginState(message, state = "idle") {
  const el = $("loginState");
  if (!el) return;
  el.textContent = message;
  el.dataset.state = state;
}

function hideBootScreens() {
  $("connectCheckingScreen")?.classList.add("hidden");
  $("connectFailedScreen")?.classList.add("hidden");
  $("loginScreen")?.classList.add("hidden");
}

function setConnectStep(step, state) {
  const li = document.querySelector(`#connectCheckingSteps [data-step="${step}"]`);
  if (!li) return;
  li.classList.remove("is-pending", "is-active", "is-done", "is-fail");
  li.classList.add(`is-${state}`);
}

function resetConnectSteps() {
  document.querySelectorAll("#connectCheckingSteps [data-step]").forEach((li) => {
    li.classList.remove("is-active", "is-done", "is-fail");
    li.classList.add("is-pending");
  });
}

function showConnectChecking(message, detail = "") {
  hideBootScreens();
  $("appShell")?.classList.add("hidden");
  $("connectCheckingScreen")?.classList.remove("hidden");
  if ($("connectCheckingMessage") && message) {
    $("connectCheckingMessage").textContent = message;
  }
  if ($("connectCheckingDetail")) $("connectCheckingDetail").textContent = detail || "";
}

function showConnectFailed({ message, detail = "" } = {}) {
  appEntered = false;
  hideBootScreens();
  $("appShell")?.classList.add("hidden");
  $("connectFailedScreen")?.classList.remove("hidden");
  if ($("connectFailedMessage")) {
    $("connectFailedMessage").textContent =
      message || "연결을 확인하지 못했습니다.";
  }
  if ($("connectFailedDetail")) $("connectFailedDetail").textContent = detail || "";
  $("connectFailedSetupLink")?.classList.toggle("hidden", !isBridgeHosted());
  $("connectFailedLogin")?.classList.toggle("hidden", isBridgeHosted());
}

function showLoginScreen() {
  appEntered = false;
  hideBootScreens();
  $("appShell")?.classList.add("hidden");
  $("loginScreen").classList.remove("hidden");
  void fillLoginFromPrefs().then(() => {
    void tryAutoEnterFromPrefs();
  });
}

async function tryAutoEnterFromPrefs() {
  if (appEntered) return;
  const prefs = loadNasPrefs();
  const last = prefs.last;
  if (!last?.remember || !last.autoEnter || !last.passwordEnc) return;
  if (!$("loginHost").value.trim() || !$("loginPassword").value) return;
  setLoginState("저장된 계정으로 바로 연결 중...", "pending");
  await connectAndEnterBoard({ preventDefault() {} });
}

$("loginRemember")?.addEventListener("change", () => {
  const autoEl = $("loginAutoEnter");
  if (!autoEl) return;
  autoEl.disabled = !$("loginRemember").checked;
  if (!$("loginRemember").checked) autoEl.checked = false;
});

function enterAppShell() {
  appEntered = true;
  hideBootScreens();
  $("appShell").classList.remove("hidden");
  applyListRangeToInputs();
  setViewMode(viewMode);
  void refreshSharedNotice({ quiet: true });
  startNoticePolling();
}

function syncLoginToDialogFields() {
  $("nasHost").value = $("loginHost").value.trim();
  $("nasShare").value = $("loginShare").value.trim();
  $("nasUser").value = $("loginUser").value.trim();
  $("nasPassword").value = $("loginPassword").value;
  $("nasRemember").checked = $("loginRemember").checked;
  if ($("loginFile").value) {
    // keep dialog file list in sync after connect
  }
}

function formatClock(date = new Date()) {
  return date.toLocaleTimeString("ko-KR", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function isBridgeDownError(error) {
  if (!error) return false;
  if (error.name === "AbortError") return true;
  if (error instanceof TypeError) return true;
  return /Failed to fetch|NetworkError|Load failed|fetch|network/i.test(
    String(error.message || error),
  );
}

function bridgeDownMessage() {
  return "BroadcastingSchedule.exe가 실행 중이 아닙니다. 프로그램을 다시 실행한 뒤 재시도하세요.";
}

function setNasDialogState(message, state = "idle") {
  const el = $("nasConnectionState");
  if (!el) return;
  el.textContent = message;
  el.dataset.state = state;
}

function setBridgeOfflineBanner(visible) {
  const banner = $("bridgeOfflineBanner");
  if (banner) banner.hidden = !visible;
  document.body.classList.toggle("bridge-offline", visible);
}

function updateConnectionUi({
  state,
  label,
  detail,
  pathText,
  syncText,
  syncState,
} = {}) {
  const status = $("connectionStatus");
  if (state) status.dataset.state = state;
  if (label) $("connectionLabel").textContent = label;
  if (detail) $("connectionDetail").textContent = detail;
  if (pathText !== undefined) {
    const pathEl = $("nasPath");
    const full = pathText || "아직 연결된 파일이 없습니다.";
    pathEl.textContent = full;
    pathEl.title = full;
    pathEl.dataset.fullPath = full;
  }
  if (syncText) {
    $("syncStatus").textContent = syncText;
    $("syncStatus").dataset.state = syncState || state || "local";
  }
  const canUseNas = bridgeAlive && bridgeConnected;
  const canSaveSide = canUseNas && Boolean(linkedFileName);
  const canSaveDialog =
    canUseNas && Boolean(($("nasFile") && $("nasFile").value) || linkedFileName);
  $("saveNasButton").disabled = !canSaveSide;
  $("backupNasButton").disabled = !canSaveSide;
  $("restoreNasButton").disabled = !canSaveSide;
  $("nasSaveButton").disabled = !canSaveDialog;
  $("nasLoadButton").disabled = !(canUseNas && $("nasFile")?.value);
  $("nasTestButton").disabled = !bridgeAlive;
  updateSaveStatusUi();
}

function updateSaveStatusUi(message, state) {
  const el = $("saveStatus");
  if (!el) return;
  if (message) {
    el.textContent = message;
    el.dataset.state = state || "none";
    return;
  }
  if (!bridgeAlive) {
    el.textContent = linkedFileName
      ? `서버 중지됨 · 이전 파일 ${linkedFileName} (로컬 편집 가능)`
      : "서버 중지됨 · 로컬 편집만 가능";
    el.dataset.state = "error";
    return;
  }
  if (!linkedFileName) {
    el.textContent = "연결 후 저장할 수 있습니다.";
    el.dataset.state = "none";
    return;
  }
  if (lastSaveOk === true && lastSaveAt) {
    el.textContent = `${formatClock(lastSaveAt)}에 저장 완료`;
    el.dataset.state = "ok";
    return;
  }
  if (lastSaveOk === false && lastSaveAt) {
    el.textContent = `${formatClock(lastSaveAt)} 저장 실패`;
    el.dataset.state = "error";
    return;
  }
  if (lastSaveAt) {
    el.textContent = "로컬 변경 있음 · NAS에 저장하세요.";
    el.dataset.state = "pending";
    return;
  }
  el.textContent = "아직 NAS에 저장하지 않았습니다.";
  el.dataset.state = "pending";
}

function markBridgeOffline() {
  const wasAlive = bridgeAlive;
  bridgeAlive = false;
  bridgeConnected = false;
  setBridgeOfflineBanner(true);
  updateConnectionUi({
    state: "error",
    label: "로컬 서버 중지됨",
    detail:
      "BroadcastingSchedule.exe를 다시 실행하세요. 일정 보기·편집은 브라우저 저장소로 계속할 수 있습니다.",
    pathText: linkedFileName || "아직 연결된 파일이 없습니다.",
    syncText: "서버 꺼짐 · 로컬만 사용",
    syncState: "error",
  });
  setNasDialogState(bridgeDownMessage(), "error");
  if (wasAlive) {
    showToast("Broadcasting Schedule가 종료되었습니다. 로컬 편집은 계속 가능합니다.");
  }
}

function markBridgeOnline(status = null) {
  const wasOffline = !bridgeAlive;
  const wasSmbConnected = bridgeConnected;
  bridgeAlive = true;
  setBridgeOfflineBanner(false);
  $("nasTestButton").disabled = false;

  if (status) {
    bridgeConnected = Boolean(status.connected);
    if (status.connected) connectionRoot = status.root || connectionRoot;
  }

  if (wasOffline) {
    updateConnectionUi({
      state: linkedFileName || bridgeConnected ? "pending" : "idle",
      label: bridgeConnected
        ? "서버 복구됨"
        : linkedFileName
          ? "서버 복구됨 · NAS 재연결 필요"
          : "서버 실행 중",
      detail: bridgeConnected
        ? connectionRoot
          ? `${connectionRoot} 세션이 유지되어 있습니다.`
          : "Broadcasting Schedule와 SMB 세션이 준비되었습니다."
        : linkedFileName
          ? `이전 파일 "${linkedFileName}"을 쓰려면 NAS 연결 설정에서 SMB를 다시 확인하세요.`
          : "Broadcasting Schedule가 실행 중입니다. NAS를 연결할 수 있습니다.",
      pathText: linkedFileName || "아직 연결된 파일이 없습니다.",
      syncText: bridgeConnected
        ? linkedFileName
          ? `서버 복구됨 · ${linkedFileName}`
          : "서버 복구됨 · SMB 연결됨"
        : "서버 복구됨 · NAS 재연결 필요",
      syncState: bridgeConnected ? "connected" : "pending",
    });
    setNasDialogState(
      bridgeConnected
        ? "서버가 복구되었습니다."
        : "서버가 복구되었습니다. SMB 연결을 다시 확인하세요.",
      bridgeConnected ? "ok" : "pending",
    );
    showToast("Broadcasting Schedule가 다시 실행되었습니다.");
  } else if (wasSmbConnected && status && !status.connected) {
    updateConnectionUi({
      state: "pending",
      label: "서버는 켜짐 · SMB 끊김",
      detail:
        "Broadcasting Schedule는 실행 중이지만 NAS SMB 세션이 없습니다. 다시 연결하세요.",
      pathText: linkedFileName || "아직 연결된 파일이 없습니다.",
      syncText: "SMB 재연결 필요",
      syncState: "pending",
    });
  }
}

async function checkBridgeHealth({ quiet = true } = {}) {
  if (healthCheckInFlight) return bridgeAlive;
  healthCheckInFlight = true;
  const retryButton = $("bridgeRetryButton");
  if (retryButton && !quiet) {
    retryButton.disabled = true;
    retryButton.textContent = "확인 중...";
  }
  try {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 2500);
    const response = await fetch(`${API_BASE}/api/status`, {
      cache: "no-store",
      signal: controller.signal,
    });
    clearTimeout(timeout);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const status = await response.json();
    markBridgeOnline(status);
    if (!quiet) showToast("Broadcasting Schedule 서버에 연결되었습니다.");
    return true;
  } catch (error) {
    markBridgeOffline();
    if (!quiet) showToast(bridgeDownMessage());
    return false;
  } finally {
    healthCheckInFlight = false;
    if (retryButton) {
      retryButton.disabled = false;
      retryButton.textContent = "서버 다시 확인";
    }
  }
}

function startBridgeWatch() {
  if (bridgeWatchTimer) clearInterval(bridgeWatchTimer);
  checkBridgeHealth({ quiet: true });
  bridgeWatchTimer = setInterval(
    () => checkBridgeHealth({ quiet: true }),
    BRIDGE_HEALTH_MS,
  );
}

async function readErrorMessage(response) {
  try {
    const data = await response.json();
    return data.message || `HTTP ${response.status}`;
  } catch {
    return `HTTP ${response.status}`;
  }
}

async function ensureBridgeConnection(fileName) {
  let statusResponse;
  try {
    statusResponse = await fetch(`${API_BASE}/api/status`, { cache: "no-store" });
  } catch (error) {
    markBridgeOffline();
    throw new Error(bridgeDownMessage());
  }
  if (!statusResponse.ok)
    throw new Error(await readErrorMessage(statusResponse));
  bridgeAlive = true;
  setBridgeOfflineBanner(false);
  const status = await statusResponse.json();
  if (!status.connected) {
    bridgeConnected = false;
    updateConnectionUi({
      state: "pending",
      label: "서버는 켜짐 · SMB 끊김",
      detail: "Broadcasting Schedule는 실행 중이지만 NAS SMB 세션이 없습니다. 다시 연결하세요.",
      pathText: linkedFileName || "아직 연결된 파일이 없습니다.",
      syncText: "SMB 재연결 필요",
      syncState: "pending",
    });
    setNasDialogState("연결이 끊어졌습니다. SMB 연결을 다시 확인하세요.", "error");
    throw new Error("SMB 연결이 유효하지 않습니다. 다시 연결을 확인하세요.");
  }
  bridgeConnected = true;
  connectionRoot = status.root || connectionRoot;
  let filesResponse;
  try {
    filesResponse = await fetch(`${API_BASE}/api/files`, { cache: "no-store" });
  } catch (error) {
    markBridgeOffline();
    throw new Error(bridgeDownMessage());
  }
  if (!filesResponse.ok) throw new Error(await readErrorMessage(filesResponse));
  const files = await filesResponse.json();
  if (!fileName || !files.includes(fileName)) {
    throw new Error("선택한 공유 파일이 없거나 더 이상 접근할 수 없습니다.");
  }
  return { root: status.root, files };
}

function loadSchedules() {
  try {
    const data = JSON.parse(localStorage.getItem(STORAGE_KEY));
    return Array.isArray(data) ? data.map(normalizeScheduleItem) : sampleSchedules();
  } catch {
    return sampleSchedules();
  }
}

function sampleSchedules() {
  return [
    {
      id: crypto.randomUUID(),
      scheduleDate: "2026-09-06",
      releaseDate: "2026-09-06",
      startTime: 1030,
      scheduleTitle: "주일 예배",
      accessoryInfo: "2부 예배",
      place: "Main_Large",
      sermonSpeaker: "홍길동",
      preparation: ["Recording", "Streaming"],
      preaparationNote: "송출 장비와 녹화 상태 확인",
      color: "orange",
    },
    {
      id: crypto.randomUUID(),
      scheduleDate: "2026-09-09",
      releaseDate: "2026-09-09",
      startTime: 1930,
      scheduleTitle: "수요 찬양 집회",
      accessoryInfo: "",
      place: "Main_Medium",
      sermonSpeaker: "김은혜",
      preparation: ["Praise"],
      preaparationNote: "",
      color: "coral",
    },
    {
      id: crypto.randomUUID(),
      scheduleDate: "2026-09-12",
      releaseDate: "2026-09-14",
      startTime: 900,
      scheduleTitle: "청년 리더십 워크숍",
      accessoryInfo: "3일 과정",
      place: "Edu_Large",
      sermonSpeaker: "",
      preparation: ["OtherDisplay"],
      preaparationNote: "프로젝터와 안내 배너 준비",
      color: "teal",
    },
  ];
}
function cloneSchedules(list = schedules) {
  return JSON.parse(JSON.stringify(list));
}

function setSchedulesBase(list = schedules) {
  schedulesBase = cloneSchedules(list);
}

function normalizeScheduleItem(item) {
  const colorState = normalizeColorState(item?.colorMode, item?.color);
  const normalized = {
    ...item,
    id: item.id || crypto.randomUUID(),
    preparation: Array.isArray(item.preparation) ? item.preparation : [],
    colorMode: colorState.colorMode,
    color: colorState.color,
  };
  if (item.officialDocument) {
    normalized.officialDocument = String(item.officialDocument);
  } else {
    delete normalized.officialDocument;
  }
  return normalized;
}

function isSpecialColorToken(value) {
  const token = String(value || "").trim().toLowerCase();
  return token === COLOR_MODE_NONE || token === COLOR_MODE_AUTO;
}

function resolveScheduleColorId(colorId) {
  if (SCHEDULE_COLORS.some((item) => item.id === colorId)) return colorId;
  if (isHexColor(colorId)) return String(colorId).trim().toLowerCase();
  return DEFAULT_SCHEDULE_COLOR;
}

function normalizeColorState(rawMode, rawColor) {
  const mode = String(rawMode || "").trim().toLowerCase();
  const color = rawColor == null ? "" : String(rawColor).trim();
  const colorLower = color.toLowerCase();
  if (mode === COLOR_MODE_NONE || colorLower === COLOR_MODE_NONE) {
    return { colorMode: COLOR_MODE_NONE, color: COLOR_MODE_NONE };
  }
  if (mode === COLOR_MODE_AUTO || colorLower === COLOR_MODE_AUTO) {
    return { colorMode: COLOR_MODE_AUTO, color: COLOR_MODE_AUTO };
  }
  if (mode === COLOR_MODE_CUSTOM) {
    return {
      colorMode: COLOR_MODE_CUSTOM,
      color: resolveScheduleColorId(color),
    };
  }
  if (
    SCHEDULE_COLORS.some((item) => item.id === color) ||
    isHexColor(color)
  ) {
    return { colorMode: COLOR_MODE_CUSTOM, color: resolveScheduleColorId(color) };
  }
  if (!mode && !color) {
    return { colorMode: DEFAULT_COLOR_MODE, color: DEFAULT_COLOR_MODE };
  }
  return {
    colorMode: COLOR_MODE_CUSTOM,
    color: resolveScheduleColorId(color),
  };
}

function encodeColorValue(state) {
  return state.colorMode === COLOR_MODE_CUSTOM ? state.color : state.colorMode;
}

function isHexColor(value) {
  return typeof value === "string" && /^#[0-9a-fA-F]{6}$/.test(value.trim());
}

function softFromHex(hex) {
  const raw = hex.replace("#", "");
  const r = parseInt(raw.slice(0, 2), 16);
  const g = parseInt(raw.slice(2, 4), 16);
  const b = parseInt(raw.slice(4, 6), 16);
  const mix = (c) => Math.round(c + (255 - c) * 0.86);
  return `#${[mix(r), mix(g), mix(b)]
    .map((n) => n.toString(16).padStart(2, "0"))
    .join("")}`;
}

function titleMatchesColorPattern(title, pattern) {
  const text = String(title || "");
  const trimmed = String(pattern || "").trim();
  if (!trimmed) return false;
  return trimmed.split(/\bor\b/).some((part) => {
    const tokens = part
      .split(/\band\b/)
      .map((token) => token.trim())
      .filter(Boolean);
    if (!tokens.length) return false;
    return tokens.every((token) => {
      // '=청년회' → 제목이 정확히 '청년회'일 때만 일치
      if (token.startsWith("=")) {
        return text === token.slice(1).trim();
      }
      return text.includes(token);
    });
  });
}

function matchAutoColor(title) {
  for (const rule of loadColorRules()) {
    if (titleMatchesColorPattern(title, rule.pattern)) return rule.color;
  }
  return null;
}

function getScheduleColor(colorId) {
  const id = resolveScheduleColorId(colorId);
  const preset = SCHEDULE_COLORS.find((item) => item.id === id);
  if (preset) return preset;
  if (isHexColor(id)) {
    return {
      id,
      label: "직접 선택",
      accent: id,
      soft: softFromHex(id),
    };
  }
  return SCHEDULE_COLORS[0];
}

function resolveEventDisplayColor(item) {
  const state = normalizeColorState(item?.colorMode, item?.color);
  if (state.colorMode === COLOR_MODE_NONE) {
    return {
      none: true,
      accent: "transparent",
      soft: "transparent",
      ink: "var(--ink)",
    };
  }
  let colorId = state.color;
  if (state.colorMode === COLOR_MODE_AUTO) {
    const hit = matchAutoColor(item?.scheduleTitle);
    if (!hit) {
      const fallback = loadColorAutoDefault();
      if (!fallback) {
        return {
          none: true,
          accent: "transparent",
          soft: "transparent",
          ink: "var(--ink)",
        };
      }
      colorId = fallback;
    } else {
      colorId = hit;
    }
  }
  const color = getScheduleColor(colorId);
  return {
    none: false,
    accent: color.accent,
    soft: color.soft,
    ink: "var(--ink)",
  };
}

function matchFontRule(title) {
  for (const rule of loadFontRules()) {
    if (titleMatchesColorPattern(title, rule.pattern)) return rule;
  }
  return null;
}

function normalizeFontHex(value, fallback) {
  const raw = String(value || "").trim().toLowerCase();
  if (isHexColor(raw)) return raw;
  return fallback;
}

function loadFontDefaults() {
  if (fontDefaultsLoaded) return fontDefaults;
  try {
    const raw = JSON.parse(localStorage.getItem(FONT_RULES_STORAGE_KEY) || "null");
    if (raw && typeof raw === "object" && !Array.isArray(raw)) {
      fontDefaults = {
        titleColor: normalizeFontHex(
          raw.defaultTitleColor,
          DEFAULT_TITLE_FONT_COLOR,
        ),
        speakerColor: normalizeFontHex(
          raw.defaultSpeakerColor,
          DEFAULT_SPEAKER_FONT_COLOR,
        ),
      };
    }
  } catch {
    /* keep defaults */
  }
  fontDefaultsLoaded = true;
  return fontDefaults;
}

function syncColorAutoDefaultControls() {
  const enabled = $("colorAutoDefaultEnabled");
  const colorInput = $("colorAutoDefaultColor");
  if (!enabled || !colorInput) return;
  const value = loadColorAutoDefault();
  enabled.checked = Boolean(value);
  if (value) {
    const accent = getScheduleColor(value).accent;
    if (isHexColor(accent)) colorInput.value = accent;
  }
  colorInput.disabled = !enabled.checked;
}

function resolveEventFontColors(item) {
  const defaults = loadFontDefaults();
  const rule = matchFontRule(item?.scheduleTitle);
  return {
    titleColor: rule?.titleColor || defaults.titleColor || DEFAULT_TITLE_FONT_COLOR,
    speakerColor:
      rule?.speakerColor || defaults.speakerColor || DEFAULT_SPEAKER_FONT_COLOR,
  };
}

function scheduleColorStyle(itemOrColor) {
  const item =
    itemOrColor && typeof itemOrColor === "object"
      ? itemOrColor
      : { color: itemOrColor, colorMode: COLOR_MODE_CUSTOM };
  const display = resolveEventDisplayColor(item);
  const fonts = resolveEventFontColors(item);
  const periodInk = resolvePeriodFontColor(item);
  // 배경은 soft(파스텔 보정)가 아니라 accent(지정색) 그대로
  return `--event-accent:${display.accent};--event-bg:${display.accent};--event-ink:${display.ink};--event-title-ink:${fonts.titleColor};--event-speaker-ink:${fonts.speakerColor};--event-period-ink:${periodInk}`;
}

function defaultMiscSettings() {
  return {
    showPeriodDates: false,
    periodColorMode: PERIOD_COLOR_MODE_SPEAKER,
    periodColor: DEFAULT_PERIOD_COLOR,
  };
}

function loadMiscSettings() {
  if (miscSettingsCache) return miscSettingsCache;
  try {
    const raw = JSON.parse(
      localStorage.getItem(MISC_SETTINGS_STORAGE_KEY) || "null",
    );
    if (raw && typeof raw === "object") {
      const mode = String(raw.periodColorMode || "");
      miscSettingsCache = {
        showPeriodDates: Boolean(raw.showPeriodDates),
        periodColorMode:
          mode === PERIOD_COLOR_MODE_TITLE ||
          mode === PERIOD_COLOR_MODE_SPEAKER ||
          mode === PERIOD_COLOR_MODE_CUSTOM
            ? mode
            : PERIOD_COLOR_MODE_SPEAKER,
        periodColor: normalizeFontHex(raw.periodColor, DEFAULT_PERIOD_COLOR),
      };
      return miscSettingsCache;
    }
  } catch {
    /* keep defaults */
  }
  miscSettingsCache = defaultMiscSettings();
  return miscSettingsCache;
}

function saveMiscSettings(next) {
  miscSettingsCache = {
    showPeriodDates: Boolean(next?.showPeriodDates),
    periodColorMode:
      next?.periodColorMode === PERIOD_COLOR_MODE_TITLE ||
      next?.periodColorMode === PERIOD_COLOR_MODE_SPEAKER ||
      next?.periodColorMode === PERIOD_COLOR_MODE_CUSTOM
        ? next.periodColorMode
        : PERIOD_COLOR_MODE_SPEAKER,
    periodColor: normalizeFontHex(next?.periodColor, DEFAULT_PERIOD_COLOR),
  };
  try {
    localStorage.setItem(
      MISC_SETTINGS_STORAGE_KEY,
      JSON.stringify(miscSettingsCache),
    );
  } catch {
    /* ignore */
  }
  return miscSettingsCache;
}

function resolvePeriodFontColor(item) {
  const misc = loadMiscSettings();
  const fonts = resolveEventFontColors(item);
  if (misc.periodColorMode === PERIOD_COLOR_MODE_TITLE) return fonts.titleColor;
  if (misc.periodColorMode === PERIOD_COLOR_MODE_SPEAKER)
    return fonts.speakerColor;
  return misc.periodColor || DEFAULT_PERIOD_COLOR;
}

function isConsecutiveCalendarDay(prevIso, nextIso) {
  const prev = parseIsoDateLocal(prevIso);
  if (!prev) return false;
  const expected = dateInputValue(
    new Date(prev.getFullYear(), prev.getMonth(), prev.getDate() + 1),
  );
  return expected === nextIso;
}

/** 같은 제목·강사로 이어진 날짜 구간(2일 이상일 때만) */
function findConsecutivePeriod(item) {
  if (!item?.scheduleDate) return null;
  const title = String(item.scheduleTitle || "").trim();
  const speaker = String(item.sermonSpeaker || "").trim();
  if (!title && !speaker) return null;
  const key = consecutiveEventKey(item);
  const dates = [
    ...new Set(
      schedules
        .filter((entry) => consecutiveEventKey(entry) === key)
        .map((entry) => entry.scheduleDate)
        .filter(Boolean),
    ),
  ].sort();
  const idx = dates.indexOf(item.scheduleDate);
  if (idx < 0) return null;
  let start = idx;
  let end = idx;
  while (start > 0 && isConsecutiveCalendarDay(dates[start - 1], dates[start])) {
    start -= 1;
  }
  while (
    end < dates.length - 1 &&
    isConsecutiveCalendarDay(dates[end], dates[end + 1])
  ) {
    end += 1;
  }
  if (end <= start) return null;
  return { start: dates[start], end: dates[end] };
}

function formatPeriodPrefix(startIso, endIso) {
  const start = parseIsoDateLocal(startIso);
  const end = parseIsoDateLocal(endIso);
  if (!start || !end) return "";
  return `(${start.getMonth() + 1}.${start.getDate()}~${end.getMonth() + 1}.${end.getDate()})`;
}

function defaultHolidayBgSettings() {
  return { enabled: false, color: DEFAULT_HOLIDAY_BG_COLOR };
}

function loadHolidayBgSettings() {
  if (holidayBgCache) return holidayBgCache;
  try {
    const raw = JSON.parse(localStorage.getItem(HOLIDAY_BG_STORAGE_KEY) || "null");
    if (raw && typeof raw === "object") {
      holidayBgCache = {
        enabled: Boolean(raw.enabled),
        color: normalizeFontHex(raw.color, DEFAULT_HOLIDAY_BG_COLOR),
      };
      return holidayBgCache;
    }
  } catch {
    /* keep defaults */
  }
  holidayBgCache = defaultHolidayBgSettings();
  return holidayBgCache;
}

function saveHolidayBgSettings(next) {
  holidayBgCache = {
    enabled: Boolean(next?.enabled),
    color: normalizeFontHex(next?.color, DEFAULT_HOLIDAY_BG_COLOR),
  };
  try {
    localStorage.setItem(
      HOLIDAY_BG_STORAGE_KEY,
      JSON.stringify(holidayBgCache),
    );
  } catch {
    /* ignore */
  }
  applyHolidayBackgroundCss();
  return holidayBgCache;
}

function applyHolidayBackgroundCss() {
  const settings = loadHolidayBgSettings();
  const root = document.documentElement;
  if (settings.enabled) {
    root.style.setProperty("--calendar-holiday-bg", settings.color);
    root.classList.add("has-holiday-bg");
  } else {
    root.style.removeProperty("--calendar-holiday-bg");
    root.classList.remove("has-holiday-bg");
  }
}

function syncMiscSettingsControls() {
  const misc = loadMiscSettings();
  const show = $("miscShowPeriodDates");
  const mode = $("miscPeriodColorMode");
  const color = $("miscPeriodColor");
  if (show) show.checked = misc.showPeriodDates;
  if (mode) mode.value = misc.periodColorMode;
  if (color) {
    color.value = misc.periodColor;
    color.disabled = misc.periodColorMode !== PERIOD_COLOR_MODE_CUSTOM;
  }
}

function syncHolidayBgControls() {
  const settings = loadHolidayBgSettings();
  const enabled = $("holidayBgEnabled");
  const color = $("holidayBgColor");
  if (enabled) enabled.checked = settings.enabled;
  if (color) {
    color.value = settings.color;
    color.disabled = !settings.enabled;
  }
}

function normalizePresetItem(raw) {
  if (!raw || typeof raw !== "object") return null;
  const id = String(raw.id || "").trim() || crypto.randomUUID();
  const name = String(raw.name || "").trim();
  if (!name) return null;
  const values =
    raw.values && typeof raw.values === "object" && !Array.isArray(raw.values)
      ? { ...raw.values }
      : {};
  let fields = Array.isArray(raw.fields)
    ? raw.fields.map(String).filter((f) => PRESET_FIELD_LABELS[f])
    : Object.keys(values).filter((f) => PRESET_FIELD_LABELS[f]);
  if (!fields.length) fields = Object.keys(PRESET_FIELD_LABELS);
  const cleanedValues = {};
  for (const field of fields) {
    cleanedValues[field] = values[field];
  }
  return {
    id,
    name,
    builtin: Boolean(raw.builtin),
    fields,
    values: cleanedValues,
  };
}

function parsePresetsPayload(raw) {
  if (Array.isArray(raw)) {
    return raw.map(normalizePresetItem).filter(Boolean);
  }
  if (raw && typeof raw === "object" && Array.isArray(raw.presets)) {
    return raw.presets.map(normalizePresetItem).filter(Boolean);
  }
  return [];
}

function presetsPayload(presets) {
  return {
    version: 1,
    updatedAt: new Date().toISOString(),
    presets: presets.map((p) => ({
      id: p.id,
      name: p.name,
      builtin: Boolean(p.builtin),
      fields: [...(p.fields || [])],
      values: { ...(p.values || {}) },
    })),
  };
}

function loadLocalPresets() {
  try {
    return parsePresetsPayload(JSON.parse(localStorage.getItem(PRESET_STORAGE_KEY) || "[]"));
  } catch {
    return [];
  }
}

function saveLocalPresets(presets) {
  localStorage.setItem(PRESET_STORAGE_KEY, JSON.stringify(presets));
}

function loadUserPresets() {
  if (Array.isArray(userPresetsCache)) return userPresetsCache.map((p) => ({ ...p, fields: [...p.fields], values: { ...p.values } }));
  userPresetsCache = loadLocalPresets();
  return userPresetsCache.map((p) => ({ ...p, fields: [...p.fields], values: { ...p.values } }));
}

function getAllPresets() {
  return loadUserPresets();
}

function updatePresetSyncHint() {
  const el = $("presetSyncHint");
  if (!el) return;
  if (bridgeAlive && bridgeConnected && linkedFileName) {
    el.textContent = presetsDirty
      ? "NAS 공유 프리셋 · 저장 중…"
      : "NAS에 공유되는 프리셋입니다. 관리에서 순서·수정이 가능합니다.";
  } else {
    el.textContent = "로컬에만 저장됩니다. NAS 연결 시 공유 파일로 올라갑니다.";
  }
}

function setPresetManageMode(on) {
  presetManageMode = Boolean(on);
  $("presetManageToggle")?.classList.toggle("active", presetManageMode);
  if (presetManageMode) setPresetCreatePanelOpen(false);
  renderPresetList();
}

async function readPresetsFromNas() {
  const tryRead = async (fileName) => {
    const response = await fetch(
      `${API_BASE}/api/file?name=` + encodeURIComponent(fileName),
      { cache: "no-store" },
    );
    if (!response.ok) {
      if (response.status === 400 || response.status === 404) {
        return null;
      }
      throw new Error(await readErrorMessage(response));
    }
    const revision = response.headers.get("X-File-Revision") || "";
    let raw;
    try {
      raw = await response.json();
    } catch {
      raw = [];
    }
    return { presets: parsePresetsPayload(raw), revision };
  };

  const primary = await tryRead(PRESETS_FILE);
  if (primary) return primary;

  // 구버전 파일명 호환 (일정 목록에 섞일 수 있어 읽기만)
  const legacy = await tryRead("schedule-presets.json");
  if (legacy) return legacy;

  return { presets: [], revision: "" };
}

async function writePresetsToNas(presets, expectedRevision) {
  const headers = { "Content-Type": "application/json" };
  if (expectedRevision) headers["X-Expected-Revision"] = expectedRevision;
  const response = await fetch(
    `${API_BASE}/api/file?name=` + encodeURIComponent(PRESETS_FILE),
    {
      method: "PUT",
      headers,
      body: JSON.stringify(presetsPayload(presets), null, 2),
    },
  );
  if (response.status === 409) {
    const conflict = await response.json().catch(() => ({}));
    const err = new Error("충돌");
    err.conflict = conflict;
    throw err;
  }
  if (!response.ok) throw new Error(await readErrorMessage(response));
  const result = await response.json().catch(() => ({}));
  return result.revision || "";
}

function mergePresetsById(preferredList, otherList, deletedIds = presetDeletedIds) {
  const map = new Map();
  for (const p of otherList) {
    if (deletedIds.has(p.id)) continue;
    map.set(p.id, p);
  }
  for (const p of preferredList) map.set(p.id, p);
  // preferred order first, then extras from other (삭제분 제외)
  const ordered = [];
  const seen = new Set();
  for (const p of preferredList) {
    ordered.push(map.get(p.id));
    seen.add(p.id);
  }
  for (const p of otherList) {
    if (seen.has(p.id) || deletedIds.has(p.id)) continue;
    ordered.push(map.get(p.id));
  }
  return ordered;
}

async function refreshPresetsFromNas({ quiet = true } = {}) {
  try {
    if (presetsDirty) {
      updatePresetSyncHint();
      renderPresetList();
      return;
    }
    if (!(bridgeAlive && bridgeConnected && linkedFileName)) {
      userPresetsCache = loadLocalPresets();
      presetsRevision = "";
      updatePresetSyncHint();
      renderPresetList();
      return;
    }
    const { presets: remote, revision } = await readPresetsFromNas();
    presetsRevision = revision;
    const local = loadLocalPresets();
    const migrated = localStorage.getItem(PRESET_LOCAL_MIGRATED_KEY) === "1";

    if (!remote.length && local.length && !migrated) {
      userPresetsCache = local;
      saveLocalPresets(local);
      try {
        presetsRevision = await writePresetsToNas(local, revision);
        localStorage.setItem(PRESET_LOCAL_MIGRATED_KEY, "1");
        if (!quiet) showToast("로컬 프리셋을 NAS에 올렸습니다.");
      } catch (err) {
        if (!quiet) showToast(`프리셋 NAS 업로드 실패: ${err.message}`);
      }
    } else if (remote.length && local.length && !migrated) {
      userPresetsCache = mergePresetsById(local, remote);
      localStorage.setItem(PRESET_LOCAL_MIGRATED_KEY, "1");
      saveLocalPresets(userPresetsCache);
      try {
        presetsRevision = await writePresetsToNas(userPresetsCache, revision);
      } catch {
        // keep merged locally
      }
    } else {
      userPresetsCache = remote.length ? remote : local;
      saveLocalPresets(userPresetsCache);
      if (!remote.length && local.length) {
        try {
          presetsRevision = await writePresetsToNas(userPresetsCache, revision);
        } catch {
          /* offline-ish */
        }
      }
    }
    presetsDirty = false;
    updatePresetSyncHint();
    renderPresetList();
  } catch (err) {
    userPresetsCache = loadLocalPresets();
    updatePresetSyncHint();
    renderPresetList();
    if (!quiet) showToast(`프리셋 불러오기 실패: ${err.message}`);
  }
}

async function persistPresets(presets, { toastMessage } = {}) {
  userPresetsCache = presets.map(normalizePresetItem).filter(Boolean);
  saveLocalPresets(userPresetsCache);
  presetsDirty = true;
  updatePresetSyncHint();
  renderPresetList();
  const snapshot = userPresetsCache.map((p) => ({ ...p, fields: [...(p.fields || [])], values: { ...(p.values || {}) } }));

  if (!(bridgeAlive && bridgeConnected && linkedFileName)) {
    // NAS 미연결: 로컬만. dirty 유지 → 이후 리프레시가 덮어쓰지 않음
    updatePresetSyncHint();
    if (toastMessage) showToast(`${toastMessage} (로컬)`);
    return;
  }

  try {
    presetsRevision = await writePresetsToNas(snapshot, presetsRevision);
    const stillSame =
      JSON.stringify(userPresetsCache) === JSON.stringify(snapshot);
    if (stillSame) {
      presetsDirty = false;
      for (const id of [...presetDeletedIds]) {
        if (!snapshot.some((p) => p.id === id)) presetDeletedIds.delete(id);
      }
    }
    updatePresetSyncHint();
    if (toastMessage) showToast(toastMessage);
  } catch (err) {
    if (err.conflict) {
      try {
        const remoteRaw = err.conflict?.content;
        const remote = parsePresetsPayload(
          typeof remoteRaw === "string" ? JSON.parse(remoteRaw) : remoteRaw,
        );
        presetsRevision = err.conflict.revision || "";
        const merged = mergePresetsById(snapshot, remote);
        userPresetsCache = merged;
        saveLocalPresets(userPresetsCache);
        const mergedSnap = merged.map((p) => ({ ...p, fields: [...(p.fields || [])], values: { ...(p.values || {}) } }));
        presetsRevision = await writePresetsToNas(mergedSnap, presetsRevision);
        presetsDirty = false;
        for (const id of [...presetDeletedIds]) {
          if (!mergedSnap.some((p) => p.id === id)) presetDeletedIds.delete(id);
        }
        updatePresetSyncHint();
        renderPresetList();
        showToast("다른 PC 프리셋과 병합 후 저장했습니다.");
        return;
      } catch (mergeErr) {
        presetsDirty = true;
        updatePresetSyncHint();
        showToast(`프리셋 충돌 해결 실패: ${mergeErr.message}`);
        return;
      }
    }
    presetsDirty = true;
    showToast(`프리셋 NAS 저장 실패: ${err.message} (로컬에는 저장됨)`);
    updatePresetSyncHint();
  }
}

function saveUserPresets(presets) {
  // sync API kept for callers — fire and forget persist
  void persistPresets(presets);
}

function renderPresetList() {
  const list = $("presetList");
  if (!list) return;
  updatePresetSyncHint();
  const presets = getAllPresets();
  $("presetManageToggle")?.classList.toggle("active", presetManageMode);

  if (!presets.length) {
    list.classList.toggle("is-manage", false);
    list.innerHTML =
      '<span class="preset-empty">프리셋 만들기로 반복 일정을 저장하세요.</span>';
    return;
  }

  if (presetManageMode) {
    list.classList.add("is-manage");
    list.innerHTML = presets
      .map((preset, index) => {
        const fields = preset.fields || Object.keys(preset.values || {});
        const fieldSummary = fields
          .map((f) => PRESET_FIELD_LABELS[f] || f)
          .join(" · ");
        return `<div class="preset-manage-row" draggable="true" data-preset-id="${escapeHtml(preset.id)}" data-index="${index}">
          <span class="preset-drag" title="끌어서 순서 변경" aria-hidden="true">⋮⋮</span>
          <div class="preset-manage-main">
            <strong>${escapeHtml(preset.name)}</strong>
            <span class="preset-manage-meta">${escapeHtml(fieldSummary || "항목 없음")}</span>
          </div>
          <div class="preset-manage-actions">
            <button type="button" class="button ghost preset-move" data-preset-up="${escapeHtml(preset.id)}" ${index === 0 ? "disabled" : ""} aria-label="위로">↑</button>
            <button type="button" class="button ghost preset-move" data-preset-down="${escapeHtml(preset.id)}" ${index === presets.length - 1 ? "disabled" : ""} aria-label="아래로">↓</button>
            <button type="button" class="button soft" data-preset-edit="${escapeHtml(preset.id)}">수정</button>
            <button type="button" class="button ghost danger-text" data-preset-delete="${escapeHtml(preset.id)}">삭제</button>
          </div>
        </div>`;
      })
      .join("");
    bindPresetManageDnD(list);
    return;
  }

  list.classList.remove("is-manage");
  list.innerHTML = presets
    .map((preset) => {
      const active = activePresetId === preset.id ? " active" : "";
      const fields = preset.fields || Object.keys(preset.values || {});
      const colorState =
        fields.includes("color") && preset.values?.color
          ? normalizeColorState(preset.values.colorMode, preset.values.color)
          : null;
      const color =
        colorState?.colorMode === COLOR_MODE_CUSTOM
          ? getScheduleColor(colorState.color)
          : null;
      const colorAttr = color
        ? ` style="--preset-accent:${color.accent};--preset-soft:${color.soft}" data-has-color="1"`
        : "";
      return `<span class="preset-chip-wrap">
        <button type="button" class="preset-chip${active}${color ? " has-color" : ""}" data-preset-id="${escapeHtml(preset.id)}" role="listitem"${colorAttr}>${escapeHtml(preset.name)}</button>
      </span>`;
    })
    .join("");
}

function bindPresetManageDnD(list) {
  list.querySelectorAll(".preset-manage-row").forEach((row) => {
    row.addEventListener("dragstart", (e) => {
      presetDragId = row.dataset.presetId;
      row.classList.add("dragging");
      e.dataTransfer.effectAllowed = "move";
      try {
        e.dataTransfer.setData("text/plain", presetDragId);
      } catch {
        /* ignore */
      }
    });
    row.addEventListener("dragend", () => {
      row.classList.remove("dragging");
      presetDragId = null;
      list.querySelectorAll(".preset-manage-row").forEach((r) => r.classList.remove("drag-over"));
    });
    row.addEventListener("dragover", (e) => {
      e.preventDefault();
      row.classList.add("drag-over");
    });
    row.addEventListener("dragleave", () => row.classList.remove("drag-over"));
    row.addEventListener("drop", (e) => {
      e.preventDefault();
      row.classList.remove("drag-over");
      const fromId = presetDragId || e.dataTransfer.getData("text/plain");
      const toId = row.dataset.presetId;
      if (!fromId || !toId || fromId === toId) return;
      reorderPreset(fromId, toId);
    });
  });
}

function reorderPreset(fromId, toId) {
  const presets = loadUserPresets();
  const from = presets.findIndex((p) => p.id === fromId);
  const to = presets.findIndex((p) => p.id === toId);
  if (from < 0 || to < 0) return;
  const [item] = presets.splice(from, 1);
  presets.splice(to, 0, item);
  void persistPresets(presets, { toastMessage: "프리셋 순서를 저장했습니다." });
}

function movePreset(presetId, delta) {
  const presets = loadUserPresets();
  const index = presets.findIndex((p) => p.id === presetId);
  if (index < 0) return;
  const next = index + delta;
  if (next < 0 || next >= presets.length) return;
  const [item] = presets.splice(index, 1);
  presets.splice(next, 0, item);
  void persistPresets(presets, { toastMessage: "프리셋 순서를 저장했습니다." });
}

function setPresetCreatePanelOpen(open) {
  $("presetCreatePanel")?.classList.toggle("hidden", !open);
  if (open && $("presetName")) {
    if (!editingPresetId) {
      $("presetName").value = $("title")?.value?.trim() || "";
    }
    $("presetName").focus();
  }
  if (!open) {
    editingPresetId = null;
    const saveBtn = $("presetSaveButton");
    if (saveBtn) saveBtn.textContent = "현재 내용으로 저장";
  }
}

function setPresetFieldChecks(fields) {
  const set = new Set(fields || []);
  document.querySelectorAll("[data-preset-field]").forEach((box) => {
    box.checked = set.size ? set.has(box.dataset.presetField) : true;
  });
}

function beginEditPreset(presetId) {
  const preset = getAllPresets().find((p) => p.id === presetId);
  if (!preset) return;
  editingPresetId = preset.id;
  applyPreset(preset, { quiet: true });
  setPresetFieldChecks(preset.fields);
  if ($("presetName")) $("presetName").value = preset.name;
  const saveBtn = $("presetSaveButton");
  if (saveBtn) saveBtn.textContent = "수정 내용 저장";
  setPresetManageMode(false);
  setPresetCreatePanelOpen(true);
  showToast(`“${preset.name}” 수정 모드 — 폼·항목을 바꾼 뒤 저장하세요.`);
}

function readFormSnapshotForPreset() {
  const preparation = [
    ...document.querySelectorAll("#scheduleForm fieldset input:checked"),
  ].map((box) => box.value);
  return {
    startTime: $("startTime").value ? Number($("startTime").value) : 0,
    scheduleTitle: $("title").value.trim(),
    place: $("place").value || "None",
    sermonSpeaker: $("speaker").value.trim(),
    accessoryInfo: $("accessory").value.trim(),
    preaparationNote: $("note").value.trim(),
    preparation,
    color: encodeColorValue(getFormColorState()),
    colorMode: getFormColorState().colorMode,
  };
}

function applyPreset(preset, { quiet = false } = {}) {
  if (!preset) return;
  activePresetId = preset.id;
  const values = preset.values || {};
  const fields = preset.fields || Object.keys(values);
  if (fields.includes("startTime")) {
    setStartTimeValue(
      values.startTime ? String(values.startTime).padStart(4, "0") : "",
    );
  }
  if (fields.includes("scheduleTitle") && $("title")) {
    $("title").value = values.scheduleTitle || "";
  }
  if (fields.includes("place")) setPlaceValue(values.place || "None");
  if (fields.includes("sermonSpeaker") && $("speaker")) {
    $("speaker").value = values.sermonSpeaker || "";
  }
  if (fields.includes("accessoryInfo") && $("accessory")) {
    $("accessory").value = values.accessoryInfo || "";
  }
  if (fields.includes("preaparationNote") && $("note")) {
    $("note").value = values.preaparationNote || "";
  }
  if (fields.includes("preparation")) {
    const prep = Array.isArray(values.preparation) ? values.preparation : [];
    document
      .querySelectorAll("#scheduleForm fieldset input")
      .forEach((box) => {
        box.checked = prep.includes(box.value);
      });
  }
  if (fields.includes("color")) {
    setFormColorState(values.colorMode, values.color);
  }
  renderPresetList();
  if (!quiet) showToast(`프리셋 “${preset.name}”을 적용했습니다.`);
}

function savePresetFromForm() {
  const name = $("presetName")?.value.trim();
  if (!name) return showToast("프리셋 이름을 입력해 주세요.");
  const fields = [
    ...document.querySelectorAll("[data-preset-field]:checked"),
  ].map((box) => box.dataset.presetField);
  if (!fields.length) return showToast("저장할 항목을 하나 이상 선택해 주세요.");
  const snapshot = readFormSnapshotForPreset();
  const values = {};
  for (const field of fields) {
    values[field] = snapshot[field];
  }
  if (fields.includes("scheduleTitle") && !values.scheduleTitle) {
    return showToast("제목을 저장하려면 일정 제목을 입력해 주세요.");
  }
  const presets = loadUserPresets();
  if (editingPresetId) {
    const index = presets.findIndex((p) => p.id === editingPresetId);
    if (index < 0) {
      editingPresetId = null;
    } else {
      presets[index] = {
        ...presets[index],
        name,
        builtin: false,
        fields,
        values,
      };
      const savedName = name;
      editingPresetId = null;
      setPresetCreatePanelOpen(false);
      void persistPresets(presets, {
        toastMessage: `프리셋 “${savedName}”을 수정·저장했습니다.`,
      });
      return;
    }
  }
  presets.push({
    id: crypto.randomUUID(),
    name,
    builtin: false,
    fields,
    values,
  });
  setPresetCreatePanelOpen(false);
  void persistPresets(presets, { toastMessage: `프리셋 “${name}”을 저장했습니다.` });
}

function deleteUserPreset(presetId) {
  if (presetId) presetDeletedIds.add(presetId);
  const presets = loadUserPresets().filter((item) => item.id !== presetId);
  if (activePresetId === presetId) activePresetId = null;
  if (editingPresetId === presetId) {
    editingPresetId = null;
    setPresetCreatePanelOpen(false);
  }
  void persistPresets(presets, { toastMessage: "프리셋을 삭제했습니다." });
}
function renderColorSwatches(selectedId = DEFAULT_SCHEDULE_COLOR) {
  const host = $("colorSwatches");
  if (!host) return;
  const selected = resolveScheduleColorId(selectedId);
  host.innerHTML = SCHEDULE_COLORS.map(
    (color) =>
      `<button type="button" class="color-swatch${color.id === selected ? " active" : ""}" data-color="${color.id}" title="${escapeHtml(color.label)}" aria-label="${escapeHtml(color.label)}" style="background:${color.accent}"></button>`,
  ).join("");
  const custom = $("scheduleColorCustom");
  const customWrap = custom?.closest(".color-custom-pick");
  if (custom) {
    custom.value = getScheduleColor(selected).accent;
  }
  customWrap?.classList.toggle("active", isHexColor(selected));
}

function getFormColorState() {
  return normalizeColorState(
    $("scheduleColorMode")?.value,
    $("scheduleColor")?.value,
  );
}

function setFormColorState(rawMode, rawColor) {
  const state = normalizeColorState(rawMode, rawColor);
  if ($("scheduleColorMode")) $("scheduleColorMode").value = state.colorMode;
  if ($("scheduleColor")) $("scheduleColor").value = encodeColorValue(state);
  document.querySelectorAll("[data-color-mode]").forEach((button) => {
    button.classList.toggle("active", button.dataset.colorMode === state.colorMode);
  });
  $("colorCustomPanel")?.classList.toggle(
    "hidden",
    state.colorMode !== COLOR_MODE_CUSTOM,
  );
  if (state.colorMode === COLOR_MODE_CUSTOM) {
    renderColorSwatches(state.color);
  } else {
    renderColorSwatches(DEFAULT_SCHEDULE_COLOR);
  }
}

function setScheduleColorValue(colorId) {
  setFormColorState(COLOR_MODE_CUSTOM, colorId);
}

function colorFingerprint(item) {
  const state = normalizeColorState(item?.colorMode, item?.color);
  return `${state.colorMode}\u0001${state.color}`;
}

function scheduleFingerprint(item) {
  const prep = Array.isArray(item.preparation)
    ? [...item.preparation].sort().join("|")
    : "";
  return [
    item.scheduleDate || "",
    item.releaseDate || "",
    String(item.startTime ?? ""),
    item.scheduleTitle || "",
    item.accessoryInfo || "",
    item.place || "",
    item.sermonSpeaker || "",
    prep,
    item.preaparationNote || "",
    item.officialDocument || "",
    colorFingerprint(item),
  ].join("\u0001");
}

function mergeSchedules(base, local, remote) {
  const baseMap = new Map(base.map((item) => [item.id, item]));
  const localMap = new Map(local.map((item) => [item.id, item]));
  const remoteMap = new Map(remote.map((item) => [item.id, item]));
  const ids = new Set([...baseMap.keys(), ...localMap.keys(), ...remoteMap.keys()]);
  const merged = [];
  let conflictCount = 0;

  for (const id of ids) {
    const baseItem = baseMap.get(id);
    const localItem = localMap.get(id);
    const remoteItem = remoteMap.get(id);

    if (!localItem && !remoteItem) continue;

    if (!localItem && remoteItem) {
      if (!baseItem || scheduleFingerprint(baseItem) !== scheduleFingerprint(remoteItem)) {
        merged.push(remoteItem);
      }
      continue;
    }

    if (localItem && !remoteItem) {
      if (!baseItem || scheduleFingerprint(baseItem) !== scheduleFingerprint(localItem)) {
        merged.push(localItem);
      }
      continue;
    }

    const localFp = scheduleFingerprint(localItem);
    const remoteFp = scheduleFingerprint(remoteItem);
    if (localFp === remoteFp) {
      merged.push(localItem);
      continue;
    }

    const baseFp = baseItem ? scheduleFingerprint(baseItem) : null;
    if (baseFp !== null && localFp === baseFp) {
      merged.push(remoteItem);
      continue;
    }
    if (baseFp !== null && remoteFp === baseFp) {
      merged.push(localItem);
      continue;
    }

    merged.push(localItem);
    conflictCount += 1;
  }

  return { merged, conflictCount };
}

function markNasDirty(fileName = linkedFileName) {
  if (!fileName) return;
  localStorage.setItem(NAS_DIRTY_KEY, fileName);
}

function clearNasDirty(fileName = linkedFileName) {
  const dirty = localStorage.getItem(NAS_DIRTY_KEY);
  if (!dirty) return;
  if (!fileName || dirty === fileName) localStorage.removeItem(NAS_DIRTY_KEY);
}

function isNasDirty(fileName = linkedFileName) {
  return Boolean(fileName) && localStorage.getItem(NAS_DIRTY_KEY) === fileName;
}

function compareSchedulesWithinDay(a, b) {
  const timeA = Number(a.startTime) || 0;
  const timeB = Number(b.startTime) || 0;
  if (timeA !== timeB) return timeA - timeB;
  return String(a.preaparationNote || "").localeCompare(
    String(b.preaparationNote || ""),
    "ko",
  );
}

function scheduleAutoSave() {
  if (!linkedFileName || !bridgeConnected || !bridgeAlive) return;
  markNasDirty(linkedFileName);
  if (autoSaveTimer) clearTimeout(autoSaveTimer);
  autoSaveTimer = setTimeout(() => {
    autoSaveTimer = null;
    void flushAutoSave();
  }, AUTO_SAVE_DELAY_MS);
}

async function flushAutoSave() {
  if (!linkedFileName || !bridgeConnected || !bridgeAlive) return;
  if (autoSaveInFlight) {
    autoSaveQueued = true;
    return;
  }
  autoSaveInFlight = true;
  try {
    await saveSharedFile({ quiet: true });
  } finally {
    autoSaveInFlight = false;
    if (autoSaveQueued) {
      autoSaveQueued = false;
      scheduleAutoSave();
    }
  }
}

function flushPendingSaveOnExit() {
  if (autoSaveTimer) {
    clearTimeout(autoSaveTimer);
    autoSaveTimer = null;
  }
  if (!linkedFileName || !bridgeConnected || !bridgeAlive) return;
  if (!isNasDirty(linkedFileName) && !autoSaveInFlight && !autoSaveQueued) return;
  localStorage.setItem(STORAGE_KEY, JSON.stringify(schedules));
  markNasDirty(linkedFileName);
  const headers = { "Content-Type": "application/json" };
  if (linkedFileRevision) headers["X-Expected-Revision"] = linkedFileRevision;
  try {
    fetch(`${API_BASE}/api/file?name=` + encodeURIComponent(linkedFileName), {
      method: "PUT",
      headers,
      body: JSON.stringify(schedules, null, 2),
      keepalive: true,
    }).catch(() => {});
  } catch {
    /* unload 중 실패는 dirty 플래그로 다음 접속 시 복구 */
  }
}

function saveLocal({ fromNas = false } = {}) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(schedules));
  if (fromNas) return;
  if (linkedFileName && bridgeConnected && bridgeAlive) {
    lastSaveOk = null;
    updateSaveStatusUi("로컬 변경 · 자동 저장 중…", "pending");
    $("syncStatus").textContent = "로컬 변경 · 자동 저장 중…";
    $("syncStatus").dataset.state = "pending";
    scheduleAutoSave();
    return;
  }
  if (linkedFileName) {
    markNasDirty(linkedFileName);
    lastSaveOk = null;
    updateSaveStatusUi("로컬 변경 있음 · NAS에 저장하세요.", "pending");
    $("syncStatus").textContent = "로컬 변경 · 저장 필요";
    $("syncStatus").dataset.state = "pending";
  }
}
function dateValue(value) {
  return new Date(`${value}T00:00:00`);
}
function dateInputValue(date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
function formatDate(value) {
  const date = dateValue(value);
  return {
    day: String(date.getDate()).padStart(2, "0"),
    month: date.toLocaleDateString("ko-KR", { month: "short" }),
    week: `(${DAY_NAMES[date.getDay()]})`,
  };
}
function formatTime(value) {
  if (!value) return "-";
  const raw = String(value).padStart(4, "0");
  const hour = Number(raw.slice(0, 2));
  const minute = raw.slice(2);
  return `${hour >= 12 ? "오후" : "오전"} ${String(hour % 12 || 12).padStart(2, "0")}:${minute}`;
}
function escapeHtml(value = "") {
  return String(value).replace(
    /[&<>'"]/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[
        c
      ],
  );
}
const VIEW_STORAGE_KEY = "schedule-data-manager-view";
const THEME_STORAGE_KEY = "schedule-data-manager-theme";
const THEME_IDS = ["garden", "slate", "studio", "ocean", "ink"];
const NOTICE_FILE = "_shared_notice.json";
const NOTICE_STORAGE_KEY = "schedule-data-manager-notice";
const NOTICE_LOCK_TTL_MS = 45000;
const NOTICE_HEARTBEAT_MS = 12000;
const NOTICE_POLL_MS = 5000;
const NOTICE_HISTORY_MAX = 10;
const QUICK_TIMES = [
  { value: 1000, label: "오전 10:00" },
  { value: 1340, label: "오후 1:40" },
  { value: 1400, label: "오후 2:00" },
  { value: 1600, label: "오후 4:00" },
  { value: 1930, label: "오후 7:30" },
];
const NOTICE_SESSION_ID =
  sessionStorage.getItem("schedule-notice-session") ||
  (() => {
    const id = crypto.randomUUID();
    sessionStorage.setItem("schedule-notice-session", id);
    return id;
  })();

let timePickerMode = "unset"; // unset | custom | quick
let sharedNoticeText = "";
let sharedNoticeUpdatedAt = "";
let sharedNoticeHistory = [];
let sharedNoticeViewIndex = 0;
let sharedNoticeRevision = "";
let sharedNoticeEditing = false;
let sharedNoticeDraft = "";
let sharedNoticeLock = null;
let sharedNoticeHeartbeatTimer = null;
let sharedNoticePollTimer = null;

function loadViewMode() {
  const saved = localStorage.getItem(VIEW_STORAGE_KEY);
  if (saved === "list") return "list";
  return "calendar";
}

function loadTheme() {
  const saved = localStorage.getItem(THEME_STORAGE_KEY);
  return THEME_IDS.includes(saved) ? saved : "garden";
}

function applyTheme(theme) {
  const next = THEME_IDS.includes(theme) ? theme : "garden";
  document.documentElement.setAttribute("data-theme", next);
  try {
    localStorage.setItem(THEME_STORAGE_KEY, next);
  } catch (_) {
    /* ignore quota / private mode */
  }
  ["themeSelect", "loginThemeSelect"].forEach((id) => {
    const el = $(id);
    if (el) el.value = next;
  });
}

function initThemeControls() {
  applyTheme(loadTheme());
  applyHolidayBackgroundCss();
  ["themeSelect", "loginThemeSelect"].forEach((id) => {
    const el = $(id);
    if (el) el.onchange = () => applyTheme(el.value);
  });
}

function startOfMonth(date) {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

let viewMode = loadViewMode();
let calendarMonth = startOfMonth(new Date());
let exportSelectMode = false;
const selectedExportIds = new Set();
let pendingImportMode = null;
const listCollapsedDates = new Set();
let listCollapseDefaultsApplied = false;

function loadListRange() {
  try {
    const raw = JSON.parse(localStorage.getItem(LIST_RANGE_STORAGE_KEY) || "{}");
    return {
      from: typeof raw.from === "string" ? raw.from : "",
      to: typeof raw.to === "string" ? raw.to : "",
    };
  } catch {
    return { from: "", to: "" };
  }
}

function saveListRange() {
  localStorage.setItem(
    LIST_RANGE_STORAGE_KEY,
    JSON.stringify({
      from: ($("listFromDate")?.value || "").trim(),
      to: ($("listToDate")?.value || "").trim(),
    }),
  );
}

function applyListRangeToInputs() {
  const range = loadListRange();
  if ($("listFromDate")) $("listFromDate").value = range.from;
  if ($("listToDate")) $("listToDate").value = range.to;
}

function getFilteredSchedules({ ignoreRange = false } = {}) {
  const query = ($("searchInput")?.value || "").trim().toLowerCase();
  const from = ($("listFromDate")?.value || "").trim();
  const to = ($("listToDate")?.value || "").trim();
  const applyCalendarFilters = viewMode === "calendar";
  let shown = schedules.filter((item) => {
    if (query) {
      const searchable =
        `${item.scheduleTitle} ${item.accessoryInfo} ${item.sermonSpeaker} ${item.preaparationNote}`.toLowerCase();
      if (!searchable.includes(query)) return false;
    }
    if (!ignoreRange) {
      if (from && item.scheduleDate < from) return false;
      if (to && item.scheduleDate > to) return false;
    }
    if (applyCalendarFilters && !matchesCalendarViewFilter(item)) return false;
    return true;
  });
  shown.sort(
    (a, b) =>
      a.scheduleDate.localeCompare(b.scheduleDate) ||
      compareSchedulesWithinDay(a, b),
  );
  return shown;
}

function applyListCollapseDefaults(dates) {
  if (listCollapseDefaultsApplied) return;
  const todayKey = dateInputValue(new Date());
  listCollapsedDates.clear();
  for (const day of dates) {
    if (day < todayKey) listCollapsedDates.add(day);
  }
  listCollapseDefaultsApplied = true;
}

function setViewMode(mode) {
  viewMode = mode === "calendar" ? "calendar" : "list";
  localStorage.setItem(VIEW_STORAGE_KEY, viewMode);
  document.querySelectorAll(".view-mode").forEach((button) => {
    const active = button.dataset.view === viewMode;
    button.classList.toggle("active", active);
    button.setAttribute("aria-selected", active ? "true" : "false");
  });
  $("scheduleList").classList.toggle("hidden", viewMode !== "list");
  $("calendarView").classList.toggle("hidden", viewMode !== "calendar");
  $("listRangeFilters")?.classList.toggle("hidden", viewMode !== "list");
  if (viewMode === "list") listCollapseDefaultsApplied = false;
  updateClearCalendarViewPreview();
  render();
}

function render() {
  updateMonthlyScheduleButtonLabel();
  updateMonthlyOfficialDocsButtonLabel();
  if (viewMode === "calendar") renderCalendar();
  else renderList();
}

function renderList() {
  const shown = getFilteredSchedules();
  if (!shown.length) {
    $("scheduleList").innerHTML =
      '<div class="empty"><strong>조건에 맞는 일정이 없습니다.</strong>필터를 바꾸거나 새 일정을 추가해 보세요.</div>';
    return;
  }
  const dates = [...new Set(shown.map((item) => item.scheduleDate))];
  applyListCollapseDefaults(dates);
  const parts = [];
  let lastDate = null;
  for (const item of shown) {
    if (item.scheduleDate !== lastDate) {
      if (lastDate !== null) parts.push(`</div></div>`);
      lastDate = item.scheduleDate;
      const meta = formatDate(lastDate);
      const count = shown.filter((entry) => entry.scheduleDate === lastDate).length;
      const collapsed = listCollapsedDates.has(lastDate);
      parts.push(`<div class="date-group${collapsed ? " is-collapsed" : ""}" data-date-group="${lastDate}">
        <div class="date-divider-row">
          <button type="button" class="date-collapse-toggle" data-collapse-day="${lastDate}" aria-expanded="${collapsed ? "false" : "true"}" title="${collapsed ? "펼치기" : "접기"}">${collapsed ? "▶" : "▼"}</button>
          <button type="button" class="date-divider" data-day="${lastDate}">
            <span class="date-divider-main">${lastDate} <em>${meta.week}</em></span>
            <span class="date-divider-count">${count}건 보기</span>
          </button>
        </div>
        <div class="date-group-cards">`);
    }
    parts.push(renderCard(item));
  }
  if (lastDate !== null) parts.push(`</div></div>`);
  $("scheduleList").innerHTML = parts.join("");
}

function consecutiveEventKey(item) {
  return `${String(item.scheduleTitle || "").trim()}\u0001${String(item.sermonSpeaker || "").trim()}`;
}

/** 음력 명절 당일(설·추석·부처님오신날). 연휴·대체는 아래에서 계산. */
const KR_LUNAR_CORE = {
  2024: { seollal: "2024-02-10", chuseok: "2024-09-17", buddha: "2024-05-15" },
  2025: { seollal: "2025-01-29", chuseok: "2025-10-06", buddha: "2025-05-05" },
  2026: { seollal: "2026-02-17", chuseok: "2026-09-25", buddha: "2026-05-24" },
  2027: { seollal: "2027-02-07", chuseok: "2027-09-15", buddha: "2027-05-13" },
  2028: { seollal: "2028-01-27", chuseok: "2028-10-03", buddha: "2028-05-02" },
  2029: { seollal: "2029-02-13", chuseok: "2029-09-22", buddha: "2029-05-20" },
  2030: { seollal: "2030-02-03", chuseok: "2030-09-12", buddha: "2030-05-09" },
  2031: { seollal: "2031-01-23", chuseok: "2031-10-01", buddha: "2031-05-28" },
  2032: { seollal: "2032-02-11", chuseok: "2032-09-19", buddha: "2032-05-16" },
  2033: { seollal: "2033-01-31", chuseok: "2033-09-08", buddha: "2033-05-06" },
  2034: { seollal: "2034-02-19", chuseok: "2034-09-27", buddha: "2034-05-25" },
  2035: { seollal: "2035-02-08", chuseok: "2035-09-16", buddha: "2035-05-15" },
};

const koreanHolidayCache = new Map();

function parseIsoDateLocal(iso) {
  const m = String(iso || "").match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!m) return null;
  return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

function shiftIsoDate(iso, days) {
  const d = parseIsoDateLocal(iso);
  if (!d) return "";
  d.setDate(d.getDate() + days);
  return dateInputValue(d);
}

function addKoreanHoliday(map, iso, name) {
  if (!iso || !name) return;
  const prev = map.get(iso);
  if (!prev) {
    map.set(iso, name);
    return;
  }
  if (prev === name || prev.includes(name)) return;
  map.set(iso, `${prev}·${name}`);
}

function nextOpenHolidayDay(map, fromIso) {
  let cursor = fromIso;
  for (let i = 0; i < 14; i += 1) {
    cursor = shiftIsoDate(cursor, 1);
    const d = parseIsoDateLocal(cursor);
    if (!d) return "";
    const dow = d.getDay();
    if (dow === 0 || dow === 6) continue;
    if (map.has(cursor)) continue;
    return cursor;
  }
  return "";
}

/** 해당 연도 관공서 공휴일(설·추석 연휴·대체 포함) */
function buildKoreanHolidays(year) {
  if (koreanHolidayCache.has(year)) return koreanHolidayCache.get(year);
  const map = new Map();
  const y = Number(year);
  if (!Number.isFinite(y)) {
    koreanHolidayCache.set(year, map);
    return map;
  }

  const fixed = [
    [1, 1, "신정"],
    [3, 1, "삼일절"],
    [5, 5, "어린이날"],
    [6, 6, "현충일"],
    [8, 15, "광복절"],
    [10, 3, "개천절"],
    [10, 9, "한글날"],
    [12, 25, "성탄절"],
  ];
  if (y >= 2026) fixed.splice(2, 0, [5, 1, "근로자의 날"]);

  for (const [month, day, name] of fixed) {
    addKoreanHoliday(
      map,
      `${y}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`,
      name,
    );
  }

  const lunar = KR_LUNAR_CORE[y];
  if (lunar?.seollal) {
    addKoreanHoliday(map, shiftIsoDate(lunar.seollal, -1), "설날 전날");
    addKoreanHoliday(map, lunar.seollal, "설날");
    addKoreanHoliday(map, shiftIsoDate(lunar.seollal, 1), "설날 다음날");
  }
  if (lunar?.chuseok) {
    addKoreanHoliday(map, shiftIsoDate(lunar.chuseok, -1), "추석 전날");
    addKoreanHoliday(map, lunar.chuseok, "추석");
    addKoreanHoliday(map, shiftIsoDate(lunar.chuseok, 1), "추석 다음날");
  }
  if (lunar?.buddha) addKoreanHoliday(map, lunar.buddha, "부처님오신날");

  const addFestivalSubstitute = (mainIso) => {
    if (!mainIso) return;
    const span = [
      shiftIsoDate(mainIso, -1),
      mainIso,
      shiftIsoDate(mainIso, 1),
    ];
    const overlapsSunday = span.some((iso) => {
      const d = parseIsoDateLocal(iso);
      return d && d.getDay() === 0;
    });
    if (!overlapsSunday) return;
    const alt = nextOpenHolidayDay(map, span[2]);
    if (alt) addKoreanHoliday(map, alt, "대체공휴일");
  };
  addFestivalSubstitute(lunar?.seollal);
  addFestivalSubstitute(lunar?.chuseok);

  // 그 외 공휴일이 토·일과 겹치면 대체
  const weekendSensitive = new Set([
    "삼일절",
    "어린이날",
    "부처님오신날",
    "광복절",
    "개천절",
    "한글날",
    "성탄절",
    "근로자의 날",
  ]);
  for (const [iso, name] of [...map.entries()]) {
    if (!weekendSensitive.has(name)) continue;
    const d = parseIsoDateLocal(iso);
    if (!d) continue;
    const dow = d.getDay();
    if (dow !== 0 && dow !== 6) continue;
    const alt = nextOpenHolidayDay(map, iso);
    if (alt) addKoreanHoliday(map, alt, `${name} 대체`);
  }

  koreanHolidayCache.set(year, map);
  return map;
}

function holidaysAroundMonth(year, monthIndex) {
  const map = new Map();
  for (const y of [year - 1, year, year + 1]) {
    for (const [iso, name] of buildKoreanHolidays(y)) {
      map.set(iso, name);
    }
  }
  return map;
}

function getHolidayName(dateKey, holidayMap) {
  return holidayMap?.get(dateKey) || "";
}

function buildWeekEventSpans(weekDateKeys, byDate) {
  const tokens = [];
  weekDateKeys.forEach((dateKey, dayIdx) => {
    for (const item of byDate.get(dateKey) || []) {
      tokens.push({ item, dayIdx, dateKey, used: false });
    }
  });
  tokens.sort(
    (a, b) => a.dayIdx - b.dayIdx || compareSchedulesWithinDay(a.item, b.item),
  );
  const spans = [];
  for (const token of tokens) {
    if (token.used) continue;
    token.used = true;
    const key = consecutiveEventKey(token.item);
    const run = [token];
    for (let day = token.dayIdx + 1; day < 7; day += 1) {
      const next = tokens.find(
        (entry) =>
          !entry.used &&
          entry.dayIdx === day &&
          consecutiveEventKey(entry.item) === key,
      );
      if (!next) break;
      next.used = true;
      run.push(next);
    }
    spans.push({
      start: run[0].dayIdx,
      end: run[run.length - 1].dayIdx,
      items: run.map((entry) => entry.item),
      dateKeys: run.map((entry) => entry.dateKey),
    });
  }
  const sorted = [...spans].sort(
    (a, b) =>
      a.start - b.start ||
      b.end - b.start - (a.end - a.start) ||
      compareSchedulesWithinDay(a.items[0], b.items[0]),
  );
  const lanes = [];
  for (const span of sorted) {
    let laneIdx = lanes.findIndex((laneSpans) =>
      laneSpans.every((other) => other.end < span.start || span.end < other.start),
    );
    if (laneIdx < 0) {
      lanes.push([span]);
      span.lane = lanes.length - 1;
    } else {
      lanes[laneIdx].push(span);
      span.lane = laneIdx;
    }
  }
  return { spans, laneCount: lanes.length };
}

function calendarEventButtonHtml(item, { dateKey, spanDays, gridColumn, spanDateKeys }) {
  const display = resolveEventDisplayColor(item);
  const colorStyle = scheduleColorStyle(item);
  const hasPrep = (item.preparation || []).length > 0;
  const prepClass = hasPrep ? "has-prep" : "no-prep";
  const noneClass = display.none ? " is-none" : "";
  const spanClass = spanDays > 1 ? " is-span" : "";
  const selected = selectedExportIds.has(item.id);
  const datesAttr = (spanDateKeys || [dateKey]).join(",");
  const selectAttr = exportSelectMode
    ? `data-export-select="${item.id}"`
    : `data-day="${dateKey}" data-span-dates="${datesAttr}"`;
  const misc = loadMiscSettings();
  const period =
    misc.showPeriodDates ? findConsecutivePeriod(item) : null;
  const periodPrefix = period
    ? formatPeriodPrefix(period.start, period.end)
    : "";
  const periodHtml = periodPrefix
    ? `<span class="calendar-event-period">${escapeHtml(periodPrefix)}</span>`
    : "";
  // 시간은 달력에 표시하지 않음(정렬은 compareSchedulesWithinDay 유지)
  return `<button type="button" class="calendar-event ${prepClass}${selected ? " is-export-selected" : ""}${noneClass}${spanClass}" ${selectAttr} style="${colorStyle};grid-column:${gridColumn} / span ${spanDays}" title="${escapeHtml(periodPrefix ? `${periodPrefix} ` : "")}${escapeHtml(item.scheduleTitle)}${item.sermonSpeaker ? ` · ${escapeHtml(item.sermonSpeaker)}` : ""}">
    <span class="calendar-event-heading">
      ${periodHtml}<span class="calendar-event-title">${escapeHtml(item.scheduleTitle)}</span>
      ${item.sermonSpeaker ? `<span class="calendar-event-speaker">${escapeHtml(item.sermonSpeaker)}</span>` : ""}
    </span>
  </button>`;
}

function syncCalendarWeekHeights() {
  const grid = $("calendarGrid");
  if (!grid) return;
  for (const week of grid.querySelectorAll(".calendar-week")) {
    const lanes = week.querySelector(".calendar-week-lanes");
    const head = week.querySelector(".calendar-day-head");
    const headBottom = head
      ? head.offsetTop + head.offsetHeight + 4
      : 28;
    if (lanes) lanes.style.top = `${headBottom}px`;
    const lanesHeight = lanes ? lanes.offsetHeight : 0;
    const total = Math.max(64, headBottom + lanesHeight + 6);
    week.style.setProperty("--week-body-height", `${total}px`);
  }
}

function renderCalendar() {
  const year = calendarMonth.getFullYear();
  const month = calendarMonth.getMonth();
  $("calendarTitle").textContent = `${year}년 ${month + 1}월`;
  const shown = getFilteredSchedules({ ignoreRange: true });
  const byDate = new Map();
  for (const item of shown) {
    if (!byDate.has(item.scheduleDate)) byDate.set(item.scheduleDate, []);
    byDate.get(item.scheduleDate).push(item);
  }
  for (const items of byDate.values()) {
    items.sort(compareSchedulesWithinDay);
  }
  const firstDay = new Date(year, month, 1);
  const startOffset = firstDay.getDay();
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const todayKey = dateInputValue(new Date());
  const dates = [];
  for (let i = 0; i < startOffset; i += 1) {
    dates.push({
      dateObj: new Date(year, month, 1 - (startOffset - i)),
      outside: true,
    });
  }
  for (let day = 1; day <= daysInMonth; day += 1) {
    dates.push({ dateObj: new Date(year, month, day), outside: false });
  }
  let trailing = 1;
  while (dates.length % 7 !== 0) {
    dates.push({ dateObj: new Date(year, month + 1, trailing), outside: true });
    trailing += 1;
  }

  const holidayMap = holidaysAroundMonth(year, month);
  const weekHtml = [];
  for (let weekStart = 0; weekStart < dates.length; weekStart += 7) {
    const slice = dates.slice(weekStart, weekStart + 7);
    const weekKeys = slice.map((entry) => dateInputValue(entry.dateObj));
    const { spans, laneCount } = buildWeekEventSpans(weekKeys, byDate);
    const cells = slice
      .map((entry, dayIdx) => {
        const dateKey = weekKeys[dayIdx];
        const items = byDate.get(dateKey) || [];
        const isToday = dateKey === todayKey;
        const dow = entry.dateObj.getDay();
        const holidayName = getHolidayName(dateKey, holidayMap);
        const weekendClass =
          dow === 0 ? " is-sunday" : dow === 6 ? " is-saturday" : "";
        const holidayClass = holidayName ? " is-holiday" : "";
        const headTitle = holidayName
          ? `${holidayName} · 이날 일정 보기`
          : "이날 일정 보기";
        return `<div class="calendar-cell${entry.outside ? " is-outside" : ""}${isToday ? " is-today" : ""}${items.length ? " has-events" : ""}${weekendClass}${holidayClass}" data-date="${dateKey}">
      <button type="button" class="calendar-day-head" data-day="${dateKey}" title="${escapeHtml(headTitle)}">
        <span class="calendar-day-head-main">
          <span class="calendar-day-number">${entry.dateObj.getDate()}</span>
          ${holidayName ? `<span class="calendar-day-holiday">${escapeHtml(holidayName)}</span>` : ""}
        </span>
        ${items.length ? `<span class="calendar-day-count">${items.length}</span>` : ""}
      </button>
    </div>`;
      })
      .join("");
    const laneRows = [];
    for (let lane = 0; lane < laneCount; lane += 1) {
      const buttons = spans
        .filter((span) => span.lane === lane)
        .map((span) =>
          calendarEventButtonHtml(span.items[0], {
            dateKey: span.dateKeys[0],
            spanDateKeys: span.dateKeys,
            spanDays: span.end - span.start + 1,
            gridColumn: span.start + 1,
          }),
        )
        .join("");
      laneRows.push(`<div class="calendar-lane">${buttons}</div>`);
    }
    weekHtml.push(
      `<div class="calendar-week" style="--lane-count:${Math.max(laneCount, 1)}">${cells}<div class="calendar-week-lanes">${laneRows.join("")}</div></div>`,
    );
  }
  $("calendarGrid").innerHTML = weekHtml.join("");
  renderCalendarFilterChips();
  syncCalendarWeekHeights();
  requestAnimationFrame(syncCalendarWeekHeights);
}

function showDaySchedules(dateKey) {
  openDayDetail(dateKey);
}

// 이어진 막대는 클릭 X가 걸친 칸 중 가장 오른쪽 날짜를 씀
function dateKeyFromCalendarSpanClick(event, fallbackDateKey) {
  const button = event.target.closest(".calendar-event");
  const spanDates = String(button?.dataset.spanDates || fallbackDateKey || "")
    .split(",")
    .map((key) => key.trim())
    .filter(Boolean);
  const lastDate = spanDates[spanDates.length - 1] || fallbackDateKey;
  const week = event.target.closest(".calendar-week");
  if (!week) return lastDate;
  const x = event.clientX;
  let hit = null;
  for (const cell of week.querySelectorAll(".calendar-cell[data-date]")) {
    if (x >= cell.getBoundingClientRect().left) hit = cell.dataset.date;
  }
  if (!hit) return lastDate;
  if (spanDates.includes(hit)) return hit;
  const firstDate = spanDates[0] || lastDate;
  if (hit < firstDate) return firstDate;
  return lastDate;
}

function openDayDetail(dateKey) {
  const items = getFilteredSchedules({ ignoreRange: true })
    .filter((item) => item.scheduleDate === dateKey)
    .sort(compareSchedulesWithinDay);
  const meta = formatDate(dateKey);
  $("dayDetailTitle").textContent = `${dateKey} ${meta.week}`;
  $("dayDetailAdd").dataset.date = dateKey;
  if (!items.length) {
    $("dayDetailList").innerHTML =
      '<div class="day-detail-empty">이 날짜에 등록된 일정이 없습니다.</div>';
  } else {
    $("dayDetailList").innerHTML = items
      .map((item) => {
        const prep = item.preparation || [];
        const hasPrep = prep.length > 0;
        const display = resolveEventDisplayColor(item);
        const selected = selectedExportIds.has(item.id);
        return `<article class="day-detail-item${selected ? " is-export-selected" : ""}${display.none ? " is-none" : ""}" style="${scheduleColorStyle(item)}">
          ${
            exportSelectMode
              ? `<label class="export-select-check"><input type="checkbox" data-export-select="${item.id}" ${selected ? "checked" : ""} aria-label="내보내기 선택" /></label>`
              : `<div class="day-detail-time">${formatTime(item.startTime)}</div>`
          }
          <div class="day-detail-body">
            <strong>${escapeHtml(item.scheduleTitle)}</strong>
            <div class="meta">
              ${exportSelectMode ? `<span>${formatTime(item.startTime)}</span>` : ""}
              <span>${escapeHtml(PLACE_LABELS[item.place] || item.place || "장소 미정")}</span>
              ${item.sermonSpeaker ? `<span>${escapeHtml(item.sermonSpeaker)}</span>` : ""}
              ${!hasPrep ? '<span class="tag">● 준비 없음</span>' : ""}
            </div>
            ${item.accessoryInfo ? `<div class="card-note">${escapeHtml(item.accessoryInfo)}</div>` : ""}
            <div class="meta" style="margin-top:6px">${
              prep.length
                ? prep
                    .map(
                      (value) =>
                        `<span class="tag orange">● ${escapeHtml(PREP_LABELS[value] || value)}</span>`,
                    )
                    .join("")
                : ""
            }</div>
            ${item.preaparationNote ? `<div class="card-note">${escapeHtml(item.preaparationNote)}</div>` : ""}
          </div>
          <div class="day-detail-actions-inline">
            ${
              exportSelectMode
                ? ""
                : `<button type="button" class="button soft day-doc-button${item.officialDocument ? "" : " hidden"}" data-doc="${escapeHtml(item.officialDocument || "")}" ${item.officialDocument ? "" : "disabled"}>
              공문 보기
            </button>
            <button type="button" class="edit-button" data-edit="${item.id}" aria-label="일정 수정">↗</button>
            <button type="button" class="delete-card-button" data-delete="${item.id}" aria-label="일정 삭제">×</button>`
            }
          </div>
        </article>`;
      })
      .join("");
  }
  $("dayDetailDialog").showModal();
}

function closeScheduleDialog() {
  $("scheduleDialog").close();
}

function setPlaceValue(value) {
  $("place").value = value || "None";
  document.querySelectorAll("[data-place]").forEach((button) => {
    button.classList.toggle("active", button.dataset.place === $("place").value);
  });
}

function setStartTimeValue(value, { mode } = {}) {
  const hidden = $("startTime");
  const hourSel = $("startHour");
  const minuteSel = $("startMinute");
  const raw =
    value === "" || value == null ? "" : String(value).padStart(4, "0");

  if (!hidden || !hourSel || !minuteSel) return;

  if (!raw) {
    hidden.value = "";
    hourSel.value = "";
    minuteSel.value = "";
    setTimePickerMode(mode || "unset");
    return;
  }

  let hour = Number(raw.slice(0, 2));
  let minute = Number(raw.slice(2, 4));
  if (!Number.isFinite(hour)) hour = 6;
  if (!Number.isFinite(minute)) minute = 0;
  minute = Math.min(50, Math.max(0, Math.round(minute / 10) * 10));
  ensureTimeHourOption(hour);

  hidden.value = `${String(hour).padStart(2, "0")}${String(minute).padStart(2, "0")}`;
  hourSel.value = String(hour).padStart(2, "0");
  minuteSel.value = String(minute).padStart(2, "0");

  const nextMode =
    mode ||
    (QUICK_TIMES.some((item) => String(item.value).padStart(4, "0") === hidden.value)
      ? "quick"
      : "custom");
  setTimePickerMode(nextMode);
}

function setTimePickerMode(mode) {
  timePickerMode = mode === "custom" || mode === "quick" ? mode : "unset";
  const customPanel = $("timeCustomPanel");
  const unsetButton = $("timeUnsetButton");
  const customButton = $("timeCustomButton");
  const hourSel = $("startHour");
  const minuteSel = $("startMinute");

  unsetButton?.classList.toggle("active", timePickerMode === "unset");
  customButton?.classList.toggle("active", timePickerMode === "custom");
  customPanel?.classList.toggle("hidden", timePickerMode !== "custom");

  if (hourSel) hourSel.disabled = timePickerMode !== "custom";
  if (minuteSel) minuteSel.disabled = timePickerMode !== "custom";

  const current = ($("startTime")?.value || "").padStart(4, "0");
  document.querySelectorAll("[data-quick-time]").forEach((button) => {
    const active =
      timePickerMode !== "unset" &&
      button.dataset.quickTime === current &&
      (timePickerMode === "quick" ||
        QUICK_TIMES.some(
          (item) => String(item.value).padStart(4, "0") === current,
        ));
    button.classList.toggle(
      "active",
      timePickerMode === "quick" && button.dataset.quickTime === current,
    );
  });
}

function ensureTimeHourOption(hour) {
  const hourSel = $("startHour");
  if (!hourSel) return;
  const value = String(hour).padStart(2, "0");
  if ([...hourSel.options].some((option) => option.value === value)) return;
  const option = document.createElement("option");
  option.value = value;
  option.textContent = formatHourLabel(hour);
  hourSel.append(option);
}

function formatHourLabel(hour) {
  const period = hour >= 12 ? "오후" : "오전";
  const display = hour % 12 || 12;
  return `${period} ${String(display).padStart(2, "0")}시`;
}

function composeStartTimeFromPicker() {
  const hour = $("startHour")?.value || "";
  const minute = $("startMinute")?.value || "";
  if (!hour || !minute) {
    if ($("startTime")) $("startTime").value = "";
    return;
  }
  setStartTimeValue(`${hour}${minute}`, { mode: "custom" });
}

function initTimePickerControls() {
  const hourSel = $("startHour");
  const minuteSel = $("startMinute");
  if (!hourSel || !minuteSel) return;

  for (let hour = 6; hour <= 22; hour += 1) {
    const option = document.createElement("option");
    option.value = String(hour).padStart(2, "0");
    option.textContent = formatHourLabel(hour);
    hourSel.append(option);
  }
  for (let minute = 0; minute < 60; minute += 10) {
    const option = document.createElement("option");
    option.value = String(minute).padStart(2, "0");
    option.textContent = `${String(minute).padStart(2, "0")}분`;
    minuteSel.append(option);
  }

  if ($("timeQuickButtons")) {
    $("timeQuickButtons").innerHTML = QUICK_TIMES.map((item) => {
      const raw = String(item.value).padStart(4, "0");
      return `<button type="button" class="choice-chip" data-quick-time="${raw}">${item.label}</button>`;
    }).join("");
  }

  hourSel.onchange = () => {
    if (!minuteSel.value) minuteSel.value = "00";
    composeStartTimeFromPicker();
  };
  minuteSel.onchange = () => {
    if (!hourSel.value) hourSel.value = "10";
    composeStartTimeFromPicker();
  };
  $("timeUnsetButton").onclick = () => setStartTimeValue("", { mode: "unset" });
  $("timeCustomButton").onclick = () => {
    const current = $("startTime")?.value;
    if (current) setStartTimeValue(current, { mode: "custom" });
    else {
      setStartTimeValue("1000", { mode: "custom" });
    }
  };
  $("timeQuickButtons").onclick = (event) => {
    const button = event.target.closest("[data-quick-time]");
    if (!button) return;
    setStartTimeValue(button.dataset.quickTime, { mode: "quick" });
  };
  setStartTimeValue("", { mode: "unset" });
}

function findSameSchedules(item) {
  if (!item) return [];
  // 제목·강사가 모두 비면 동일 연결로 보지 않음
  if (!String(item.scheduleTitle || "").trim() && !String(item.sermonSpeaker || "").trim()) {
    return [item];
  }
  const key = consecutiveEventKey(item);
  return schedules.filter((entry) => consecutiveEventKey(entry) === key);
}

/** 같은 제목·강사 연결 일정이 있으면 전체/해당일 선택. false면 취소. */
function removeScheduleWithLinkPrompt(id) {
  const item = schedules.find((entry) => entry.id === id);
  if (!item) return false;
  if (!confirm("이 일정을 삭제할까요?")) return false;
  const linked = findSameSchedules(item);
  let ids = [id];
  if (linked.length > 1) {
    const deleteAll = confirm(
      `같은 제목·강사 일정이 다른 날짜에 ${linked.length - 1}개 더 있습니다.\n확인 = 모든 날짜에서 삭제\n취소 = 이 날짜만 삭제`,
    );
    if (deleteAll) ids = linked.map((entry) => entry.id);
  }
  const idSet = new Set(ids);
  schedules = schedules.filter((entry) => !idSet.has(entry.id));
  saveLocal();
  render();
  showToast(
    ids.length > 1
      ? `같은 일정 ${ids.length}건을 삭제했습니다.`
      : "일정을 삭제했습니다.",
  );
  void syncOfficialDocumentIndex();
  return true;
}

function deleteCurrentSchedule() {
  if (!editingId) return;
  if (!removeScheduleWithLinkPrompt(editingId)) return;
  closeScheduleDialog();
}

function updateOfficialDocumentUi() {
  const hasDoc = Boolean(pendingOfficialDocument);
  const status = $("officialDocStatus");
  const summary = $("officialDocSummary");
  const block = $("officialDocBlock");
  if (status) {
    status.textContent = hasDoc
      ? `연결됨: ${pendingOfficialDocument}`
      : "연결된 공문 없음";
  }
  if (summary) {
    summary.textContent = hasDoc ? pendingOfficialDocument : "연결 없음";
  }
  if (block) {
    const forceOpen = hasDoc;
    if (forceOpen) {
      block.classList.remove("is-collapsed");
      $("officialDocToggle")?.setAttribute("aria-expanded", "true");
    } else if (!block.dataset.userOpened) {
      block.classList.add("is-collapsed");
      $("officialDocToggle")?.setAttribute("aria-expanded", "false");
    }
  }
  if ($("officialDocViewButton")) $("officialDocViewButton").disabled = !hasDoc;
  if ($("dialogOfficialDocViewButton")) {
    $("dialogOfficialDocViewButton").classList.toggle("hidden", !hasDoc);
    $("dialogOfficialDocViewButton").disabled = !hasDoc;
  }
  if ($("officialDocClearButton")) {
    $("officialDocClearButton").classList.toggle("hidden", !hasDoc);
  }
  const canUseNas = bridgeAlive && bridgeConnected && Boolean(linkedFileName);
  if ($("officialDocUploadButton")) $("officialDocUploadButton").disabled = !canUseNas;
  if ($("officialDocLinkButton")) $("officialDocLinkButton").disabled = !canUseNas;
}

function setOfficialDocExpanded(expanded) {
  const block = $("officialDocBlock");
  if (!block) return;
  block.classList.toggle("is-collapsed", !expanded);
  $("officialDocToggle")?.setAttribute("aria-expanded", expanded ? "true" : "false");
  if (expanded) block.dataset.userOpened = "1";
  else delete block.dataset.userOpened;
}

function appendSpeakerPaSuffix() {
  const input = $("speaker");
  if (!input) return;
  const value = input.value.trimEnd();
  if (/PA$/i.test(value)) {
    showToast("이미 이름 끝에 PA가 있습니다.");
    return;
  }
  input.value = `${value}PA`;
  input.focus();
  input.setSelectionRange(input.value.length, input.value.length);
}

function normalizeSpeakerRoster(raw) {
  // 등록 순서 유지 (가나다 강제 정렬하지 않음)
  const clean = (list) => {
    const seen = new Set();
    const out = [];
    for (const value of Array.isArray(list) ? list : []) {
      const name = String(value || "").trim();
      if (!name || seen.has(name)) continue;
      seen.add(name);
      out.push(name);
    }
    return out;
  };
  return {
    gwangju: clean([...(raw?.gwangju || []), ...(raw?.names || []), ...(raw?.external || [])]),
  };
}

function speakerRosterPayload(roster) {
  const next = normalizeSpeakerRoster(roster);
  return {
    version: 1,
    updatedAt: new Date().toISOString(),
    gwangju: next.gwangju,
  };
}

function loadLocalSpeakerRoster() {
  try {
    return normalizeSpeakerRoster(
      JSON.parse(localStorage.getItem(SPEAKER_ROSTER_STORAGE_KEY) || "{}"),
    );
  } catch {
    return normalizeSpeakerRoster({});
  }
}

function saveLocalSpeakerRoster(roster) {
  const next = normalizeSpeakerRoster(roster);
  localStorage.setItem(
    SPEAKER_ROSTER_STORAGE_KEY,
    JSON.stringify({ gwangju: next.gwangju }),
  );
  return next;
}

function loadSpeakerRoster() {
  if (speakerRosterCache) {
    return {
      gwangju: [...speakerRosterCache.gwangju],
    };
  }
  speakerRosterCache = loadLocalSpeakerRoster();
  return {
    gwangju: [...speakerRosterCache.gwangju],
  };
}

function updateSpeakerRosterSyncHint() {
  const el = $("speakerRosterSyncHint");
  if (!el) return;
  if (bridgeAlive && bridgeConnected && linkedFileName) {
    el.textContent = speakerRosterDirty
      ? "NAS 공유 명단 · 저장 중…"
      : "NAS에 공유되는 명단입니다. 모든 PC에서 동일하게 사용합니다.";
  } else {
    el.textContent =
      "로컬에만 저장됩니다. NAS 연결 시 공유 파일로 올라갑니다. ↑↓로 표시 순서를 바꿀 수 있습니다.";
  }
}

function mergeSpeakerRoster(preferred, other, deletedNames = speakerRosterDeletedNames) {
  const a = normalizeSpeakerRoster(preferred);
  const b = normalizeSpeakerRoster(other);
  const seen = new Set();
  const gwangju = [];
  for (const name of [...a.gwangju, ...b.gwangju]) {
    if (seen.has(name) || deletedNames.has(name)) continue;
    seen.add(name);
    gwangju.push(name);
  }
  return { gwangju };
}

async function readSpeakerRosterFromNas() {
  const tryRead = async (fileName) => {
    const response = await fetch(
      `${API_BASE}/api/file?name=` + encodeURIComponent(fileName),
      { cache: "no-store" },
    );
    if (!response.ok) {
      if (response.status === 400 || response.status === 404) return null;
      throw new Error(await readErrorMessage(response));
    }
    const revision = response.headers.get("X-File-Revision") || "";
    let raw;
    try {
      raw = await response.json();
    } catch {
      raw = {};
    }
    return { roster: normalizeSpeakerRoster(raw), revision };
  };
  const primary = await tryRead(SPEAKER_ROSTER_FILE);
  if (primary) return primary;
  const legacy = await tryRead("speaker-roster.json");
  if (legacy) return legacy;
  return { roster: normalizeSpeakerRoster({}), revision: "" };
}

async function writeSpeakerRosterToNas(roster, expectedRevision) {
  const headers = { "Content-Type": "application/json" };
  if (expectedRevision) headers["X-Expected-Revision"] = expectedRevision;
  const response = await fetch(
    `${API_BASE}/api/file?name=` + encodeURIComponent(SPEAKER_ROSTER_FILE),
    {
      method: "PUT",
      headers,
      body: JSON.stringify(speakerRosterPayload(roster), null, 2),
    },
  );
  if (response.status === 409) {
    const conflict = await response.json().catch(() => ({}));
    const err = new Error("충돌");
    err.conflict = conflict;
    throw err;
  }
  if (!response.ok) throw new Error(await readErrorMessage(response));
  const result = await response.json().catch(() => ({}));
  return result.revision || "";
}

async function refreshSpeakerRosterFromNas({ quiet = true } = {}) {
  try {
    if (speakerRosterDirty) {
      updateSpeakerRosterSyncHint();
      return;
    }
    if (!(bridgeAlive && bridgeConnected && linkedFileName)) {
      speakerRosterCache = loadLocalSpeakerRoster();
      speakerRosterRevision = "";
      updateSpeakerRosterSyncHint();
      return;
    }
    const { roster: remote, revision } = await readSpeakerRosterFromNas();
    speakerRosterRevision = revision;
    const local = loadLocalSpeakerRoster();
    const migrated = localStorage.getItem(SPEAKER_ROSTER_LOCAL_MIGRATED_KEY) === "1";
    const remoteEmpty = !remote.gwangju.length;
    const localEmpty = !local.gwangju.length;

    if (remoteEmpty && !localEmpty && !migrated) {
      speakerRosterCache = local;
      saveLocalSpeakerRoster(local);
      try {
        speakerRosterRevision = await writeSpeakerRosterToNas(local, revision);
        localStorage.setItem(SPEAKER_ROSTER_LOCAL_MIGRATED_KEY, "1");
        if (!quiet) showToast("로컬 명단을 NAS에 올렸습니다.");
      } catch (err) {
        if (!quiet) showToast(`명단 NAS 업로드 실패: ${err.message}`);
      }
    } else if (!remoteEmpty && !localEmpty && !migrated) {
      speakerRosterCache = mergeSpeakerRoster(local, remote);
      localStorage.setItem(SPEAKER_ROSTER_LOCAL_MIGRATED_KEY, "1");
      saveLocalSpeakerRoster(speakerRosterCache);
      try {
        speakerRosterRevision = await writeSpeakerRosterToNas(
          speakerRosterCache,
          revision,
        );
      } catch {
        /* keep merged locally */
      }
    } else {
      speakerRosterCache = remoteEmpty ? local : remote;
      saveLocalSpeakerRoster(speakerRosterCache);
      if (remoteEmpty && !localEmpty) {
        try {
          speakerRosterRevision = await writeSpeakerRosterToNas(
            speakerRosterCache,
            revision,
          );
        } catch {
          /* offline-ish */
        }
      }
    }
    speakerRosterDirty = false;
    updateSpeakerRosterSyncHint();
  } catch (err) {
    speakerRosterCache = loadLocalSpeakerRoster();
    updateSpeakerRosterSyncHint();
    if (!quiet) showToast(`명단 불러오기 실패: ${err.message}`);
  }
}

async function persistSpeakerRoster(roster, { toastMessage } = {}) {
  speakerRosterCache = normalizeSpeakerRoster(roster);
  saveLocalSpeakerRoster(speakerRosterCache);
  speakerRosterDirty = true;
  updateSpeakerRosterSyncHint();
  const snapshot = {
    gwangju: [...speakerRosterCache.gwangju],
  };

  if (!(bridgeAlive && bridgeConnected && linkedFileName)) {
    updateSpeakerRosterSyncHint();
    if (toastMessage) showToast(`${toastMessage} (로컬)`);
    return;
  }

  try {
    speakerRosterRevision = await writeSpeakerRosterToNas(
      snapshot,
      speakerRosterRevision,
    );
    const stillSame =
      JSON.stringify(speakerRosterCache.gwangju) === JSON.stringify(snapshot.gwangju);
    if (stillSame) {
      speakerRosterDirty = false;
      for (const name of [...speakerRosterDeletedNames]) {
        if (!snapshot.gwangju.includes(name)) speakerRosterDeletedNames.delete(name);
      }
    }
    updateSpeakerRosterSyncHint();
    if (toastMessage) showToast(toastMessage);
  } catch (err) {
    if (err.conflict) {
      try {
        const remoteRaw = err.conflict?.content;
        const remote = normalizeSpeakerRoster(
          typeof remoteRaw === "string" ? JSON.parse(remoteRaw) : remoteRaw,
        );
        speakerRosterRevision = err.conflict.revision || "";
        const merged = mergeSpeakerRoster(snapshot, remote);
        speakerRosterCache = merged;
        saveLocalSpeakerRoster(speakerRosterCache);
        const mergedSnap = { gwangju: [...merged.gwangju] };
        speakerRosterRevision = await writeSpeakerRosterToNas(
          mergedSnap,
          speakerRosterRevision,
        );
        speakerRosterDirty = false;
        for (const name of [...speakerRosterDeletedNames]) {
          if (!mergedSnap.gwangju.includes(name)) speakerRosterDeletedNames.delete(name);
        }
        updateSpeakerRosterSyncHint();
        showToast("다른 PC 명단과 병합 후 저장했습니다.");
        return;
      } catch (mergeErr) {
        speakerRosterDirty = true;
        updateSpeakerRosterSyncHint();
        showToast(`명단 충돌 해결 실패: ${mergeErr.message}`);
        return;
      }
    }
    speakerRosterDirty = true;
    showToast(`명단 NAS 저장 실패: ${err.message} (로컬에는 저장됨)`);
    updateSpeakerRosterSyncHint();
  }
}

function normalizeColorRules(raw) {
  const list = Array.isArray(raw)
    ? raw
    : Array.isArray(raw?.rules)
      ? raw.rules
      : [];
  const seen = new Set();
  const out = [];
  for (const entry of list) {
    if (!entry || typeof entry !== "object") continue;
    const pattern = String(entry.pattern || "").trim();
    if (!pattern) continue;
    let color = String(entry.color || "").trim();
    if (!SCHEDULE_COLORS.some((item) => item.id === color) && !isHexColor(color)) {
      color = DEFAULT_SCHEDULE_COLOR;
    }
    const id = String(entry.id || "").trim() || crypto.randomUUID();
    if (seen.has(id)) continue;
    seen.add(id);
    out.push({ id, pattern, color });
  }
  return out;
}

function colorRulesPayload(rules) {
  return {
    version: 1,
    updatedAt: new Date().toISOString(),
    rules: normalizeColorRules(rules),
    defaultAutoColor: loadColorAutoDefault(),
  };
}

function normalizeColorAutoDefault(value) {
  if (value == null || value === "" || value === "none") return null;
  const color = String(value).trim();
  if (SCHEDULE_COLORS.some((item) => item.id === color) || isHexColor(color)) {
    return isHexColor(color) ? color.toLowerCase() : color;
  }
  return null;
}

function loadColorAutoDefault() {
  if (colorAutoDefaultLoaded) return colorAutoDefault;
  try {
    const raw = localStorage.getItem(COLOR_AUTO_DEFAULT_STORAGE_KEY);
    if (raw == null || raw === "" || raw === "null") {
      colorAutoDefault = null;
    } else {
      colorAutoDefault = normalizeColorAutoDefault(JSON.parse(raw));
    }
  } catch {
    colorAutoDefault = null;
  }
  colorAutoDefaultLoaded = true;
  return colorAutoDefault;
}

function saveColorAutoDefault(value) {
  colorAutoDefault = normalizeColorAutoDefault(value);
  colorAutoDefaultLoaded = true;
  localStorage.setItem(
    COLOR_AUTO_DEFAULT_STORAGE_KEY,
    JSON.stringify(colorAutoDefault),
  );
  return colorAutoDefault;
}

function loadLocalColorRules() {
  try {
    const raw = JSON.parse(localStorage.getItem(COLOR_RULES_STORAGE_KEY) || "[]");
    if (raw && typeof raw === "object" && !Array.isArray(raw)) {
      if ("defaultAutoColor" in raw) {
        colorAutoDefault = normalizeColorAutoDefault(raw.defaultAutoColor);
        localStorage.setItem(
          COLOR_AUTO_DEFAULT_STORAGE_KEY,
          JSON.stringify(colorAutoDefault),
        );
      }
      return normalizeColorRules(raw);
    }
    return normalizeColorRules(raw);
  } catch {
    return [];
  }
}

function saveLocalColorRules(rules) {
  const next = normalizeColorRules(rules);
  localStorage.setItem(
    COLOR_RULES_STORAGE_KEY,
    JSON.stringify({
      rules: next,
      defaultAutoColor: loadColorAutoDefault(),
    }),
  );
  return next;
}

function loadColorRules() {
  if (colorRulesCache) return colorRulesCache;
  colorRulesCache = loadLocalColorRules();
  return colorRulesCache;
}

function updateColorRulesSyncHint() {
  const el = $("colorRulesSyncStatus");
  if (!el) return;
  if (bridgeAlive && bridgeConnected && linkedFileName) {
    el.textContent = colorRulesDirty
      ? "NAS 공유 색상 · 저장 중…"
      : "NAS에 공유되는 자동 색상 조건입니다. 모든 PC에서 동일하게 사용합니다.";
  } else {
    el.textContent =
      "로컬에만 저장됩니다. NAS 연결 시 공유 파일로 올라갑니다.";
  }
}

async function readColorRulesFromNas() {
  const tryRead = async (fileName) => {
    const response = await fetch(
      `${API_BASE}/api/file?name=` + encodeURIComponent(fileName),
      { cache: "no-store" },
    );
    if (!response.ok) {
      if (response.status === 400 || response.status === 404) return null;
      throw new Error(await readErrorMessage(response));
    }
    const revision = response.headers.get("X-File-Revision") || "";
    let raw;
    try {
      raw = await response.json();
    } catch {
      raw = [];
    }
    const defaultAutoColor =
      raw && typeof raw === "object" && !Array.isArray(raw)
        ? normalizeColorAutoDefault(raw.defaultAutoColor)
        : null;
    return { rules: normalizeColorRules(raw), revision, defaultAutoColor };
  };
  const primary = await tryRead(COLOR_RULES_FILE);
  if (primary) return primary;
  return { rules: [], revision: "", defaultAutoColor: null };
}

async function writeColorRulesToNas(rules, expectedRevision) {
  const headers = { "Content-Type": "application/json" };
  if (expectedRevision) headers["X-Expected-Revision"] = expectedRevision;
  const response = await fetch(
    `${API_BASE}/api/file?name=` + encodeURIComponent(COLOR_RULES_FILE),
    {
      method: "PUT",
      headers,
      body: JSON.stringify(colorRulesPayload(rules), null, 2),
    },
  );
  if (response.status === 409) {
    const conflict = await response.json().catch(() => ({}));
    const err = new Error("충돌");
    err.conflict = conflict;
    throw err;
  }
  if (!response.ok) throw new Error(await readErrorMessage(response));
  const result = await response.json().catch(() => ({}));
  return result.revision || "";
}

async function refreshColorRulesFromNas({ quiet = true } = {}) {
  try {
    if (colorRulesDirty) {
      updateColorRulesSyncHint();
      return;
    }
    if (!(bridgeAlive && bridgeConnected && linkedFileName)) {
      colorRulesCache = loadLocalColorRules();
      colorRulesRevision = "";
      updateColorRulesSyncHint();
      syncColorAutoDefaultControls();
      return;
    }
    const {
      rules: remote,
      revision,
      defaultAutoColor: remoteDefault,
    } = await readColorRulesFromNas();
    colorRulesRevision = revision;
    const local = loadLocalColorRules();
    if (!remote.length && local.length) {
      colorRulesCache = local;
      try {
        colorRulesRevision = await writeColorRulesToNas(local, revision);
      } catch {
        /* 로컬 유지 */
      }
    } else {
      colorRulesCache = remote.length ? remote : local;
      saveLocalColorRules(colorRulesCache);
      if (remoteDefault !== undefined) {
        saveColorAutoDefault(remoteDefault);
      }
    }
    colorRulesDirty = false;
    updateColorRulesSyncHint();
    syncColorAutoDefaultControls();
    render();
  } catch (err) {
    colorRulesCache = loadLocalColorRules();
    updateColorRulesSyncHint();
    syncColorAutoDefaultControls();
    if (!quiet) showToast(`색상 조건 불러오기 실패: ${err.message}`);
  }
}

async function persistColorRules(rules, { toastMessage } = {}) {
  colorRulesCache = normalizeColorRules(rules);
  saveLocalColorRules(colorRulesCache);
  colorRulesDirty = true;
  updateColorRulesSyncHint();
  render();
  if (!(bridgeAlive && bridgeConnected && linkedFileName)) {
    if (toastMessage) showToast(`${toastMessage} (로컬)`);
    return;
  }
  try {
    colorRulesRevision = await writeColorRulesToNas(
      colorRulesCache,
      colorRulesRevision,
    );
    colorRulesDirty = false;
    updateColorRulesSyncHint();
    if (toastMessage) showToast(toastMessage);
  } catch (err) {
    if (err.conflict) {
      try {
        colorRulesRevision = err.conflict.revision || "";
        colorRulesRevision = await writeColorRulesToNas(
          colorRulesCache,
          colorRulesRevision,
        );
        colorRulesDirty = false;
        updateColorRulesSyncHint();
        if (toastMessage) showToast(toastMessage);
        return;
      } catch (retryErr) {
        showToast(`색상 조건 충돌 저장 실패: ${retryErr.message}`);
        return;
      }
    }
    showToast(`색상 조건 NAS 저장 실패: ${err.message} (로컬에는 저장됨)`);
    updateColorRulesSyncHint();
  }
}

function addColorRule(pattern, color) {
  const trimmed = String(pattern || "").trim();
  if (!trimmed) {
    showToast("제목 조건을 입력하세요.");
    return false;
  }
  const rules = loadColorRules();
  rules.push({
    id: crypto.randomUUID(),
    pattern: trimmed,
    color: normalizeColorRuleColor(color),
  });
  void persistColorRules(rules, { toastMessage: "색상 조건을 추가했습니다." });
  return true;
}

function removeColorRule(id) {
  const rules = loadColorRules().filter((rule) => rule.id !== id);
  void persistColorRules(rules, { toastMessage: "색상 조건을 삭제했습니다." });
}

function normalizeColorRuleColor(color) {
  if (isHexColor(color) || SCHEDULE_COLORS.some((item) => item.id === color)) {
    return isHexColor(color) ? String(color).trim().toLowerCase() : color;
  }
  return DEFAULT_SCHEDULE_COLOR;
}

function updateColorRuleColor(id, color) {
  const rules = loadColorRules();
  const rule = rules.find((item) => item.id === id);
  if (!rule) return false;
  const nextColor = normalizeColorRuleColor(color);
  if (rule.color === nextColor) return false;
  rule.color = nextColor;
  void persistColorRules(rules, { toastMessage: "색상 조건을 수정했습니다." });
  return true;
}

function moveColorRule(id, delta) {
  const rules = loadColorRules();
  const index = rules.findIndex((rule) => rule.id === id);
  if (index < 0) return false;
  const next = index + delta;
  if (next < 0 || next >= rules.length) return false;
  const [item] = rules.splice(index, 1);
  rules.splice(next, 0, item);
  void persistColorRules(rules, { toastMessage: "색상 조건 순서를 저장했습니다." });
  return true;
}

function renderColorRulesEditor() {
  updateColorRulesSyncHint();
  const listEl = $("colorRuleList");
  if (!listEl) return;
  const rules = loadColorRules();
  if (!rules.length) {
    listEl.innerHTML = `<li class="speaker-roster-empty">등록된 조건이 없습니다. 제목에 맞는 자동 색이 없으면 배경 없이 표시됩니다.</li>`;
    return;
  }
  listEl.innerHTML = rules
    .map((rule, index) => {
      const color = getScheduleColor(rule.color);
      return `<li>
          <input
            type="color"
            class="color-rule-swatch-input"
            value="${escapeHtml(color.accent)}"
            data-color-rule-color="${escapeHtml(rule.id)}"
            title="색상 변경"
            aria-label="조건 색상 변경"
          />
          <span class="color-rule-pattern" title="${escapeHtml(rule.pattern)}">${escapeHtml(rule.pattern)}</span>
          <span class="roster-actions">
            <button type="button" class="roster-move" data-color-rule-move="${escapeHtml(rule.id)}" data-color-rule-delta="-1" title="위로" ${index === 0 ? "disabled" : ""}>↑</button>
            <button type="button" class="roster-move" data-color-rule-move="${escapeHtml(rule.id)}" data-color-rule-delta="1" title="아래로" ${index >= rules.length - 1 ? "disabled" : ""}>↓</button>
            <button type="button" class="roster-remove" data-color-rule-remove="${escapeHtml(rule.id)}" aria-label="삭제">×</button>
          </span>
        </li>`;
    })
    .join("");
}

function normalizeFontRules(raw) {
  const list = Array.isArray(raw)
    ? raw
    : Array.isArray(raw?.rules)
      ? raw.rules
      : [];
  const seen = new Set();
  const out = [];
  for (const entry of list) {
    if (!entry || typeof entry !== "object") continue;
    const pattern = String(entry.pattern || "").trim();
    if (!pattern) continue;
    const id = String(entry.id || "").trim() || crypto.randomUUID();
    if (seen.has(id)) continue;
    seen.add(id);
    out.push({
      id,
      pattern,
      titleColor: normalizeFontHex(entry.titleColor, DEFAULT_TITLE_FONT_COLOR),
      speakerColor: normalizeFontHex(
        entry.speakerColor,
        DEFAULT_SPEAKER_FONT_COLOR,
      ),
    });
  }
  return out;
}

function fontRulesPayload(rules) {
  const defaults = loadFontDefaults();
  return {
    version: 1,
    updatedAt: new Date().toISOString(),
    defaultTitleColor: defaults.titleColor,
    defaultSpeakerColor: defaults.speakerColor,
    rules: normalizeFontRules(rules),
  };
}

function loadLocalFontRules() {
  try {
    const raw = JSON.parse(localStorage.getItem(FONT_RULES_STORAGE_KEY) || "[]");
    if (raw && typeof raw === "object" && !Array.isArray(raw)) {
      fontDefaults = {
        titleColor: normalizeFontHex(
          raw.defaultTitleColor,
          DEFAULT_TITLE_FONT_COLOR,
        ),
        speakerColor: normalizeFontHex(
          raw.defaultSpeakerColor,
          DEFAULT_SPEAKER_FONT_COLOR,
        ),
      };
      fontDefaultsLoaded = true;
      return normalizeFontRules(raw);
    }
    return normalizeFontRules(raw);
  } catch {
    return [];
  }
}

function saveLocalFontRules(rules) {
  const next = normalizeFontRules(rules);
  const defaults = loadFontDefaults();
  localStorage.setItem(
    FONT_RULES_STORAGE_KEY,
    JSON.stringify({
      defaultTitleColor: defaults.titleColor,
      defaultSpeakerColor: defaults.speakerColor,
      rules: next,
    }),
  );
  return next;
}

function loadFontRules() {
  if (fontRulesCache) return fontRulesCache;
  fontRulesCache = loadLocalFontRules();
  return fontRulesCache;
}

function updateFontRulesSyncHint() {
  const el = $("fontRulesSyncStatus");
  if (!el) return;
  if (bridgeAlive && bridgeConnected && linkedFileName) {
    el.textContent = fontRulesDirty
      ? "NAS 공유 글자색 · 저장 중…"
      : "NAS에 공유되는 글자색 설정입니다. 모든 PC에서 동일하게 사용합니다.";
  } else {
    el.textContent =
      "로컬에만 저장됩니다. NAS 연결 시 공유 파일로 올라갑니다.";
  }
}

function syncFontDefaultControls() {
  const title = $("fontDefaultTitleColor");
  const speaker = $("fontDefaultSpeakerColor");
  const defaults = loadFontDefaults();
  if (title) title.value = defaults.titleColor;
  if (speaker) speaker.value = defaults.speakerColor;
}

async function readFontRulesFromNas() {
  const response = await fetch(
    `${API_BASE}/api/file?name=` + encodeURIComponent(FONT_RULES_FILE),
    { cache: "no-store" },
  );
  if (!response.ok) {
    if (response.status === 400 || response.status === 404) {
      return { rules: [], revision: "", defaults: null };
    }
    throw new Error(await readErrorMessage(response));
  }
  const revision = response.headers.get("X-File-Revision") || "";
  let raw;
  try {
    raw = await response.json();
  } catch {
    raw = [];
  }
  const defaults =
    raw && typeof raw === "object" && !Array.isArray(raw)
      ? {
          titleColor: normalizeFontHex(
            raw.defaultTitleColor,
            DEFAULT_TITLE_FONT_COLOR,
          ),
          speakerColor: normalizeFontHex(
            raw.defaultSpeakerColor,
            DEFAULT_SPEAKER_FONT_COLOR,
          ),
        }
      : null;
  return { rules: normalizeFontRules(raw), revision, defaults };
}

async function writeFontRulesToNas(rules, expectedRevision) {
  const headers = { "Content-Type": "application/json" };
  if (expectedRevision) headers["X-Expected-Revision"] = expectedRevision;
  const response = await fetch(
    `${API_BASE}/api/file?name=` + encodeURIComponent(FONT_RULES_FILE),
    {
      method: "PUT",
      headers,
      body: JSON.stringify(fontRulesPayload(rules), null, 2),
    },
  );
  if (response.status === 409) {
    const conflict = await response.json().catch(() => ({}));
    const err = new Error("충돌");
    err.conflict = conflict;
    throw err;
  }
  if (!response.ok) throw new Error(await readErrorMessage(response));
  const result = await response.json().catch(() => ({}));
  return result.revision || "";
}

async function refreshFontRulesFromNas({ quiet = true } = {}) {
  try {
    if (fontRulesDirty) {
      updateFontRulesSyncHint();
      return;
    }
    if (!(bridgeAlive && bridgeConnected && linkedFileName)) {
      fontRulesCache = loadLocalFontRules();
      fontRulesRevision = "";
      updateFontRulesSyncHint();
      syncFontDefaultControls();
      return;
    }
    const {
      rules: remote,
      revision,
      defaults: remoteDefaults,
    } = await readFontRulesFromNas();
    fontRulesRevision = revision;
    const local = loadLocalFontRules();
    if (!remote.length && local.length) {
      fontRulesCache = local;
      try {
        fontRulesRevision = await writeFontRulesToNas(local, revision);
      } catch {
        /* keep local */
      }
    } else {
      fontRulesCache = remote.length ? remote : local;
      if (remoteDefaults) {
        fontDefaults = remoteDefaults;
        fontDefaultsLoaded = true;
      }
      saveLocalFontRules(fontRulesCache);
    }
    fontRulesDirty = false;
    updateFontRulesSyncHint();
    syncFontDefaultControls();
    render();
  } catch (err) {
    fontRulesCache = loadLocalFontRules();
    updateFontRulesSyncHint();
    syncFontDefaultControls();
    if (!quiet) showToast(`글자색 설정 불러오기 실패: ${err.message}`);
  }
}

async function persistFontRules(rules, { toastMessage } = {}) {
  fontRulesCache = normalizeFontRules(rules);
  saveLocalFontRules(fontRulesCache);
  fontRulesDirty = true;
  updateFontRulesSyncHint();
  render();
  if (!(bridgeAlive && bridgeConnected && linkedFileName)) {
    if (toastMessage) showToast(`${toastMessage} (로컬)`);
    return;
  }
  try {
    fontRulesRevision = await writeFontRulesToNas(
      fontRulesCache,
      fontRulesRevision,
    );
    fontRulesDirty = false;
    updateFontRulesSyncHint();
    if (toastMessage) showToast(toastMessage);
  } catch (err) {
    if (err.conflict) {
      try {
        fontRulesRevision = err.conflict.revision || "";
        fontRulesRevision = await writeFontRulesToNas(
          fontRulesCache,
          fontRulesRevision,
        );
        fontRulesDirty = false;
        updateFontRulesSyncHint();
        if (toastMessage) showToast(toastMessage);
        return;
      } catch (retryErr) {
        showToast(`글자색 조건 충돌 저장 실패: ${retryErr.message}`);
        return;
      }
    }
    showToast(`글자색 조건 NAS 저장 실패: ${err.message} (로컬에는 저장됨)`);
    updateFontRulesSyncHint();
  }
}

function persistFontDefaults(nextDefaults, { toastMessage } = {}) {
  fontDefaults = {
    titleColor: normalizeFontHex(
      nextDefaults.titleColor,
      DEFAULT_TITLE_FONT_COLOR,
    ),
    speakerColor: normalizeFontHex(
      nextDefaults.speakerColor,
      DEFAULT_SPEAKER_FONT_COLOR,
    ),
  };
  fontDefaultsLoaded = true;
  void persistFontRules(loadFontRules(), { toastMessage });
}

function addFontRule(pattern, titleColor, speakerColor) {
  const trimmed = String(pattern || "").trim();
  if (!trimmed) {
    showToast("제목 조건을 입력하세요.");
    return false;
  }
  const rules = loadFontRules();
  rules.push({
    id: crypto.randomUUID(),
    pattern: trimmed,
    titleColor: normalizeFontHex(titleColor, DEFAULT_TITLE_FONT_COLOR),
    speakerColor: normalizeFontHex(speakerColor, DEFAULT_SPEAKER_FONT_COLOR),
  });
  void persistFontRules(rules, { toastMessage: "글자색 조건을 추가했습니다." });
  return true;
}

function removeFontRule(id) {
  const rules = loadFontRules().filter((rule) => rule.id !== id);
  void persistFontRules(rules, { toastMessage: "글자색 조건을 삭제했습니다." });
}

function updateFontRuleColor(id, field, color) {
  const rules = loadFontRules();
  const rule = rules.find((item) => item.id === id);
  if (!rule) return false;
  const next = normalizeFontHex(
    color,
    field === "speakerColor"
      ? DEFAULT_SPEAKER_FONT_COLOR
      : DEFAULT_TITLE_FONT_COLOR,
  );
  if (rule[field] === next) return false;
  rule[field] = next;
  void persistFontRules(rules, { toastMessage: "글자색 조건을 수정했습니다." });
  return true;
}

function moveFontRule(id, delta) {
  const rules = loadFontRules();
  const index = rules.findIndex((rule) => rule.id === id);
  if (index < 0) return false;
  const next = index + delta;
  if (next < 0 || next >= rules.length) return false;
  const [item] = rules.splice(index, 1);
  rules.splice(next, 0, item);
  void persistFontRules(rules, { toastMessage: "글자색 조건 순서를 저장했습니다." });
  return true;
}

function renderFontRulesEditor() {
  updateFontRulesSyncHint();
  syncFontDefaultControls();
  const listEl = $("fontRuleList");
  if (!listEl) return;
  const rules = loadFontRules();
  if (!rules.length) {
    listEl.innerHTML = `<li class="speaker-roster-empty">등록된 조건이 없습니다. 기본 글자색만 적용됩니다.</li>`;
    return;
  }
  listEl.innerHTML = rules
    .map((rule, index) => {
      return `<li class="font-rule-item">
          <label class="font-rule-color-field" title="제목 글자색">
            <span>제목</span>
            <input type="color" class="color-rule-swatch-input" value="${escapeHtml(rule.titleColor)}" data-font-rule-title="${escapeHtml(rule.id)}" aria-label="제목 글자색" />
          </label>
          <label class="font-rule-color-field" title="강사 글자색">
            <span>강사</span>
            <input type="color" class="color-rule-swatch-input" value="${escapeHtml(rule.speakerColor)}" data-font-rule-speaker="${escapeHtml(rule.id)}" aria-label="강사 글자색" />
          </label>
          <span class="color-rule-pattern" title="${escapeHtml(rule.pattern)}">${escapeHtml(rule.pattern)}</span>
          <span class="roster-actions">
            <button type="button" class="roster-move" data-font-rule-move="${escapeHtml(rule.id)}" data-font-rule-delta="-1" title="위로" ${index === 0 ? "disabled" : ""}>↑</button>
            <button type="button" class="roster-move" data-font-rule-move="${escapeHtml(rule.id)}" data-font-rule-delta="1" title="아래로" ${index >= rules.length - 1 ? "disabled" : ""}>↓</button>
            <button type="button" class="roster-remove" data-font-rule-remove="${escapeHtml(rule.id)}" aria-label="삭제">×</button>
          </span>
        </li>`;
    })
    .join("");
}

function defaultCalendarFiltersState() {
  return { filters: [], checked: {}, otherChecked: true };
}

function loadCalendarFiltersState() {
  if (calendarFiltersState) return calendarFiltersState;
  try {
    const raw = JSON.parse(
      localStorage.getItem(CALENDAR_FILTERS_STORAGE_KEY) || "null",
    );
    if (!raw || typeof raw !== "object") {
      calendarFiltersState = defaultCalendarFiltersState();
      return calendarFiltersState;
    }
    const filters = Array.isArray(raw.filters)
      ? raw.filters
          .map((item) => ({
            id: String(item?.id || crypto.randomUUID()),
            pattern: String(item?.pattern || "").trim(),
          }))
          .filter((item) => item.pattern)
      : [];
    const checked =
      raw.checked && typeof raw.checked === "object" ? { ...raw.checked } : {};
    for (const filter of filters) {
      if (!(filter.id in checked)) checked[filter.id] = true;
    }
    calendarFiltersState = {
      filters,
      checked,
      otherChecked: raw.otherChecked !== false,
    };
  } catch {
    calendarFiltersState = defaultCalendarFiltersState();
  }
  return calendarFiltersState;
}

function saveCalendarFiltersState(state) {
  calendarFiltersState = state;
  localStorage.setItem(CALENDAR_FILTERS_STORAGE_KEY, JSON.stringify(state));
}

function matchesCalendarViewFilter(item) {
  const state = loadCalendarFiltersState();
  if (!state.filters.length) return true;
  const title = item?.scheduleTitle || "";
  const hits = state.filters.filter((filter) =>
    titleMatchesColorPattern(title, filter.pattern),
  );
  if (hits.length) {
    return hits.some((filter) => state.checked[filter.id] !== false);
  }
  return state.otherChecked !== false;
}

function renderCalendarFilterChips() {
  const host = $("calendarFilterChips");
  if (!host) return;
  const state = loadCalendarFiltersState();
  if (!state.filters.length) {
    host.innerHTML = `<span class="calendar-filter-empty muted">필터 없음 (전체 표시)</span>`;
    return;
  }
  const chips = state.filters.map((filter) => {
    const on = state.checked[filter.id] !== false;
    return `<label class="calendar-filter-chip${on ? " is-on" : ""}">
      <input type="checkbox" data-calendar-filter-id="${escapeHtml(filter.id)}" ${on ? "checked" : ""} />
      <span>${escapeHtml(filter.pattern)}</span>
    </label>`;
  });
  chips.push(`<label class="calendar-filter-chip${state.otherChecked !== false ? " is-on" : ""}">
      <input type="checkbox" data-calendar-filter-other ${state.otherChecked !== false ? "checked" : ""} />
      <span>기타</span>
    </label>`);
  host.innerHTML = chips.join("");
}

function renderCalendarFilterEditor() {
  const list = $("calendarFilterRuleList");
  if (!list) return;
  const state = loadCalendarFiltersState();
  if (!state.filters.length) {
    list.innerHTML = `<li class="speaker-roster-empty">추가된 필터가 없습니다.</li>`;
    return;
  }
  list.innerHTML = state.filters
    .map(
      (filter) => `<li data-calendar-filter-rule="${escapeHtml(filter.id)}">
        <span class="color-rule-pattern" title="${escapeHtml(filter.pattern)}">${escapeHtml(filter.pattern)}</span>
        <span class="roster-actions">
          <button type="button" class="roster-remove" data-calendar-filter-remove="${escapeHtml(filter.id)}" aria-label="삭제">×</button>
        </span>
      </li>`,
    )
    .join("");
}

function addCalendarViewFilter(pattern) {
  const trimmed = String(pattern || "").trim();
  if (!trimmed) {
    showToast("필터 조건을 입력하세요.");
    return false;
  }
  const state = loadCalendarFiltersState();
  if (state.filters.some((filter) => filter.pattern === trimmed)) {
    showToast("이미 같은 필터가 있습니다.");
    return false;
  }
  const id = crypto.randomUUID();
  state.filters.push({ id, pattern: trimmed });
  state.checked[id] = true;
  saveCalendarFiltersState(state);
  renderCalendarFilterChips();
  renderCalendarFilterEditor();
  render();
  showToast("달력 필터를 추가했습니다.");
  return true;
}

function removeCalendarViewFilter(id) {
  const state = loadCalendarFiltersState();
  state.filters = state.filters.filter((filter) => filter.id !== id);
  delete state.checked[id];
  saveCalendarFiltersState(state);
  renderCalendarFilterChips();
  renderCalendarFilterEditor();
  render();
}

function normalizeImportExcludeRules(raw) {
  if (!Array.isArray(raw)) return [];
  return raw
    .map((item) => {
      if (typeof item === "string") {
        const pattern = item.trim();
        if (!pattern) return null;
        return { id: crypto.randomUUID(), pattern };
      }
      const pattern = String(item?.pattern || "").trim();
      if (!pattern) return null;
      return {
        id: String(item?.id || crypto.randomUUID()),
        pattern,
      };
    })
    .filter(Boolean);
}

function loadLocalImportExcludeRules() {
  try {
    return normalizeImportExcludeRules(
      JSON.parse(localStorage.getItem(IMPORT_EXCLUDE_STORAGE_KEY) || "[]"),
    );
  } catch {
    return [];
  }
}

function saveLocalImportExcludeRules(rules) {
  const next = normalizeImportExcludeRules(rules);
  localStorage.setItem(IMPORT_EXCLUDE_STORAGE_KEY, JSON.stringify(next));
  importExcludeCache = next;
  return next;
}

function loadImportExcludeRules() {
  if (importExcludeCache) return importExcludeCache;
  importExcludeCache = loadLocalImportExcludeRules();
  return importExcludeCache;
}

function persistImportExcludeRules(rules, { toastMessage } = {}) {
  saveLocalImportExcludeRules(rules);
  if (toastMessage) showToast(toastMessage);
}

function isScheduleTitleExcluded(title) {
  const rules = loadImportExcludeRules();
  if (!rules.length) return false;
  return rules.some((rule) => titleMatchesColorPattern(title, rule.pattern));
}

function filterExcludedImportedSchedules(imported) {
  const kept = [];
  let skipped = 0;
  for (const item of imported) {
    if (isScheduleTitleExcluded(item?.scheduleTitle)) {
      skipped += 1;
      continue;
    }
    kept.push(item);
  }
  return { kept, skipped };
}

function addImportExcludeRule(pattern) {
  const trimmed = String(pattern || "").trim();
  if (!trimmed) {
    showToast("제외할 제목 조건을 입력하세요.");
    return false;
  }
  const rules = loadImportExcludeRules();
  if (rules.some((rule) => rule.pattern === trimmed)) {
    showToast("이미 같은 조건이 있습니다.");
    return false;
  }
  rules.push({ id: crypto.randomUUID(), pattern: trimmed });
  persistImportExcludeRules(rules, { toastMessage: "제외 제목 조건을 추가했습니다." });
  return true;
}

function removeImportExcludeRule(id) {
  const rules = loadImportExcludeRules().filter((rule) => rule.id !== id);
  persistImportExcludeRules(rules, { toastMessage: "제외 제목 조건을 삭제했습니다." });
}

function moveImportExcludeRule(id, delta) {
  const rules = loadImportExcludeRules();
  const index = rules.findIndex((rule) => rule.id === id);
  if (index < 0) return false;
  const next = index + delta;
  if (next < 0 || next >= rules.length) return false;
  const [item] = rules.splice(index, 1);
  rules.splice(next, 0, item);
  persistImportExcludeRules(rules, { toastMessage: "제외 제목 조건 순서를 저장했습니다." });
  return true;
}

function renderImportExcludeEditor() {
  const listEl = $("importExcludeList");
  if (!listEl) return;
  const rules = loadImportExcludeRules();
  if (!rules.length) {
    listEl.innerHTML = `<li class="speaker-roster-empty">등록된 제외 조건이 없습니다. JSON 가져오기 시 모든 제목이 들어갑니다.</li>`;
    return;
  }
  listEl.innerHTML = rules
    .map(
      (rule, index) => `<li>
          <span class="color-rule-pattern" title="${escapeHtml(rule.pattern)}">${escapeHtml(rule.pattern)}</span>
          <span class="roster-actions">
            <button type="button" class="roster-move" data-import-exclude-move="${escapeHtml(rule.id)}" data-import-exclude-delta="-1" title="위로" ${index === 0 ? "disabled" : ""}>↑</button>
            <button type="button" class="roster-move" data-import-exclude-move="${escapeHtml(rule.id)}" data-import-exclude-delta="1" title="아래로" ${index >= rules.length - 1 ? "disabled" : ""}>↓</button>
            <button type="button" class="roster-remove" data-import-exclude-remove="${escapeHtml(rule.id)}" aria-label="삭제">×</button>
          </span>
        </li>`,
    )
    .join("");
}

function saveSpeakerRoster(roster) {
  void persistSpeakerRoster(roster);
  return normalizeSpeakerRoster(roster);
}

function getSpeakerRosterNames(_group) {
  return loadSpeakerRoster().gwangju;
}

function addSpeakerRosterName(_group, name) {
  const trimmed = String(name || "").trim();
  if (!trimmed) {
    showToast("이름을 입력하세요.");
    return false;
  }
  const roster = loadSpeakerRoster();
  if (roster.gwangju.some((item) => item === trimmed)) {
    showToast("이미 등록된 이름입니다.");
    return false;
  }
  speakerRosterDeletedNames.delete(trimmed);
  roster.gwangju.push(trimmed);
  void persistSpeakerRoster(roster, { toastMessage: "명단에 추가했습니다." });
  return true;
}

function removeSpeakerRosterName(_group, name) {
  if (name) speakerRosterDeletedNames.add(name);
  const roster = loadSpeakerRoster();
  roster.gwangju = roster.gwangju.filter((item) => item !== name);
  void persistSpeakerRoster(roster, { toastMessage: "명단에서 삭제했습니다." });
}

function moveSpeakerRosterName(_group, name, delta) {
  const roster = loadSpeakerRoster();
  const list = roster.gwangju;
  const index = list.indexOf(name);
  if (index < 0) return false;
  const next = index + delta;
  if (next < 0 || next >= list.length) return false;
  const [item] = list.splice(index, 1);
  list.splice(next, 0, item);
  void persistSpeakerRoster(roster, { toastMessage: "명단 순서를 저장했습니다." });
  return true;
}

function renderSpeakerRosterEditor() {
  updateSpeakerRosterSyncHint();
  const roster = loadSpeakerRoster();
  const listEl = $("speakerRosterListGwangju");
  if (!listEl) return;
  const names = roster.gwangju || [];
  if (!names.length) {
    listEl.innerHTML = `<li class="speaker-roster-empty">등록된 이름이 없습니다.</li>`;
    return;
  }
  listEl.innerHTML = names
    .map(
      (name, index) => `<li>
          <span class="roster-name">${escapeHtml(name)}</span>
          <span class="roster-actions">
            <button type="button" class="roster-move" data-roster-move="gwangju" data-roster-name="${escapeHtml(name)}" data-roster-delta="-1" title="위로" ${index === 0 ? "disabled" : ""}>↑</button>
            <button type="button" class="roster-move" data-roster-move="gwangju" data-roster-name="${escapeHtml(name)}" data-roster-delta="1" title="아래로" ${index >= names.length - 1 ? "disabled" : ""}>↓</button>
            <button type="button" class="roster-remove" data-roster-remove="gwangju" data-roster-name="${escapeHtml(name)}" aria-label="삭제">×</button>
          </span>
        </li>`,
    )
    .join("");
}

function openAppSettings(tab = "speakers") {
  closeSpeakerPickPopover();
  document.querySelectorAll("[data-settings-tab]").forEach((btn) => {
    const on = btn.dataset.settingsTab === tab;
    btn.classList.toggle("active", on);
    btn.setAttribute("aria-selected", on ? "true" : "false");
  });
  document.querySelectorAll("[data-settings-panel]").forEach((panel) => {
    panel.classList.toggle("hidden", panel.dataset.settingsPanel !== tab);
  });
  if (tab === "speakers") {
    void refreshSpeakerRosterFromNas({ quiet: true }).then(() =>
      renderSpeakerRosterEditor(),
    );
    renderSpeakerRosterEditor();
  }
  if (tab === "colors") {
    void refreshColorRulesFromNas({ quiet: true }).then(() =>
      renderColorRulesEditor(),
    );
    renderColorRulesEditor();
    syncHolidayBgControls();
  }
  if (tab === "fonts") {
    void refreshFontRulesFromNas({ quiet: true }).then(() =>
      renderFontRulesEditor(),
    );
    renderFontRulesEditor();
  }
  if (tab === "data") {
    initClearMonthControls();
    updateClearCalendarViewPreview();
    renderImportExcludeEditor();
  }
  if (tab === "misc") {
    syncMiscSettingsControls();
  }
  $("appSettingsDialog")?.showModal();
}

function monthInputValue(date = calendarMonth) {
  const year = date.getFullYear();
  const month = date.getMonth() + 1;
  return `${year}-${String(month).padStart(2, "0")}`;
}

function countSchedulesForMonth(ym) {
  if (!ym) return 0;
  return schedules.filter((item) =>
    String(item.scheduleDate || "").startsWith(ym),
  ).length;
}

/** 지금 보는 달력 칸에 나오는 모든 날짜(이전달·다음달 칸 포함) */
function getCalendarViewDateKeys(date = calendarMonth) {
  const year = date.getFullYear();
  const month = date.getMonth();
  const firstDay = new Date(year, month, 1);
  const startOffset = firstDay.getDay();
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const keys = [];
  for (let i = 0; i < startOffset; i += 1) {
    keys.push(dateInputValue(new Date(year, month, 1 - (startOffset - i))));
  }
  for (let day = 1; day <= daysInMonth; day += 1) {
    keys.push(dateInputValue(new Date(year, month, day)));
  }
  let trailing = 1;
  while (keys.length % 7 !== 0) {
    keys.push(dateInputValue(new Date(year, month + 1, trailing)));
    trailing += 1;
  }
  return keys;
}

function countSchedulesForCalendarView(date = calendarMonth) {
  const keySet = new Set(getCalendarViewDateKeys(date));
  return schedules.filter((item) =>
    keySet.has(String(item.scheduleDate || "")),
  ).length;
}

function updateClearCalendarViewPreview() {
  const preview = $("clearCalendarViewPreview");
  const button = $("clearCalendarViewButton");
  if (!preview || !button) return;
  if (viewMode !== "calendar") {
    preview.textContent = "달력 보기 상태에서만 사용할 수 있습니다.";
    button.disabled = true;
    return;
  }
  const ym = monthInputValue(calendarMonth);
  const [year, month] = ym.split("-");
  const count = countSchedulesForCalendarView(calendarMonth);
  preview.textContent = `지금 보는 달력(${year}년 ${Number(month)}월 주간 칸 전체, 이전·다음달 포함) · 일정 ${count}건`;
  button.disabled = count === 0;
}

function updateClearMonthPreview() {
  const input = $("clearMonthInput");
  const preview = $("clearMonthPreview");
  const button = $("clearMonthButton");
  if (!input || !preview) return;
  const ym = input.value;
  if (!ym) {
    preview.textContent = "삭제할 월을 선택하세요.";
    if (button) button.disabled = true;
    return;
  }
  const [year, month] = ym.split("-");
  const count = countSchedulesForMonth(ym);
  preview.textContent = `${year}년 ${Number(month)}월 일정 ${count}건`;
  if (button) button.disabled = count === 0;
}

function initClearMonthControls() {
  const input = $("clearMonthInput");
  if (!input) return;
  if (!input.value) input.value = monthInputValue();
  updateClearMonthPreview();
  updateClearCalendarViewPreview();
}

function clearSchedulesForMonthKey(ym, { toastPrefix } = {}) {
  if (!ym) {
    showToast("삭제할 월을 선택하세요.");
    return false;
  }
  const count = countSchedulesForMonth(ym);
  if (!count) {
    showToast("해당 월에 삭제할 일정이 없습니다.");
    updateClearMonthPreview();
    updateClearCalendarViewPreview();
    return false;
  }
  const [year, month] = ym.split("-");
  const label = toastPrefix || `${year}년 ${Number(month)}월`;
  if (
    !confirm(
      `${label} 일정 ${count}건을 모두 삭제할까요?\n이 작업은 되돌릴 수 없습니다.`,
    )
  ) {
    return false;
  }
  schedules = schedules.filter(
    (item) => !String(item.scheduleDate || "").startsWith(ym),
  );
  saveLocal();
  render();
  updateClearMonthPreview();
  updateClearCalendarViewPreview();
  showToast(`${label} 일정 ${count}건을 삭제했습니다.`);
  void syncOfficialDocumentIndex();
  return true;
}

function clearSchedulesForSelectedMonth() {
  clearSchedulesForMonthKey($("clearMonthInput")?.value);
}

function clearSchedulesForCurrentCalendarView() {
  if (viewMode !== "calendar") {
    showToast("달력 보기에서만 사용할 수 있습니다.");
    updateClearCalendarViewPreview();
    return;
  }
  const year = calendarMonth.getFullYear();
  const month = calendarMonth.getMonth() + 1;
  const keySet = new Set(getCalendarViewDateKeys(calendarMonth));
  const targets = schedules.filter((item) =>
    keySet.has(String(item.scheduleDate || "")),
  );
  const count = targets.length;
  if (!count) {
    showToast("지금 보는 달력에 삭제할 일정이 없습니다.");
    updateClearCalendarViewPreview();
    return;
  }
  const label = `지금 보는 달력(${year}년 ${month}월 · 같은 주 이전·다음달 칸 포함)`;
  if (
    !confirm(
      `${label} 일정 ${count}건을 모두 삭제할까요?\n이 작업은 되돌릴 수 없습니다.`,
    )
  ) {
    return;
  }
  schedules = schedules.filter(
    (item) => !keySet.has(String(item.scheduleDate || "")),
  );
  saveLocal();
  render();
  updateClearMonthPreview();
  updateClearCalendarViewPreview();
  showToast(`${label} 일정 ${count}건을 삭제했습니다.`);
  void syncOfficialDocumentIndex();
}

function closeAppSettings() {
  $("appSettingsDialog")?.close();
}

function closeSpeakerPickPopover() {
  const pop = $("speakerPickPopover");
  if (!pop) return;
  pop.classList.add("hidden");
  pop.hidden = true;
  document
    .querySelectorAll(".speaker-quick-btn.is-open")
    .forEach((btn) => btn.classList.remove("is-open"));
}

function openSpeakerPickPopover(group, anchor) {
  const pop = $("speakerPickPopover");
  const list = $("speakerPickList");
  const title = $("speakerPickTitle");
  if (!pop || !list || !anchor) return;
  const names = getSpeakerRosterNames(group);
  const label = SPEAKER_ROSTER_LABELS[group] || "명단";
  if (title) title.textContent = label;
  if (!names.length) {
    list.innerHTML = `<div class="speaker-pick-empty">등록된 이름이 없습니다.<br />설정(톱니바퀴) → 명단에서 추가하세요.</div>`;
  } else {
    list.innerHTML = names
      .map(
        (name) =>
          `<button type="button" class="speaker-pick-item" role="option" data-pick-speaker="${escapeHtml(name)}">${escapeHtml(name)}</button>`,
      )
      .join("");
  }
  document
    .querySelectorAll(".speaker-quick-btn.is-open")
    .forEach((btn) => btn.classList.remove("is-open"));
  anchor.classList.add("is-open");
  pop.classList.remove("hidden");
  pop.hidden = false;
  // dialog top-layer 안에서 앵커 기준으로 배치 (모달 밖 fixed는 가려짐)
  const field = anchor.closest(".speaker-field") || pop.parentElement;
  const fieldRect = field?.getBoundingClientRect?.() || anchor.getBoundingClientRect();
  const rect = anchor.getBoundingClientRect();
  const pad = 4;
  pop.style.left = `${Math.round(rect.left - fieldRect.left)}px`;
  pop.style.top = `${Math.round(rect.bottom - fieldRect.top + 6)}px`;
  pop.style.right = "auto";
  requestAnimationFrame(() => {
    const box = pop.getBoundingClientRect();
    const dialog = $("scheduleDialog");
    const dialogRect = dialog?.getBoundingClientRect?.();
    if (!dialogRect) return;
    if (box.right > dialogRect.right - pad) {
      pop.style.left = `${Math.round(
        Math.max(0, dialogRect.right - fieldRect.left - box.width - pad),
      )}px`;
    }
    if (box.bottom > dialogRect.bottom - pad) {
      pop.style.top = `${Math.round(
        Math.max(0, rect.top - fieldRect.top - box.height - 6),
      )}px`;
    }
  });
}

function applyPickedSpeaker(name) {
  const input = $("speaker");
  if (!input) return;
  input.value = name;
  input.focus();
  input.setSelectionRange(input.value.length, input.value.length);
  closeSpeakerPickPopover();
}

function toggleSpeakerPick(group, anchor) {
  const pop = $("speakerPickPopover");
  const open =
    pop &&
    !pop.hidden &&
    !pop.classList.contains("hidden") &&
    anchor?.classList.contains("is-open");
  if (open) {
    closeSpeakerPickPopover();
    return;
  }
  openSpeakerPickPopover(group, anchor);
}

async function syncOfficialDocumentIndex() {
  if (!bridgeAlive || !bridgeConnected || !linkedFileName) return;
  try {
    const documents = schedules
      .filter((item) => item.officialDocument)
      .map((item) => ({
        scheduleId: item.id,
        scheduleDate: item.scheduleDate,
        scheduleTitle: item.scheduleTitle,
        fileName: item.officialDocument,
        folder: OFFICIAL_DOC_FOLDER,
        linkedAt: new Date().toISOString().slice(0, 19).replace("T", " "),
        schedule: {
          id: item.id,
          scheduleDate: item.scheduleDate,
          releaseDate: item.releaseDate,
          startTime: item.startTime,
          scheduleTitle: item.scheduleTitle,
          accessoryInfo: item.accessoryInfo,
          place: item.place,
          sermonSpeaker: item.sermonSpeaker,
          preparation: item.preparation || [],
          preaparationNote: item.preaparationNote,
          officialDocument: item.officialDocument,
        },
      }));
    const response = await fetch(`${API_BASE}/api/document/index`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ documents }, null, 2),
    });
    if (!response.ok) throw new Error(await readErrorMessage(response));
  } catch (error) {
    if (!isBridgeDownError(error)) {
      console.warn("공문 인덱스 동기화 실패:", error.message);
    }
  }
}

function officialDocumentUrl(fileName) {
  return (
    `${API_BASE}/api/document?name=` + encodeURIComponent(fileName)
  );
}

/** YYYYMMDD → YYYY-MM-DD (유효하지 않으면 "") */
function ymd8ToDateKey(raw) {
  const text = String(raw || "");
  if (!/^\d{8}$/.test(text)) return "";
  const dateKey = `${text.slice(0, 4)}-${text.slice(4, 6)}-${text.slice(6, 8)}`;
  return Number.isNaN(Date.parse(dateKey)) ? "" : dateKey;
}

/** 강사 토큰 여부 — 이름 끝이 PA / EV 일 때만 */
function isOfficialDocSpeakerToken(value) {
  return /(?:PA|EV)$/i.test(String(value || "").trim());
}

/** 공문 파일명의 장소 문구 → place 키 */
function matchPlaceKeyFromDocLabel(text) {
  const raw = String(text || "").trim();
  if (!raw) return "None";
  const norm = raw.replace(/\s+/g, "");
  for (const [key, label] of Object.entries(PLACE_LABELS)) {
    if (key === "None") continue;
    if (String(label).replace(/\s+/g, "") === norm) return key;
  }
  for (const [key, label] of Object.entries(PLACE_LABELS)) {
    if (key === "None") continue;
    const lab = String(label).replace(/\s+/g, "");
    if (norm.includes(lab) || lab.includes(norm)) return key;
  }
  return "Etc";
}

/**
 * 공문 파일명 → 일정 초안 필드
 * `시작[_끝]_제목[_강사PA|EV][_장소][_시각]` (`_` 구분, 페이지 `_00` 은 groupKey에서 제외)
 */
function parseOfficialDocScheduleFields(fileName) {
  const parsed = parseOfficialDocName(fileName);
  const parts = String(parsed.groupKey || "")
    .split("_")
    .map((part) => part.trim())
    .filter(Boolean);
  let idx = 0;
  let startDate = "";
  let endDate = "";
  if (parts[0] && ymd8ToDateKey(parts[0])) {
    startDate = ymd8ToDateKey(parts[0]);
    idx = 1;
    if (parts[1] && ymd8ToDateKey(parts[1])) {
      endDate = ymd8ToDateKey(parts[1]);
      idx = 2;
    } else {
      endDate = startDate;
    }
  }
  const rest = parts.slice(idx);
  let startTime = "";
  let placeLabel = "";
  let speaker = "";
  if (rest.length && /^\d{3,4}$/.test(rest[rest.length - 1])) {
    startTime = rest.pop().padStart(4, "0");
  }
  if (rest.length) {
    const last = rest[rest.length - 1];
    if (isOfficialDocSpeakerToken(last)) {
      speaker = rest.pop();
    } else if (rest.length >= 2 && isOfficialDocSpeakerToken(rest[rest.length - 2])) {
      placeLabel = rest.pop();
      speaker = rest.pop();
    } else if (rest.length >= 2) {
      placeLabel = rest.pop();
    }
  }
  const title = rest.join("_").trim();
  return {
    fileName: parsed.fileName,
    groupKey: parsed.groupKey,
    startDate,
    endDate: endDate || startDate,
    title,
    speaker,
    placeLabel,
    placeKey: matchPlaceKeyFromDocLabel(placeLabel),
    startTime,
  };
}

/** 파일명 → 공문 그룹·페이지·날짜 파싱 (`20260920_행사공문_00.jpg`) */
function parseOfficialDocName(fileName) {
  const name = String(fileName || "");
  const dot = name.lastIndexOf(".");
  const base = dot > 0 ? name.slice(0, dot) : name;
  const pageMatch = base.match(/_(\d+)$/);
  const page = pageMatch ? Number(pageMatch[1]) : 0;
  const groupKey = pageMatch ? base.slice(0, -pageMatch[0].length) : base;
  const dual = groupKey.match(/^(\d{8})_(\d{8})(?!\d)/);
  let dateKey = "";
  let endDateKey = "";
  let hasYmd8Prefix = false;
  if (dual) {
    dateKey = ymd8ToDateKey(dual[1]);
    endDateKey = ymd8ToDateKey(dual[2]);
    hasYmd8Prefix = Boolean(dateKey);
    if (!endDateKey) endDateKey = dateKey;
  } else {
    const ymd8 = base.match(/^(\d{8})(?!\d)/);
    if (ymd8) {
      dateKey = ymd8ToDateKey(ymd8[1]);
      if (dateKey) {
        hasYmd8Prefix = true;
        endDateKey = dateKey;
      }
    } else {
      // 월 일정 이미지 등 6자리(yyyymm) — 연결 목록에는 쓰지 않음
      const ymd6 = groupKey.match(/^(\d{6})(?!\d)/);
      if (ymd6) {
        dateKey = ymd8ToDateKey(`${ymd6[1]}01`);
        endDateKey = dateKey;
      }
    }
  }
  return { fileName: name, groupKey, page, dateKey, endDateKey, hasYmd8Prefix };
}

function shiftMonths(dateKey, months) {
  const date = dateValue(dateKey);
  date.setMonth(date.getMonth() + months);
  return date;
}

/** 일정 날짜 기준 앞뒤 2개월 이내인지 (경계 포함). 날짜 없으면 제외.
 *  기간 공문(시작~끝)은 시작·끝 중 하나라도 창 안에 있거나, 일정일이 적용기간에 포함되면 통과. */
function isOfficialDocInScheduleWindow(docDateKey, scheduleDateKey, docEndDateKey = "") {
  if (!docDateKey || !scheduleDateKey) return false;
  try {
    const schedule = dateValue(scheduleDateKey);
    const winStart = shiftMonths(scheduleDateKey, -2);
    const winEnd = shiftMonths(scheduleDateKey, 2);
    const docStart = dateValue(docDateKey);
    const docEnd = docEndDateKey ? dateValue(docEndDateKey) : docStart;
    if (docStart >= winStart && docStart <= winEnd) return true;
    if (docEnd >= winStart && docEnd <= winEnd) return true;
    if (schedule >= docStart && schedule <= docEnd) return true;
    return false;
  } catch {
    return false;
  }
}

/** 공문 연결 드롭다운에 넣을 수 있는 파일인지 (YYYYMMDD 8자리 시작) */
function isLinkableOfficialDocument(parsed) {
  return Boolean(parsed?.hasYmd8Prefix && parsed?.dateKey);
}

function groupOfficialDocuments(files) {
  const map = new Map();
  for (const item of files) {
    const name = typeof item === "string" ? item : item?.name;
    if (!name) continue;
    const parsed = parseOfficialDocName(name);
    if (!map.has(parsed.groupKey)) map.set(parsed.groupKey, []);
    map.get(parsed.groupKey).push(parsed);
  }
  for (const pages of map.values()) {
    pages.sort((a, b) => a.page - b.page || a.fileName.localeCompare(b.fileName, "ko"));
  }
  return map;
}

let officialViewerPages = [];
let officialViewerIndex = 0;
let officialViewerZoom = 1;
const OFFICIAL_VIEWER_ZOOM_MIN = 0.5;
const OFFICIAL_VIEWER_ZOOM_MAX = 3;
const OFFICIAL_VIEWER_ZOOM_STEP = 0.25;

function applyOfficialViewerZoom() {
  const image = $("officialDocViewerImage");
  if (image) {
    image.style.width = `${Math.round(officialViewerZoom * 100)}%`;
    image.style.maxWidth = "none";
    image.style.maxHeight = "none";
  }
  const label = $("officialDocViewerZoomLabel");
  if (label) label.textContent = `${Math.round(officialViewerZoom * 100)}%`;
  const zoomOut = $("officialDocViewerZoomOut");
  const zoomIn = $("officialDocViewerZoomIn");
  if (zoomOut) zoomOut.disabled = officialViewerZoom <= OFFICIAL_VIEWER_ZOOM_MIN;
  if (zoomIn) zoomIn.disabled = officialViewerZoom >= OFFICIAL_VIEWER_ZOOM_MAX;
}

function setOfficialViewerZoom(next) {
  officialViewerZoom = Math.max(
    OFFICIAL_VIEWER_ZOOM_MIN,
    Math.min(OFFICIAL_VIEWER_ZOOM_MAX, next),
  );
  applyOfficialViewerZoom();
}

function resetOfficialViewerZoom() {
  officialViewerZoom = 1;
  applyOfficialViewerZoom();
}

async function fetchOfficialDocumentList() {
  const response = await fetch(`${API_BASE}/api/documents`, { cache: "no-store" });
  if (!response.ok) throw new Error(await readErrorMessage(response));
  const files = await response.json();
  return Array.isArray(files) ? files : [];
}

async function showOfficialViewerPage(index) {
  if (!officialViewerPages.length) return;
  officialViewerIndex = Math.max(0, Math.min(index, officialViewerPages.length - 1));
  const current = officialViewerPages[officialViewerIndex];
  const response = await fetch(officialDocumentUrl(current.fileName), {
    cache: "no-store",
  });
  if (!response.ok) throw new Error(await readErrorMessage(response));
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const image = $("officialDocViewerImage");
  if (image.dataset.objectUrl) URL.revokeObjectURL(image.dataset.objectUrl);
  image.dataset.objectUrl = url;
  image.src = url;
  image.onload = () => {
    // 원본 비율 유지 (CSS object-fit + intrinsic size)
    image.removeAttribute("width");
    image.removeAttribute("height");
    applyOfficialViewerZoom();
  };
  applyOfficialViewerZoom();
  const groupLabel = current.groupKey || current.fileName;
  $("officialDocViewerTitle").textContent =
    officialViewerPages.length > 1
      ? `${groupLabel} · ${officialViewerIndex + 1}/${officialViewerPages.length}`
      : groupLabel;
  const label = $("officialDocViewerPageLabel");
  if (label) {
    label.textContent = `${officialViewerIndex + 1} / ${officialViewerPages.length}`;
  }
  const prev = $("officialDocViewerPrev");
  const next = $("officialDocViewerNext");
  if (prev) prev.disabled = officialViewerIndex <= 0;
  if (next) next.disabled = officialViewerIndex >= officialViewerPages.length - 1;
  const makeBtn = $("officialDocMakeScheduleButton");
  if (makeBtn) {
    const canMake = Boolean(
      current?.hasYmd8Prefix ||
        parseOfficialDocName(current?.fileName || "").hasYmd8Prefix,
    );
    makeBtn.classList.toggle("hidden", !canMake);
    makeBtn.disabled = !canMake;
  }
}

async function openOfficialDocumentViewer(fileName) {
  if (!fileName) return showToast("연결된 공문이 없습니다.");
  if (!bridgeAlive || !bridgeConnected)
    return showToast("NAS 연결 후 공문을 볼 수 있습니다.");
  try {
    const files = await fetchOfficialDocumentList();
    const groups = groupOfficialDocuments(files);
    const parsed = parseOfficialDocName(fileName);
    const pages = groups.get(parsed.groupKey) || [parsed];
    officialViewerPages = pages;
    const startIndex = Math.max(
      0,
      pages.findIndex((page) => page.fileName === fileName),
    );
    resetOfficialViewerZoom();
    $("officialDocViewerDialog").showModal();
    await showOfficialViewerPage(startIndex < 0 ? 0 : startIndex);
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    showToast(`공문 열기 실패: ${error.message}`);
  }
}

/** `yyyymm_광주교회일정_nn.jpg|png` — 공문 폴더 월 일정 이미지 (NFC 정규화) */
const MONTHLY_SCHEDULE_DOC_RE =
  /^(\d{6})_광주교회일정(?:_(\d+))?$/i;
const MONTHLY_SCHEDULE_EXTS = new Set([".jpg", ".jpeg", ".png"]);

function normalizeFsName(value) {
  try {
    return String(value || "").normalize("NFC");
  } catch {
    return String(value || "");
  }
}

function calendarYearMonthKey(date = calendarMonth) {
  const year = date.getFullYear();
  const month = date.getMonth() + 1;
  return {
    year,
    month,
    yyyymm: `${year}${String(month).padStart(2, "0")}`,
  };
}

function updateMonthlyScheduleButtonLabel() {
  const label = $("monthlyScheduleButtonLabel");
  if (!label) return;
  const { month } = calendarYearMonthKey();
  label.innerHTML = `광주교회<br />${month}월 일정`;
}

function updateMonthlyOfficialDocsButtonLabel() {
  const label = $("monthlyOfficialDocsButtonLabel");
  if (!label) return;
  label.innerHTML = `이번 달<br />공문`;
}

/** 달력(또는 지정) 연월의 YYYYMMDD 공문 그룹만 */
function findMonthlyOfficialDocuments(files, yyyymm) {
  const targetYm = normalizeFsName(yyyymm);
  const year = targetYm.slice(0, 4);
  const month = targetYm.slice(4, 6);
  const datePrefix = `${year}-${month}`;
  const groups = groupOfficialDocuments(files);
  const items = [];
  for (const [groupKey, pages] of groups.entries()) {
    if (!pages?.length) continue;
    // 월 일정 이미지(yyyymm_광주교회일정)는 제외
    if (MONTHLY_SCHEDULE_DOC_RE.test(normalizeFsName(groupKey))) continue;
    const hasYmd8 = pages.some((page) => page.hasYmd8Prefix);
    const dateKey = pages.find((page) => page.dateKey)?.dateKey || "";
    if (
      !hasYmd8 ||
      !isLinkableOfficialDocument({
        hasYmd8Prefix: hasYmd8,
        dateKey,
      }) ||
      !dateKey.startsWith(datePrefix)
    ) {
      continue;
    }
    items.push({
      groupKey,
      pages,
      value: pages[0].fileName,
      dateKey,
      label:
        pages.length > 1
          ? `${groupKey} (${pages.length}장)`
          : groupKey || pages[0].fileName,
    });
  }
  items.sort((a, b) => {
    const byDate = (b.dateKey || "").localeCompare(a.dateKey || "");
    if (byDate) return byDate;
    return a.groupKey.localeCompare(b.groupKey, "ko");
  });
  return items;
}

function formatOfficialDocDateLabel(dateKey) {
  const date = parseIsoDateLocal(dateKey);
  if (!date) return dateKey || "";
  return `${date.getFullYear()}.${date.getMonth() + 1}.${date.getDate()} (${DAY_NAMES[date.getDay()]})`;
}

async function openMonthlyOfficialDocsDialog() {
  if (!bridgeAlive || !bridgeConnected) {
    return showToast("NAS 연결 후 공문을 볼 수 있습니다.");
  }
  const { year, month, yyyymm } = calendarYearMonthKey();
  const dialog = $("monthlyOfficialDocsDialog");
  const list = $("monthlyOfficialDocsList");
  const state = $("monthlyOfficialDocsState");
  const title = $("monthlyOfficialDocsTitle");
  const hint = $("monthlyOfficialDocsHint");
  if (!dialog || !list || !state) return;
  if (title) title.textContent = `${year}년 ${month}월 공문`;
  if (hint) {
    hint.innerHTML = `<code>_official_documents</code>에서 <strong>${year}년 ${month}월</strong> 날짜(YYYYMMDD)로 시작하는 공문입니다. 항목을 누르면 공문을 봅니다.`;
  }
  list.innerHTML = "";
  state.textContent = "공문 목록 조회 중...";
  state.dataset.state = "pending";
  dialog.showModal();
  try {
    const files = await fetchOfficialDocumentList();
    const items = findMonthlyOfficialDocuments(files, yyyymm);
    if (!items.length) {
      state.textContent = `${year}년 ${month}월 공문이 없습니다.`;
      state.dataset.state = "idle";
      return;
    }
    list.innerHTML = items
      .map(
        (item) => `<li>
          <button type="button" class="monthly-official-doc-item" data-doc="${escapeHtml(item.value)}">
            <span class="monthly-official-doc-title">${escapeHtml(item.label)}</span>
            <span class="monthly-official-doc-meta">${escapeHtml(formatOfficialDocDateLabel(item.dateKey))}${item.pages.length > 1 ? ` · ${item.pages.length}장` : ""}</span>
          </button>
        </li>`,
      )
      .join("");
    state.textContent = `${items.length}개 공문`;
    state.dataset.state = "ok";
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    state.textContent = error.message || String(error);
    state.dataset.state = "error";
  }
}

function closeMonthlyOfficialDocsDialog() {
  $("monthlyOfficialDocsDialog")?.close();
}

function findMonthlyScheduleDocuments(files, yyyymm) {
  const pages = [];
  const targetYm = normalizeFsName(yyyymm);
  for (const item of files) {
    const rawName = typeof item === "string" ? item : item?.name;
    if (!rawName) continue;
    const name = normalizeFsName(rawName.replace(/\\/g, "/").split("/").pop());
    const dot = name.lastIndexOf(".");
    if (dot < 0) continue;
    const ext = name.slice(dot).toLowerCase();
    if (!MONTHLY_SCHEDULE_EXTS.has(ext)) continue;
    const base = normalizeFsName(name.slice(0, dot));
    const match = base.match(MONTHLY_SCHEDULE_DOC_RE);
    if (!match || match[1] !== targetYm) continue;
    pages.push({
      fileName: name,
      groupKey: `${match[1]}_광주교회일정`,
      page: match[2] != null ? Number(match[2]) : 0,
      dateKey: `${match[1].slice(0, 4)}-${match[1].slice(4, 6)}-01`,
    });
  }
  pages.sort(
    (a, b) => a.page - b.page || a.fileName.localeCompare(b.fileName, "ko"),
  );
  return pages;
}

async function openMonthlyScheduleImage() {
  if (!bridgeAlive || !bridgeConnected) {
    return showToast("NAS 연결 후 월 일정 이미지를 볼 수 있습니다.");
  }
  const { year, month, yyyymm } = calendarYearMonthKey();
  try {
    const files = await fetchOfficialDocumentList();
    const pages = findMonthlyScheduleDocuments(files, yyyymm);
    if (!pages.length) {
      showToast(`${year}년 ${month}월 일정 이미지가 없습니다.`);
      return;
    }
    officialViewerPages = pages;
    resetOfficialViewerZoom();
    $("officialDocViewerDialog").showModal();
    await showOfficialViewerPage(0);
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    showToast(`월 일정 이미지 열기 실패: ${error.message}`);
  }
}

function requireOfficialDocContext({ needTitle = false } = {}) {
  if (!bridgeAlive || !bridgeConnected || !linkedFileName) {
    showToast("NAS 공유 파일 연결 후 공문을 사용할 수 있습니다.");
    return false;
  }
  const date = $("startDate").value;
  if (!date) {
    showToast("공문 연결 전에 일정 날짜를 입력하세요.");
    return false;
  }
  // 새 일정은 제목보다 공문 연결을 먼저 하는 경우가 많음 — 연결/업로드는 날짜만 필수
  if (needTitle && !$("title").value.trim()) {
    showToast("공문 연결 전에 일정 제목을 입력하세요.");
    return false;
  }
  if (!pendingScheduleId) pendingScheduleId = crypto.randomUUID();
  return true;
}

async function uploadOfficialDocumentFile(file) {
  if (!requireOfficialDocContext()) return;
  if (!file) return;
  const form = new FormData();
  form.append("file", file, file.name);
  form.append("scheduleId", pendingScheduleId);
  form.append("scheduleDate", $("startDate").value);
  form.append("scheduleTitle", $("title").value.trim());
  try {
    const response = await fetch(`${API_BASE}/api/document/upload`, {
      method: "POST",
      body: form,
    });
    if (!response.ok) throw new Error(await readErrorMessage(response));
    const result = await response.json();
    pendingOfficialDocument = result.fileName || "";
    updateOfficialDocumentUi();
    const savedAs = result.fileName || "";
    const renamed =
      savedAs &&
      file.name &&
      savedAs.toLowerCase() !== String(file.name).toLowerCase();
    showToast(
      renamed
        ? `공문을 업로드했습니다 → ${savedAs}. 일정 저장을 완료하세요.`
        : "공문을 업로드하고 연결했습니다. 일정 저장을 완료하세요.",
    );
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    showToast(`공문 업로드 실패: ${error.message}`);
  }
}

async function openOfficialDocLinkDialog() {
  if (!requireOfficialDocContext()) return;
  const select = $("officialDocLinkSelect");
  const confirm = $("officialDocLinkConfirm");
  const state = $("officialDocLinkState");
  select.innerHTML = '<option value="">불러오는 중...</option>';
  confirm.disabled = true;
  state.textContent = "공문 목록 조회 중...";
  state.dataset.state = "pending";
  $("officialDocLinkDialog").showModal();
  try {
    const files = await fetchOfficialDocumentList();
    const scheduleDate = $("startDate").value;
    const groups = groupOfficialDocuments(files);
    const options = [...groups.entries()]
      .map(([groupKey, pages]) => {
        const first = pages[0];
        const dateKey = pages.find((page) => page.dateKey)?.dateKey || first.dateKey;
        const endDateKey =
          pages.find((page) => page.endDateKey)?.endDateKey ||
          first.endDateKey ||
          dateKey;
        return {
          groupKey,
          pages,
          value: first.fileName,
          dateKey,
          endDateKey,
          hasYmd8Prefix: pages.some((page) => page.hasYmd8Prefix),
          label:
            pages.length > 1
              ? `${groupKey} (${pages.length}장)`
              : groupKey || first.fileName,
        };
      })
      .filter(
        (item) =>
          item.hasYmd8Prefix &&
          isLinkableOfficialDocument({
            hasYmd8Prefix: item.hasYmd8Prefix,
            dateKey: item.dateKey,
          }) &&
          isOfficialDocInScheduleWindow(
            item.dateKey,
            scheduleDate,
            item.endDateKey,
          ),
      )
      .sort((a, b) => {
        const byDate = (b.dateKey || "").localeCompare(a.dateKey || "");
        if (byDate) return byDate;
        return a.groupKey.localeCompare(b.groupKey, "ko");
      });

    if (!options.length) {
      select.innerHTML =
        '<option value="">조건에 맞는 공문이 없습니다.</option>';
      state.textContent = scheduleDate
        ? `YYYYMMDD(8자리)로 시작하고 일정 ${scheduleDate} 기준 ±2개월 이내 공문만 표시합니다.`
        : "_official_documents 폴더에 연결 가능한 공문이 없습니다.";
      state.dataset.state = "idle";
      return;
    }
    select.innerHTML = options
      .map(
        (item) =>
          `<option value="${escapeHtml(item.value)}">${escapeHtml(
            item.label,
          )}</option>`,
      )
      .join("");
    confirm.disabled = false;
    const skipped = groups.size - options.length;
    state.textContent =
      skipped > 0
        ? `${options.length}개 공문 (날짜 밖 ${skipped}개 제외)`
        : `${options.length}개 공문`;
    state.dataset.state = "ok";
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    select.innerHTML = '<option value="">목록을 불러오지 못했습니다.</option>';
    state.textContent = error.message;
    state.dataset.state = "error";
  }
}

async function confirmOfficialDocLink() {
  if (!requireOfficialDocContext()) return;
  const fileName = $("officialDocLinkSelect").value;
  if (!fileName) return showToast("연결할 공문을 선택하세요.");
  // 새 일정에서 제목이 비어 있으면 파일명 규칙으로 채움
  if (!$("title").value.trim()) {
    const fields = parseOfficialDocScheduleFields(fileName);
    if (fields.title) $("title").value = fields.title;
  }
  try {
    const response = await fetch(`${API_BASE}/api/document/link`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        fileName,
        scheduleId: pendingScheduleId,
        scheduleDate: $("startDate").value,
        scheduleTitle: $("title").value.trim(),
      }),
    });
    if (!response.ok) throw new Error(await readErrorMessage(response));
    const result = await response.json();
    pendingOfficialDocument = result.fileName || fileName;
    updateOfficialDocumentUi();
    $("officialDocLinkDialog").close();
    showToast("공문을 연결했습니다. 일정 저장을 완료하세요.");
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    // 인덱스 갱신 실패여도 일정 초안에는 파일명을 붙여 저장할 수 있게 함
    pendingOfficialDocument = fileName;
    updateOfficialDocumentUi();
    $("officialDocLinkDialog").close();
    showToast(
      `공문을 일정에 연결했습니다. 저장을 완료하세요. (인덱스: ${error.message})`,
    );
  }
}

/** 보고 있는 공문 파일명으로 새 일정 입력창을 연다 */
function openScheduleFromOfficialDocument(fileName) {
  const source = String(fileName || "").trim();
  if (!source) return showToast("공문 파일이 없습니다.");
  const fields = parseOfficialDocScheduleFields(source);
  if (!fields.startDate) {
    return showToast(
      "파일명에서 날짜를 읽지 못했습니다. YYYYMMDD_ 형식이 필요합니다.",
    );
  }
  $("officialDocViewerDialog")?.close();
  $("monthlyOfficialDocsDialog")?.close();
  openDialog(null, fields.startDate);
  $("title").value = fields.title || "";
  $("endDate").value = fields.endDate || fields.startDate;
  setStartTimeValue(fields.startTime || "");
  setPlaceValue(fields.placeKey || "None");
  $("speaker").value = fields.speaker || "";
  pendingOfficialDocument = source;
  const isRange =
    Boolean(fields.endDate) && fields.endDate !== fields.startDate;
  setMode(isRange ? "range" : "single");
  updateOfficialDocumentUi();
  setOfficialDocExpanded(true);
  showToast(
    fields.title
      ? `공문 규칙으로 「${fields.title}」 일정을 채웠습니다. 확인 후 저장하세요.`
      : "공문을 연결한 새 일정입니다. 제목을 입력한 뒤 저장하세요.",
  );
}

function openDialog(item, presetDate) {
  editingId = item?.id || null;
  pendingScheduleId = item?.id || crypto.randomUUID();
  pendingOfficialDocument = item?.officialDocument || "";
  activePresetId = null;
  $("dialogTitle").textContent = item ? "일정 수정" : "새 일정";
  $("deleteScheduleButton").classList.toggle("hidden", !item);
  const bulkModeButton = $("bulkModeButton");
  if (bulkModeButton) bulkModeButton.classList.toggle("hidden", Boolean(item));
  $("startDate").value =
    item?.scheduleDate ||
    presetDate ||
    new Date().toISOString().slice(0, 10);
  $("endDate").value = item?.releaseDate || $("startDate").value;
  setStartTimeValue(
    item?.startTime ? String(item.startTime).padStart(4, "0") : "",
  );
  $("title").value = item?.scheduleTitle || "";
  $("accessory").value = item?.accessoryInfo || "";
  setPlaceValue(item?.place || "None");
  $("speaker").value = item?.sermonSpeaker || "";
  $("note").value = item?.preaparationNote || "";
  document
    .querySelectorAll("#scheduleForm fieldset input")
    .forEach(
      (box) => (box.checked = (item?.preparation || []).includes(box.value)),
    );
  renderColorSwatches(DEFAULT_SCHEDULE_COLOR);
  setFormColorState(item?.colorMode, item?.color || (item ? undefined : DEFAULT_COLOR_MODE));
  presetManageMode = false;
  editingPresetId = null;
  void refreshPresetsFromNas({ quiet: true });
  void refreshSpeakerRosterFromNas({ quiet: true });
  void refreshColorRulesFromNas({ quiet: true });
  void refreshFontRulesFromNas({ quiet: true });
  renderPresetList();
  setPresetCreatePanelOpen(false);
  const officialBlock = $("officialDocBlock");
  if (officialBlock) delete officialBlock.dataset.userOpened;
  resetBulkScheduleRows(presetDate || $("startDate").value);
  setMode(item && item.releaseDate !== item.scheduleDate ? "range" : "single");
  updateOfficialDocumentUi();
  $("scheduleDialog").showModal();
}
function setMode(mode) {
  if (mode === "bulk" && editingId) {
    mode = "single";
  }
  inputMode = mode;
  const isBulk = mode === "bulk";
  document
    .querySelectorAll("#scheduleDialog .mode")
    .forEach((button) =>
      button.classList.toggle("active", button.dataset.mode === mode),
    );
  $("endDateField")?.classList.toggle("hidden", mode !== "range");
  $("scheduleSinglePanel")?.classList.toggle("hidden", isBulk);
  $("scheduleBulkPanel")?.classList.toggle("hidden", !isBulk);
  $("scheduleDialog")?.classList.toggle("is-bulk-mode", isBulk);
  $("deleteScheduleButton")?.classList.toggle(
    "hidden",
    isBulk || !editingId,
  );
  const submitButton = $("scheduleSubmitButton");
  if (submitButton) submitButton.textContent = isBulk ? "일괄 저장" : "저장";
  if (isBulk) {
    $("dialogTitle").textContent = "다수 일정 추가";
    updateBulkRowCountLabel();
    requestAnimationFrame(() => {
      const firstTitle = $("bulkScheduleRows")?.querySelector(
        'tr [data-bulk="title"]',
      );
      firstTitle?.focus();
    });
  } else if (!editingId) {
    $("dialogTitle").textContent = "새 일정";
  }
}
function scheduleTimeToInputValue(value) {
  if (value === "" || value == null || Number(value) === 0) return "";
  const raw = String(value).padStart(4, "0");
  return `${raw.slice(0, 2)}:${raw.slice(2, 4)}`;
}
function inputValueToScheduleTime(value) {
  if (!value) return 0;
  const [hour, minute] = String(value).split(":").map(Number);
  if (!Number.isFinite(hour) || !Number.isFinite(minute)) return 0;
  return hour * 100 + minute;
}
function bulkPlaceOptionsHtml(selected = "None") {
  return Object.entries(PLACE_LABELS)
    .map(
      ([value, label]) =>
        `<option value="${escapeHtml(value)}" ${
          value === selected ? "selected" : ""
        }>${escapeHtml(label)}</option>`,
    )
    .join("");
}
function bulkPrepChecksHtml(selected = []) {
  const picked = new Set(selected || []);
  return Object.entries(PREP_LABELS)
    .map(
      ([value, label]) =>
        `<label><input type="checkbox" data-bulk-prep value="${escapeHtml(
          value,
        )}" ${picked.has(value) ? "checked" : ""} /> ${escapeHtml(
          label,
        )}</label>`,
    )
    .join("");
}
function bulkColorSwatchesHtml(selected = DEFAULT_COLOR_MODE) {
  const state = normalizeColorState("", selected);
  const stored = encodeColorValue(state);
  const customId =
    state.colorMode === COLOR_MODE_CUSTOM ? state.color : DEFAULT_SCHEDULE_COLOR;
  const swatches = SCHEDULE_COLORS.map(
    (color) =>
      `<button type="button" class="bulk-color-swatch${
        color.id === customId ? " active" : ""
      }" data-bulk-color="${escapeHtml(color.id)}" title="${escapeHtml(
        color.label,
      )}" aria-label="${escapeHtml(color.label)}" style="background:${
        color.accent
      }"></button>`,
  ).join("");
  const customActive = isHexColor(customId);
  const customValue = customActive
    ? customId
    : getScheduleColor(customId).accent;
  return `<div class="bulk-color-swatches" data-bulk-color-wrap>
    <input type="hidden" data-bulk="color" value="${escapeHtml(stored)}" />
    <div class="color-mode-row" role="group" aria-label="색상 방법">
      <button type="button" class="choice-chip${state.colorMode === COLOR_MODE_NONE ? " active" : ""}" data-bulk-color-mode="none">없음</button>
      <button type="button" class="choice-chip${state.colorMode === COLOR_MODE_AUTO ? " active" : ""}" data-bulk-color-mode="auto">자동</button>
      <button type="button" class="choice-chip${state.colorMode === COLOR_MODE_CUSTOM ? " active" : ""}" data-bulk-color-mode="custom">직접</button>
    </div>
    <div class="bulk-color-custom-panel${state.colorMode === COLOR_MODE_CUSTOM ? "" : " hidden"}">
    ${swatches}
    <label class="bulk-color-custom${customActive ? " active" : ""}" title="직접 선택">
      <input type="color" data-bulk-color-custom value="${escapeHtml(
        customValue,
      )}" aria-label="색상 직접 선택" />
    </label>
    </div>
  </div>`;
}
function createBulkScheduleRow(seed = {}) {
  const date =
    seed.scheduleDate ||
    $("startDate")?.value ||
    new Date().toISOString().slice(0, 10);
  const endDate = seed.releaseDate || seed.endDate || date;
  const colorId = encodeColorValue(
    normalizeColorState(seed.colorMode, seed.color || DEFAULT_COLOR_MODE),
  );
  const row = document.createElement("tr");
  row.innerHTML = `
    <td class="bulk-col-date"><input type="date" data-bulk="date" value="${escapeHtml(
      date,
    )}" /></td>
    <td class="bulk-col-end"><input type="date" data-bulk="end" value="${escapeHtml(
      endDate,
    )}" /></td>
    <td class="bulk-col-title"><input type="text" data-bulk="title" placeholder="일정 제목" value="${escapeHtml(
      seed.scheduleTitle || "",
    )}" autocomplete="off" /></td>
    <td class="bulk-col-time"><input type="time" data-bulk="time" step="600" value="${escapeHtml(
      scheduleTimeToInputValue(seed.startTime),
    )}" /></td>
    <td class="bulk-col-place"><select data-bulk="place">${bulkPlaceOptionsHtml(
      seed.place || "None",
    )}</select></td>
    <td class="bulk-col-speaker"><input type="text" data-bulk="speaker" placeholder="강사" value="${escapeHtml(
      seed.sermonSpeaker || "",
    )}" autocomplete="off" /></td>
    <td class="bulk-col-color">${bulkColorSwatchesHtml(colorId)}</td>
    <td class="bulk-col-prep"><div class="bulk-prep">${bulkPrepChecksHtml(
      seed.preparation || [],
    )}</div></td>
    <td class="bulk-col-remove"><button type="button" class="bulk-row-remove" data-bulk-remove aria-label="행 삭제">×</button></td>
  `;
  return row;
}
function updateBulkRowCountLabel() {
  const count = $("bulkScheduleRows")?.querySelectorAll("tr").length || 0;
  const label = $("bulkRowCountLabel");
  if (label) label.textContent = `${count}행`;
}
function addBulkScheduleRows(count = 1, seed = {}) {
  const body = $("bulkScheduleRows");
  if (!body) return;
  for (let i = 0; i < count; i += 1) {
    body.append(createBulkScheduleRow(seed));
  }
  updateBulkRowCountLabel();
}
function resetBulkScheduleRows(presetDate) {
  const body = $("bulkScheduleRows");
  if (!body) return;
  body.innerHTML = "";
  const date =
    presetDate ||
    $("startDate")?.value ||
    new Date().toISOString().slice(0, 10);
  addBulkScheduleRows(2, { scheduleDate: date, releaseDate: date });
}
function clearBulkScheduleRows() {
  const body = $("bulkScheduleRows");
  if (!body) return;
  const firstDate =
    body.querySelector('[data-bulk="date"]')?.value ||
    $("startDate")?.value ||
    new Date().toISOString().slice(0, 10);
  const firstEnd =
    body.querySelector('[data-bulk="end"]')?.value || firstDate;
  body.innerHTML = "";
  addBulkScheduleRows(2, {
    scheduleDate: firstDate,
    releaseDate: firstEnd,
  });
}
function fillBulkEmptyDatesFromFirst() {
  const body = $("bulkScheduleRows");
  if (!body) return;
  const firstDate = body.querySelector('[data-bulk="date"]')?.value || "";
  const firstEnd = body.querySelector('[data-bulk="end"]')?.value || firstDate;
  if (!firstDate) return showToast("첫 행의 시작일을 먼저 입력하세요.");
  let filled = 0;
  body.querySelectorAll("tr").forEach((row, index) => {
    if (index === 0) return;
    const startInput = row.querySelector('[data-bulk="date"]');
    const endInput = row.querySelector('[data-bulk="end"]');
    if (startInput && !startInput.value) {
      startInput.value = firstDate;
      filled += 1;
    }
    if (endInput && !endInput.value) {
      endInput.value = firstEnd || firstDate;
      filled += 1;
    }
  });
  showToast(filled ? `빈 날짜 ${filled}칸을 채웠습니다.` : "채울 빈 날짜가 없습니다.");
}
function collectBulkScheduleItems() {
  const rows = [...($("bulkScheduleRows")?.querySelectorAll("tr") || [])];
  const items = [];
  for (let index = 0; index < rows.length; index += 1) {
    const row = rows[index];
    const start = row.querySelector('[data-bulk="date"]')?.value || "";
    const endRaw = row.querySelector('[data-bulk="end"]')?.value || "";
    const end = endRaw || start;
    const title = (row.querySelector('[data-bulk="title"]')?.value || "").trim();
    const speaker = (
      row.querySelector('[data-bulk="speaker"]')?.value || ""
    ).trim();
    const timeValue = row.querySelector('[data-bulk="time"]')?.value || "";
    const place = row.querySelector('[data-bulk="place"]')?.value || "None";
    const colorState = normalizeColorState(
      "",
      row.querySelector('[data-bulk="color"]')?.value,
    );
    const preparation = [
      ...row.querySelectorAll("[data-bulk-prep]:checked"),
    ].map((box) => box.value);
    if (!title && !speaker && !timeValue && !preparation.length) continue;
    if (!title) {
      return { error: `${index + 1}행: 일정 제목을 입력해 주세요.` };
    }
    if (!start) {
      return { error: `${index + 1}행: 시작일을 입력해 주세요.` };
    }
    if (end < start) {
      return { error: `${index + 1}행: 종료일이 시작일보다 빠릅니다.` };
    }
    const base = {
      startTime: inputValueToScheduleTime(timeValue),
      scheduleTitle: title,
      accessoryInfo: "",
      place,
      sermonSpeaker: speaker,
      preparation,
      preaparationNote: "",
      color: colorState.color,
      colorMode: colorState.colorMode,
    };
    if (start === end) {
      items.push({
        ...base,
        id: crypto.randomUUID(),
        scheduleDate: start,
        releaseDate: end,
      });
    } else {
      for (
        let date = dateValue(start);
        date <= dateValue(end);
        date.setDate(date.getDate() + 1)
      ) {
        items.push({
          ...base,
          id: crypto.randomUUID(),
          scheduleDate: dateInputValue(date),
          releaseDate: end,
        });
      }
    }
  }
  return { items };
}
function submitBulkSchedules() {
  const { items, error } = collectBulkScheduleItems();
  if (error) return showToast(error);
  if (!items.length) return showToast("추가할 일정을 한 줄 이상 입력해 주세요.");
  items.forEach((item) => upsert(normalizeScheduleItem(item)));
  saveLocal();
  render();
  closeScheduleDialog();
  showToast(`${items.length}건의 일정을 추가했습니다.`);
  void syncOfficialDocumentIndex();
}
/** Expand a date range into one schedule item per day (releaseDate = end). */
function expandScheduleDateRange(base, start, end, { singleId } = {}) {
  const rows = [];
  if (!start || !end || end < start) return rows;
  if (start === end) {
    rows.push({
      ...base,
      id: singleId || crypto.randomUUID(),
      scheduleDate: start,
      releaseDate: end,
    });
    return rows;
  }
  for (
    let date = dateValue(start);
    date <= dateValue(end);
    date.setDate(date.getDate() + 1)
  ) {
    rows.push({
      ...base,
      id: crypto.randomUUID(),
      scheduleDate: dateInputValue(date),
      releaseDate: end,
    });
  }
  return rows;
}

function parseBulkTimeValue(raw) {
  const text = String(raw || "").trim();
  if (!text) return 0;
  const match = text.match(/^(\d{1,2}):(\d{2})$/);
  if (!match) return null;
  const hour = Number(match[1]);
  let minute = Number(match[2]);
  if (hour > 23 || minute > 59) return null;
  minute = Math.round(minute / 10) * 10;
  if (minute === 60) {
    if (hour >= 23) return 2330;
    return (hour + 1) * 100;
  }
  return hour * 100 + minute;
}

function placeSelectOptionsHtml(selected = "None") {
  return Object.entries(PLACE_LABELS)
    .map(
      ([id, label]) =>
        `<option value="${escapeHtml(id)}"${id === selected ? " selected" : ""}>${escapeHtml(label)}</option>`,
    )
    .join("");
}

function colorSelectOptionsHtml(selected = DEFAULT_SCHEDULE_COLOR) {
  const selectedId = resolveScheduleColorId(selected);
  return SCHEDULE_COLORS.map(
    (item) =>
      `<option value="${escapeHtml(item.id)}"${item.id === selectedId ? " selected" : ""}>${escapeHtml(item.label)}</option>`,
  ).join("");
}

/** 메인「일괄 입력」대화상자용 (다수 추가 createBulkScheduleRow 와 이름 충돌 금지) */
function createEntryBulkScheduleRow() {
  const tr = document.createElement("tr");
  tr.className = "bulk-schedule-row";
  tr.innerHTML = `
    <td><input type="date" class="bulk-start" aria-label="시작일" /></td>
    <td><input type="date" class="bulk-end" aria-label="종료일" /></td>
    <td><input type="text" class="bulk-title" placeholder="일정 제목" aria-label="일정 제목" autocomplete="off" /></td>
    <td><input type="time" class="bulk-time" step="600" aria-label="시간" /></td>
    <td><select class="bulk-place" aria-label="장소">${placeSelectOptionsHtml()}</select></td>
    <td class="bulk-color-cell">${bulkColorSwatchesHtml()}</td>
    <td class="bulk-col-remove"><button type="button" class="bulk-row-remove" data-entry-bulk-remove aria-label="행 삭제">×</button></td>
  `;
  return tr;
}

function resetEntryBulkScheduleRows(count = 5) {
  const body = $("bulkScheduleBody");
  if (!body) return;
  body.innerHTML = "";
  for (let i = 0; i < count; i += 1) body.appendChild(createEntryBulkScheduleRow());
}

function appendBulkScheduleRow() {
  const body = $("bulkScheduleBody");
  if (!body) {
    showToast("일괄 입력 표를 찾을 수 없습니다.");
    return;
  }
  const row = createEntryBulkScheduleRow();
  body.appendChild(row);
  const wrap = body.closest(".bulk-schedule-table-wrap");
  wrap?.scrollTo({ top: wrap.scrollHeight, behavior: "smooth" });
  row.querySelector(".bulk-title")?.focus();
}

function openBulkScheduleDialog() {
  resetEntryBulkScheduleRows(5);
  $("bulkScheduleDialog")?.showModal();
}

function closeBulkScheduleDialog() {
  $("bulkScheduleDialog")?.close();
}

function submitBulkSchedule() {
  const rows = [...document.querySelectorAll("#bulkScheduleBody tr")];
  let added = 0;
  let skippedInvalid = 0;
  for (const row of rows) {
    const title = row.querySelector(".bulk-title")?.value.trim() || "";
    const start = row.querySelector(".bulk-start")?.value || "";
    let end = row.querySelector(".bulk-end")?.value || "";
    const timeRaw = row.querySelector(".bulk-time")?.value || "";
    if (!title && !start && !end && !timeRaw) continue;
    if (!title) {
      skippedInvalid += 1;
      continue;
    }
    if (!start) {
      skippedInvalid += 1;
      continue;
    }
    if (!end || end === start) end = start;
    if (end < start) {
      skippedInvalid += 1;
      continue;
    }
    const startTime = parseBulkTimeValue(timeRaw);
    if (startTime === null) {
      skippedInvalid += 1;
      continue;
    }
    const colorState = normalizeColorState(
      "",
      row.querySelector("[data-bulk='color']")?.value ||
        row.querySelector(".bulk-color")?.value,
    );
    const base = {
      startTime,
      scheduleTitle: title,
      accessoryInfo: "",
      place: row.querySelector(".bulk-place")?.value || "None",
      sermonSpeaker: "",
      preparation: [],
      preaparationNote: "",
      color: colorState.color,
      colorMode: colorState.colorMode,
    };
    const items = expandScheduleDateRange(base, start, end);
    for (const item of items) {
      upsert(item);
      added += 1;
    }
  }
  if (!added) {
    return showToast(
      skippedInvalid
        ? "저장할 유효한 행이 없습니다. 제목·날짜를 확인해 주세요."
        : "저장할 행이 없습니다. 제목을 입력해 주세요.",
    );
  }
  saveLocal();
  render();
  closeBulkScheduleDialog();
  const extra =
    skippedInvalid > 0 ? ` · 건너뛴 행 ${skippedInvalid}개` : "";
  showToast(`${added}개 일정을 일괄 추가했습니다.${extra}`);
}

function submitSchedule() {
  if (inputMode === "bulk") {
    submitBulkSchedules();
    return;
  }
  const start = $("startDate").value;
  const end = inputMode === "range" ? $("endDate").value : start;
  if (!start || !end || end < start)
    return showToast("날짜 범위를 확인해 주세요.");
  const preparation = [
    ...document.querySelectorAll("#scheduleForm fieldset input:checked"),
  ].map((box) => box.value);
  const colorState = getFormColorState();
  const base = {
    scheduleDate: start,
    releaseDate: end,
    startTime: $("startTime").value ? Number($("startTime").value) : 0,
    scheduleTitle: $("title").value.trim(),
    accessoryInfo: $("accessory").value.trim(),
    place: $("place").value || "None",
    sermonSpeaker: $("speaker").value.trim(),
    preparation,
    preaparationNote: $("note").value.trim(),
    color: colorState.color,
    colorMode: colorState.colorMode,
  };
  if (pendingOfficialDocument) {
    base.officialDocument = pendingOfficialDocument;
  }
  if (!base.scheduleTitle) return showToast("일정 제목을 입력해 주세요.");
  const wasEditing = Boolean(editingId);
  const itemId = editingId || pendingScheduleId || crypto.randomUUID();
  if (wasEditing || inputMode === "single" || start === end) {
    const next = { ...base, id: itemId };
    if (!pendingOfficialDocument) delete next.officialDocument;
    upsert(next);
  } else {
    const rows = expandScheduleDateRange(base, start, end);
    for (const row of rows) {
      if (!pendingOfficialDocument) delete row.officialDocument;
      schedules.push(row);
    }
  }
  saveLocal();
  render();
  closeScheduleDialog();
  showToast(wasEditing ? "일정을 수정했습니다." : "일정을 추가했습니다.");
  void syncOfficialDocumentIndex();
}
function renderCard(item) {
  const date = formatDate(item.scheduleDate);
  const prep = item.preparation || [];
  const display = resolveEventDisplayColor(item);
  const selected = selectedExportIds.has(item.id);
  const selectCell = exportSelectMode
    ? `<label class="export-select-check"><input type="checkbox" data-export-select="${item.id}" ${selected ? "checked" : ""} aria-label="내보내기 선택" /></label>`
    : "";
  const actions = exportSelectMode
    ? ""
    : `<div class="card-actions" style="display:flex;gap:6px;align-items:center"><button class="edit-button" data-edit="${item.id}" aria-label="일정 수정">↗</button><button class="delete-card-button" style="color:#c74f3a;font-size:20px" data-delete="${item.id}" aria-label="일정 삭제">×</button></div>`;
  return `<article class="schedule-card${selected ? " is-export-selected" : ""}${display.none ? " is-none" : ""}" style="${scheduleColorStyle(item)}">${selectCell}<div class="date-block"><div class="date-day">${date.day}</div><div class="date-month">${date.month}</div><div class="date-week">${date.week}</div></div><div><div class="card-title">${escapeHtml(item.scheduleTitle)}</div><div class="meta"><span>${formatTime(item.startTime)}</span><span>${escapeHtml(PLACE_LABELS[item.place] || item.place || "장소 미정")}</span>${item.sermonSpeaker ? `<span>${escapeHtml(item.sermonSpeaker)}</span>` : ""}</div>${item.accessoryInfo ? `<div class="card-note">${escapeHtml(item.accessoryInfo)}</div>` : ""}<div class="meta" style="margin-top:8px">${prep.length ? prep.map((value) => `<span class="tag orange">● ${escapeHtml(PREP_LABELS[value] || value)}</span>`).join("") : '<span class="tag">● 준비 없음</span>'}</div>${item.preaparationNote ? `<div class="card-note">${escapeHtml(item.preaparationNote)}</div>` : ""}</div>${actions}</article>`;
}
function upsert(item) {
  const index = schedules.findIndex((value) => value.id === item.id);
  if (index < 0) schedules.push(item);
  else schedules[index] = item;
}
function emptyNoticePayload() {
  return { text: "", updatedAt: "", lock: null, history: [] };
}

function normalizeNoticeHistory(data) {
  const fromHistory = Array.isArray(data?.history)
    ? data.history
        .map((item) => ({
          text: typeof item?.text === "string" ? item.text : "",
          updatedAt: item?.updatedAt || "",
        }))
        .filter((item) => item.text !== "" || item.updatedAt !== "")
    : [];
  if (fromHistory.length) return fromHistory.slice(0, NOTICE_HISTORY_MAX);
  const text = typeof data?.text === "string" ? data.text : "";
  const updatedAt = data?.updatedAt || "";
  if (text !== "" || updatedAt !== "") return [{ text, updatedAt }];
  return [];
}

function parseNoticePayload(raw) {
  try {
    const data = typeof raw === "string" ? JSON.parse(raw) : raw;
    const history = normalizeNoticeHistory(data);
    const latest = history[0] || { text: "", updatedAt: "" };
    return {
      text: latest.text,
      updatedAt: latest.updatedAt,
      lock: data?.lock && typeof data.lock === "object" ? data.lock : null,
      history,
    };
  } catch {
    return emptyNoticePayload();
  }
}

function buildNoticePayload({ text, updatedAt, lock = null, history }) {
  const entries =
    Array.isArray(history) && history.length
      ? history
          .map((item) => ({
            text: typeof item?.text === "string" ? item.text : "",
            updatedAt: item?.updatedAt || "",
          }))
          .slice(0, NOTICE_HISTORY_MAX)
      : [{ text: text || "", updatedAt: updatedAt || "" }];
  const latest = entries[0] || { text: "", updatedAt: "" };
  return {
    text: latest.text,
    updatedAt: latest.updatedAt,
    lock: lock ?? null,
    history: entries,
  };
}

function appendNoticeHistoryEntry(history, text, updatedAt) {
  return [{ text: text || "", updatedAt: updatedAt || "" }, ...(history || [])].slice(
    0,
    NOTICE_HISTORY_MAX,
  );
}

function applySharedNoticeState(payload) {
  sharedNoticeHistory = normalizeNoticeHistory(payload);
  sharedNoticeText = sharedNoticeHistory[0]?.text || "";
  sharedNoticeUpdatedAt = sharedNoticeHistory[0]?.updatedAt || "";
  sharedNoticeLock = payload.lock ?? null;
  if (sharedNoticeViewIndex > Math.max(0, sharedNoticeHistory.length - 1)) {
    sharedNoticeViewIndex = Math.max(0, sharedNoticeHistory.length - 1);
  }
}

function isNoticeLockActive(lock, now = Date.now()) {
  if (!lock?.sessionId || !lock?.expiresAt) return false;
  return Number(lock.expiresAt) > now;
}

function isNoticeLockedByOther(lock = sharedNoticeLock) {
  return (
    isNoticeLockActive(lock) &&
    lock.sessionId &&
    lock.sessionId !== NOTICE_SESSION_ID
  );
}

function updateNoticeHistoryNav() {
  const older = $("sharedNoticeOlderButton");
  const newer = $("sharedNoticeNewerButton");
  if (!older || !newer) return;
  const maxIndex = Math.max(0, sharedNoticeHistory.length - 1);
  const atOldest = sharedNoticeViewIndex >= maxIndex;
  const atNewest = sharedNoticeViewIndex <= 0;
  older.disabled = sharedNoticeEditing || atOldest;
  newer.disabled = sharedNoticeEditing || atNewest;
  older.classList.toggle("is-disabled", older.disabled);
  newer.classList.toggle("is-disabled", newer.disabled);
}

function applyNoticeToUi({ keepDraft = false } = {}) {
  const input = $("sharedNoticeInput");
  const button = $("sharedNoticeEditButton");
  const lockState = $("sharedNoticeLockState");
  if (!input || !button) return;

  const lockedByOther = isNoticeLockedByOther();
  if (!sharedNoticeEditing) {
    const entry =
      sharedNoticeHistory[sharedNoticeViewIndex] ||
      ({ text: sharedNoticeText, updatedAt: sharedNoticeUpdatedAt });
    input.value = entry.text || "";
    input.readOnly = true;
    input.classList.remove("is-editing");
    button.textContent = "수정";
    button.classList.remove("is-editing");
    button.disabled = lockedByOther;
  } else if (!keepDraft) {
    input.value = sharedNoticeDraft;
  }

  if (lockState) {
    if (lockedByOther) {
      lockState.textContent = "다른 사용자가 수정 중입니다";
    } else if (sharedNoticeEditing) {
      lockState.textContent = "수정 중 (이 PC)";
    } else if (sharedNoticeViewIndex > 0) {
      const entry = sharedNoticeHistory[sharedNoticeViewIndex];
      const stamp = entry?.updatedAt
        ? new Date(entry.updatedAt).toLocaleString("ko-KR")
        : "";
      lockState.textContent = stamp
        ? `이전 기록 · ${stamp}`
        : `이전 기록 · ${sharedNoticeViewIndex + 1}/${sharedNoticeHistory.length || 1}`;
    } else {
      lockState.textContent = "";
    }
  }
  updateNoticeHistoryNav();
}

function showOlderSharedNotice() {
  if (sharedNoticeEditing) return;
  const maxIndex = Math.max(0, sharedNoticeHistory.length - 1);
  if (sharedNoticeViewIndex >= maxIndex) return;
  sharedNoticeViewIndex += 1;
  applyNoticeToUi();
}

function showNewerSharedNotice() {
  if (sharedNoticeEditing) return;
  if (sharedNoticeViewIndex <= 0) return;
  sharedNoticeViewIndex -= 1;
  applyNoticeToUi();
}

async function readNoticeFromNas() {
  const response = await fetch(
    `${API_BASE}/api/file?name=` + encodeURIComponent(NOTICE_FILE),
    { cache: "no-store" },
  );
  if (!response.ok) {
    if (response.status === 400 || response.status === 404) {
      return { payload: emptyNoticePayload(), revision: "" };
    }
    throw new Error(await readErrorMessage(response));
  }
  const revision = response.headers.get("X-File-Revision") || "";
  const payload = parseNoticePayload(await response.text());
  return { payload, revision };
}

async function writeNoticeToNas(payload, expectedRevision) {
  const headers = { "Content-Type": "application/json" };
  if (expectedRevision) headers["X-Expected-Revision"] = expectedRevision;
  const response = await fetch(
    `${API_BASE}/api/file?name=` + encodeURIComponent(NOTICE_FILE),
    {
      method: "PUT",
      headers,
      body: JSON.stringify(payload, null, 2),
    },
  );
  if (response.status === 409) {
    const conflict = await response.json().catch(() => ({}));
    const err = new Error("충돌");
    err.conflict = conflict;
    throw err;
  }
  if (!response.ok) throw new Error(await readErrorMessage(response));
  const result = await response.json().catch(() => ({}));
  return result.revision || "";
}

function loadLocalNotice() {
  try {
    return parseNoticePayload(localStorage.getItem(NOTICE_STORAGE_KEY) || "{}");
  } catch {
    return emptyNoticePayload();
  }
}

function saveLocalNotice(payload) {
  localStorage.setItem(NOTICE_STORAGE_KEY, JSON.stringify(payload));
}

async function refreshSharedNotice({ quiet = true } = {}) {
  try {
    if (bridgeAlive && bridgeConnected) {
      const { payload, revision } = await readNoticeFromNas();
      sharedNoticeRevision = revision;
      if (!sharedNoticeEditing) {
        applySharedNoticeState(payload);
      } else {
        sharedNoticeLock = payload.lock;
      }
    } else {
      const payload = loadLocalNotice();
      sharedNoticeRevision = "";
      if (!sharedNoticeEditing) {
        applySharedNoticeState(payload);
      } else {
        sharedNoticeLock = null;
      }
    }
    applyNoticeToUi({ keepDraft: true });
  } catch (error) {
    if (!quiet) showToast(`공유사항 불러오기 실패: ${error.message}`);
  }
}

function stopNoticeHeartbeat() {
  if (sharedNoticeHeartbeatTimer) {
    clearInterval(sharedNoticeHeartbeatTimer);
    sharedNoticeHeartbeatTimer = null;
  }
}

function startNoticeHeartbeat() {
  stopNoticeHeartbeat();
  sharedNoticeHeartbeatTimer = setInterval(() => {
    void renewNoticeLock();
  }, NOTICE_HEARTBEAT_MS);
}

function startNoticePolling() {
  if (sharedNoticePollTimer) clearInterval(sharedNoticePollTimer);
  sharedNoticePollTimer = setInterval(() => {
    if (document.visibilityState === "hidden") return;
    void refreshSharedNotice({ quiet: true });
  }, NOTICE_POLL_MS);
}

async function renewNoticeLock() {
  if (!sharedNoticeEditing) return;
  if (!(bridgeAlive && bridgeConnected)) return;
  try {
    const { payload, revision } = await readNoticeFromNas();
    sharedNoticeRevision = revision;
    if (isNoticeLockedByOther(payload.lock)) {
      await cancelSharedNoticeEdit({ remoteTaken: true });
      return;
    }
    const next = buildNoticePayload({
      text: payload.text || sharedNoticeText,
      updatedAt: payload.updatedAt || sharedNoticeUpdatedAt,
      history: payload.history?.length ? payload.history : sharedNoticeHistory,
      lock: {
        sessionId: NOTICE_SESSION_ID,
        expiresAt: Date.now() + NOTICE_LOCK_TTL_MS,
      },
    });
    sharedNoticeRevision = await writeNoticeToNas(next, sharedNoticeRevision);
    sharedNoticeLock = next.lock;
    applyNoticeToUi({ keepDraft: true });
  } catch {
    // 네트워크 일시 오류는 다음 heartbeat에서 재시도
  }
}

async function beginSharedNoticeEdit() {
  if (sharedNoticeEditing) return;
  sharedNoticeViewIndex = 0;
  if (bridgeAlive && bridgeConnected) {
    try {
      const { payload, revision } = await readNoticeFromNas();
      sharedNoticeRevision = revision;
      applySharedNoticeState(payload);
      if (isNoticeLockedByOther(payload.lock)) {
        applyNoticeToUi();
        showToast("다른 사용자가 수정 중입니다.");
        return;
      }
      const next = buildNoticePayload({
        text: payload.text || "",
        updatedAt: payload.updatedAt || "",
        history: payload.history,
        lock: {
          sessionId: NOTICE_SESSION_ID,
          expiresAt: Date.now() + NOTICE_LOCK_TTL_MS,
        },
      });
      sharedNoticeRevision = await writeNoticeToNas(next, sharedNoticeRevision);
      sharedNoticeLock = next.lock;
    } catch (error) {
      showToast(`수정 잠금 실패: ${error.message}`);
      return;
    }
  }

  sharedNoticeEditing = true;
  sharedNoticeDraft = sharedNoticeText;
  const input = $("sharedNoticeInput");
  const button = $("sharedNoticeEditButton");
  if (input) {
    input.readOnly = false;
    input.classList.add("is-editing");
    input.value = sharedNoticeDraft;
    input.focus();
  }
  if (button) {
    button.textContent = "완료";
    button.classList.add("is-editing");
    button.disabled = false;
  }
  applyNoticeToUi({ keepDraft: true });
  startNoticeHeartbeat();
}

async function cancelSharedNoticeEdit({ remoteTaken = false } = {}) {
  stopNoticeHeartbeat();
  if (bridgeAlive && bridgeConnected && sharedNoticeEditing) {
    try {
      const { payload, revision } = await readNoticeFromNas();
      sharedNoticeRevision = revision;
      if (payload.lock?.sessionId === NOTICE_SESSION_ID) {
        const next = buildNoticePayload({
          text: payload.text || sharedNoticeText,
          updatedAt: payload.updatedAt || sharedNoticeUpdatedAt,
          history: payload.history?.length ? payload.history : sharedNoticeHistory,
          lock: null,
        });
        sharedNoticeRevision = await writeNoticeToNas(next, sharedNoticeRevision);
        applySharedNoticeState(next);
      } else {
        applySharedNoticeState(payload);
      }
    } catch {
      // ignore unlock failure
    }
  }
  sharedNoticeEditing = false;
  sharedNoticeDraft = "";
  sharedNoticeViewIndex = 0;
  applyNoticeToUi();
  if (remoteTaken) showToast("다른 사용자가 수정 중입니다. 수정이 종료되었습니다.");
}

async function commitSharedNoticeEdit() {
  if (!sharedNoticeEditing) return;
  const input = $("sharedNoticeInput");
  const draft = input ? input.value : sharedNoticeDraft;
  if (
    !confirm("공유사항을 정말로 수정할까요?\n확인을 누르면 모든 연결된 PC에 반영됩니다.")
  ) {
    await cancelSharedNoticeEdit();
    return;
  }

  const updatedAt = new Date().toISOString();
  const nextHistory = appendNoticeHistoryEntry(
    sharedNoticeHistory,
    draft,
    updatedAt,
  );
  const payload = buildNoticePayload({
    text: draft,
    updatedAt,
    history: nextHistory,
    lock: null,
  });

  try {
    if (bridgeAlive && bridgeConnected) {
      const latest = await readNoticeFromNas();
      sharedNoticeRevision = latest.revision;
      if (
        isNoticeLockedByOther(latest.payload.lock) &&
        latest.payload.lock.sessionId !== NOTICE_SESSION_ID
      ) {
        showToast("다른 사용자가 수정 중입니다. 저장할 수 없습니다.");
        await cancelSharedNoticeEdit({ remoteTaken: true });
        return;
      }
      const mergedHistory = appendNoticeHistoryEntry(
        latest.payload.history?.length
          ? latest.payload.history
          : sharedNoticeHistory,
        draft,
        updatedAt,
      );
      const next = buildNoticePayload({
        text: draft,
        updatedAt,
        history: mergedHistory,
        lock: null,
      });
      sharedNoticeRevision = await writeNoticeToNas(next, sharedNoticeRevision);
      applySharedNoticeState(next);
    } else {
      saveLocalNotice(payload);
      applySharedNoticeState(payload);
    }
    sharedNoticeEditing = false;
    sharedNoticeDraft = "";
    sharedNoticeViewIndex = 0;
    stopNoticeHeartbeat();
    applyNoticeToUi();
    showToast("공유사항을 저장했습니다.");
  } catch (error) {
    if (error.conflict) {
      showToast("다른 곳에서 공유사항이 변경되었습니다. 다시 시도해 주세요.");
      await refreshSharedNotice({ quiet: false });
      await cancelSharedNoticeEdit();
      return;
    }
    showToast(`공유사항 저장 실패: ${error.message}`);
  }
}

async function toggleSharedNoticeEdit() {
  if (sharedNoticeEditing) {
    sharedNoticeDraft = $("sharedNoticeInput")?.value ?? sharedNoticeDraft;
    await commitSharedNoticeEdit();
  } else {
    await beginSharedNoticeEdit();
  }
}

function showToast(message) {
  $("toast").textContent = message;
  $("toast").classList.add("show");
  setTimeout(() => $("toast").classList.remove("show"), 2400);
}
function setExportSelectMode(enabled) {
  exportSelectMode = Boolean(enabled);
  if (!exportSelectMode) selectedExportIds.clear();
  $("exportSelectToggle")?.classList.toggle("active", exportSelectMode);
  $("exportSelectBar")?.classList.toggle("hidden", !exportSelectMode);
  updateExportSelectionUi();
  render();
  if (exportSelectMode) {
    showToast("일정을 체크한 뒤 「선택된 일정 내보내기」를 누르세요.");
  }
}

function updateExportSelectionUi() {
  const count = selectedExportIds.size;
  if ($("exportSelectCount")) {
    $("exportSelectCount").textContent = `${count}개 선택`;
  }
  if ($("exportSelectedButton")) {
    $("exportSelectedButton").disabled = count === 0;
  }
}

function toggleExportSelection(id, force) {
  if (!id) return;
  const shouldSelect =
    force === undefined ? !selectedExportIds.has(id) : Boolean(force);
  if (shouldSelect) selectedExportIds.add(id);
  else selectedExportIds.delete(id);
  updateExportSelectionUi();
}

function selectVisibleSchedulesForExport() {
  const shown =
    viewMode === "calendar"
      ? getFilteredSchedules({ ignoreRange: true })
      : getFilteredSchedules();
  for (const item of shown) selectedExportIds.add(item.id);
  updateExportSelectionUi();
  render();
  showToast(`현재 목록 ${shown.length}개를 선택했습니다.`);
}

function clearExportSelection() {
  selectedExportIds.clear();
  updateExportSelectionUi();
  render();
}

function downloadJsonList(list, filename, toastMessage) {
  const blob = new Blob([JSON.stringify(list, null, 2)], {
    type: "application/json",
  });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = filename;
  link.click();
  URL.revokeObjectURL(link.href);
  showToast(toastMessage);
}

function downloadJson() {
  downloadJsonList(
    schedules,
    "schedule-data.json",
    "schedule-data.json을 내보냈습니다.",
  );
}

function downloadSelectedJson() {
  const selected = schedules.filter((item) => selectedExportIds.has(item.id));
  if (!selected.length) {
    return showToast("내보낼 일정을 선택해 주세요.");
  }
  const stamp = new Date().toISOString().slice(0, 10).replace(/-/g, "");
  downloadJsonList(
    selected,
    `schedule-selected-${stamp}.json`,
    `선택한 ${selected.length}개 일정을 내보냈습니다.`,
  );
}

function openImportModeDialog() {
  pendingImportMode = null;
  $("importModeDialog")?.showModal();
}

function chooseImportMode(mode) {
  pendingImportMode = mode === "merge" ? "merge" : "replace";
  $("importModeDialog")?.close();
  $("fileInput")?.click();
}

function applyImportedSchedules(imported, mode) {
  const { kept, skipped } = filterExcludedImportedSchedules(imported);
  const skipNote = skipped > 0 ? ` · 제외 조건으로 ${skipped}개 건너뜀` : "";
  const normalized = kept.map((item) => normalizeScheduleItem(item));
  if (mode === "merge") {
    const map = new Map(schedules.map((item) => [item.id, item]));
    let added = 0;
    let updated = 0;
    for (const item of normalized) {
      if (map.has(item.id)) updated += 1;
      else added += 1;
      map.set(item.id, item);
    }
    schedules = [...map.values()];
    saveLocal();
    setSchedulesBase(schedules);
    render();
    showToast(
      `추가 병합 완료 · 새로 ${added}개, 갱신 ${updated}개 (전체 ${schedules.length}개)${skipNote}`,
    );
    return;
  }

  schedules = normalized;
  saveLocal();
  setSchedulesBase(schedules);
  render();
  showToast(
    `덮어쓰기 완료 · ${schedules.length}개 일정으로 교체했습니다.${skipNote}`,
  );
}
async function connectNas() {
  if (!window.showDirectoryPicker)
    return showToast(
      "이 브라우저는 폴더 연결을 지원하지 않습니다. JSON 가져오기를 사용해 주세요.",
    );
  try {
    nasHandle = await window.showDirectoryPicker({ mode: "readwrite" });
    $("nasPath").textContent = `${nasHandle.name} / schedule-data.json`;
    $("saveNasButton").disabled = false;
    $("syncStatus").textContent = "NAS 폴더 연결됨";
    try {
      const fileHandle = await nasHandle.getFileHandle("schedule-data.json");
      const imported = JSON.parse(await (await fileHandle.getFile()).text());
      if (!Array.isArray(imported)) throw new Error("배열 형식이 아닙니다.");
      schedules = imported.map((item) => normalizeScheduleItem(item));
      saveLocal();
      render();
      showToast(`${schedules.length}개 일정을 NAS에서 불러왔습니다.`);
    } catch (error) {
      if (error.name === "NotFoundError")
        showToast("공유 폴더에 파일이 없어 새 파일로 연결했습니다.");
      else throw error;
    }
  } catch (error) {
    if (error.name !== "AbortError")
      showToast(`NAS 연결 실패: ${error.message}`);
  }
}
async function saveNas() {
  if (!nasHandle) return;
  try {
    const fileHandle = await nasHandle.getFileHandle("schedule-data.json", {
      create: true,
    });
    const writable = await fileHandle.createWritable();
    await writable.write(JSON.stringify(schedules, null, 2));
    await writable.close();
    showToast("NAS에 저장했습니다.");
  } catch (error) {
    showToast(`NAS 저장 실패: ${error.message}`);
  }
}
function selectSharedFile() {
  showLoginScreen();
}
function applyJsonFileOptions(files, preferredName = "") {
  const preferred = preferredName || pickPreferredJsonFile(files);
  const html = files.length
    ? files
        .map(
          (name) =>
            `<option value="${escapeHtml(name)}">${escapeHtml(name)}</option>`,
        )
        .join("")
    : '<option value="">JSON 파일이 없습니다.</option>';
  ["loginFile", "nasFile"].forEach((id) => {
    const select = $(id);
    if (!select) return;
    select.innerHTML = html;
    select.disabled = !files.length;
    if (preferred && files.includes(preferred)) select.value = preferred;
  });
  $("nasLoadButton").disabled = !files.length;
  $("nasSaveButton").disabled = !files.length;
  return preferred;
}
async function connectNasSession(source = "login") {
  const fromLogin = source === "login" || source === "login-enter";
  const host = (fromLogin ? $("loginHost") : $("nasHost")).value.trim();
  const share = (fromLogin ? $("loginShare") : $("nasShare")).value.trim();
  const username = (fromLogin ? $("loginUser") : $("nasUser")).value.trim();
  const password = (fromLogin ? $("loginPassword") : $("nasPassword")).value;
  const remember = (fromLogin ? $("loginRemember") : $("nasRemember")).checked;
  const setState = fromLogin ? setLoginState : setNasDialogState;

  if (!host || !share || !username || !password) {
    setState("필수 항목을 모두 입력하세요.", "error");
    return { ok: false, files: [], preferred: "" };
  }
  if (!bridgeAlive) {
    const alive = await checkBridgeHealth({ quiet: false });
    if (!alive) {
      setState(bridgeDownMessage(), "error");
      return { ok: false, files: [], preferred: "" };
    }
  }

  setState("SMB 연결 중...", "pending");
  $("loginFile").disabled = true;
  $("nasFile").disabled = true;
  $("loginEnterButton").disabled = true;
  $("nasLoadButton").disabled = true;
  $("nasSaveButton").disabled = true;
  $("nasTestButton").disabled = true;

  try {
    const response = await fetch(`${API_BASE}/api/connect`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ host, share, username, password }),
    });
    if (!response.ok) throw new Error(await readErrorMessage(response));
    bridgeAlive = true;
    setBridgeOfflineBanner(false);
    const connected = await response.json();
    connectionRoot = connected.root || "";
    const filesResponse = await fetch(`${API_BASE}/api/files`);
    if (!filesResponse.ok)
      throw new Error(await readErrorMessage(filesResponse));
    const files = await filesResponse.json();
    bridgeConnected = true;
    const preferred = applyJsonFileOptions(files);
    await persistCredentials({
      host,
      share,
      username,
      password,
      remember,
      autoEnter: fromLogin ? Boolean($("loginAutoEnter")?.checked) : Boolean(loadNasPrefs().last?.autoEnter),
      file: preferred || "",
    });
    syncLoginToDialogFields();
    if (!fromLogin) {
      $("loginHost").value = host;
      $("loginShare").value = share;
      $("loginUser").value = username;
      $("loginRemember").checked = remember;
      if (remember) $("loginPassword").value = password;
    }
    if (fromLogin && $("loginAutoEnter")) {
      $("loginAutoEnter").disabled = !remember;
      if (!remember) $("loginAutoEnter").checked = false;
    }
    if (!remember) {
      $("loginPassword").value = "";
      $("nasPassword").value = "";
    }
    setState(
      files.length
        ? `연결됨 · ${files.length}개 JSON · 선택: ${preferred || "없음"}`
        : "연결됨 · JSON 파일 없음",
      "ok",
    );
    setNasDialogState(
      files.length
        ? `연결됨 · ${files.length}개 JSON · 선택: ${preferred || "없음"}`
        : "연결됨 · JSON 파일 없음",
      "ok",
    );
    updateConnectionUi({
      state: linkedFileName ? "linked" : "connected",
      label: linkedFileName ? "공유 파일 연결됨" : "SMB 연결됨",
      detail: connectionRoot
        ? `${connectionRoot} 접근 가능`
        : "공유 폴더 접근 가능. 파일을 선택하세요.",
      pathText: linkedFileName || preferred || "파일을 불러오면 여기에 표시됩니다.",
      syncText: linkedFileName
        ? "SMB 공유 데이터 사용 중"
        : "SMB 연결됨 · 파일 미선택",
      syncState: linkedFileName ? "linked" : "connected",
    });
    if (source !== "login-enter") showToast("SMB 연결을 확인했습니다.");
    return { ok: true, files, preferred };
  } catch (error) {
    bridgeConnected = false;
    $("nasLoadButton").disabled = true;
    $("nasSaveButton").disabled = true;
    if (isBridgeDownError(error)) {
      markBridgeOffline();
      setState(bridgeDownMessage(), "error");
      showToast(bridgeDownMessage());
      return { ok: false, files: [], preferred: "" };
    }
    setState(`연결 실패: ${error.message}`, "error");
    updateConnectionUi({
      state: "error",
      label: "연결 실패",
      detail: error.message,
      syncText: "NAS 연결 실패",
      syncState: "error",
    });
    showToast("로컬 브리지 실행 여부와 SMB 정보를 확인하세요.");
    return { ok: false, files: [], preferred: "" };
  } finally {
    $("loginEnterButton").disabled = false;
    $("nasTestButton").disabled = !bridgeAlive;
  }
}
async function loadSelectedBridgeFile(fileName) {
  if (!bridgeAlive) throw new Error(bridgeDownMessage());
  if (!bridgeConnected || !fileName) throw new Error("공유 파일을 선택하세요.");
  await ensureBridgeConnection(fileName);
  const response = await fetch(
    `${API_BASE}/api/file?name=` + encodeURIComponent(fileName),
    { cache: "no-store" },
  );
  if (!response.ok) throw new Error(await readErrorMessage(response));
  linkedFileRevision = response.headers.get("X-File-Revision") || "";
  const imported = await response.json();
  if (!Array.isArray(imported)) throw new Error("JSON 배열 형식이 아닙니다.");
  const remote = imported.map(normalizeScheduleItem);
  linkedFileName = fileName;
  rememberPreferredFile(fileName);
  void persistActiveScheduleFile(fileName);
  void refreshSharedNotice({ quiet: true });
  void refreshPresetsFromNas({ quiet: true });
  void refreshSpeakerRosterFromNas({ quiet: true });
  void refreshColorRulesFromNas({ quiet: true });
  void refreshFontRulesFromNas({ quiet: true });

  if (isNasDirty(fileName)) {
    let localCopy = [];
    try {
      localCopy = JSON.parse(localStorage.getItem(STORAGE_KEY) || "[]");
    } catch {
      localCopy = [];
    }
    if (Array.isArray(localCopy)) {
      const local = localCopy.map(normalizeScheduleItem);
      const localFp = JSON.stringify(local);
      const remoteFp = JSON.stringify(remote);
      if (localFp !== remoteFp) {
        schedules = local;
        setSchedulesBase(remote);
        lastSaveAt = new Date();
        lastSaveOk = null;
        localStorage.setItem(STORAGE_KEY, JSON.stringify(schedules));
        updateConnectionUi({
          state: "linked",
          label: "공유 파일 연결됨",
          detail: connectionRoot
            ? `${connectionRoot} 에서 불러옴`
            : "SMB 공유 파일을 사용 중",
          pathText: fileName,
          syncText: `미저장 변경 복구 · ${fileName}`,
          syncState: "pending",
        });
        updateSaveStatusUi("미저장 변경 복구 · 자동 저장 중…", "pending");
        void saveSharedFile({ quiet: true });
        return schedules.length;
      }
      clearNasDirty(fileName);
    }
  }

  schedules = remote;
  setSchedulesBase(schedules);
  lastSaveAt = new Date();
  lastSaveOk = true;
  clearNasDirty(fileName);
  saveLocal({ fromNas: true });
  updateConnectionUi({
    state: "linked",
    label: "공유 파일 연결됨",
    detail: connectionRoot
      ? `${connectionRoot} 에서 불러옴`
      : "SMB 공유 파일을 사용 중",
    pathText: fileName,
    syncText: `NAS 연결됨 · ${fileName}`,
    syncState: "linked",
  });
  updateSaveStatusUi(`${formatClock(lastSaveAt)}에 불러옴 (최신)`, "ok");
  return schedules.length;
}
async function backupLinkedFile() {
  if (!bridgeAlive) return showToast(bridgeDownMessage());
  if (!bridgeConnected || !linkedFileName)
    return showToast("먼저 연결된 공유 파일이 필요합니다.");
  const button = $("backupNasButton");
  const previous = button.textContent;
  button.disabled = true;
  button.textContent = "백업 중...";
  try {
    await ensureBridgeConnection(linkedFileName);
    const response = await fetch(
      `${API_BASE}/api/backup?name=` + encodeURIComponent(linkedFileName),
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(schedules, null, 2),
      },
    );
    if (!response.ok) throw new Error(await readErrorMessage(response));
    const result = await response.json();
    const kb = Math.max(1, Math.round((result.size || 0) / 1024));
    showToast(`백업 완료: _backup/${result.backup} (${kb}KB)`);
    updateSaveStatusUi(
      `백업 생성 · ${result.backup}`,
      "ok",
    );
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    showToast(`백업 실패: ${isBridgeDownError(error) ? bridgeDownMessage() : error.message}`);
  } finally {
    button.textContent = previous;
    button.disabled = !(bridgeAlive && bridgeConnected && linkedFileName);
  }
}

function formatBackupLabel(item) {
  const when = item.modified
    ? new Date(item.modified).toLocaleString("ko-KR")
    : "-";
  const kb = Math.max(1, Math.round((item.size || 0) / 1024));
  return `${item.name} · ${when} · ${kb}KB`;
}

async function refreshBackupList() {
  const select = $("restoreFileSelect");
  const confirmButton = $("restoreConfirmButton");
  const state = $("restoreState");
  select.innerHTML = '<option value="">불러오는 중...</option>';
  confirmButton.disabled = true;
  state.textContent = "백업 목록 조회 중...";
  state.dataset.state = "pending";
  try {
    await ensureBridgeConnection(linkedFileName);
    const response = await fetch(
      `${API_BASE}/api/backups?name=` + encodeURIComponent(linkedFileName),
      { cache: "no-store" },
    );
    if (!response.ok) throw new Error(await readErrorMessage(response));
    const items = await response.json();
    if (!items.length) {
      select.innerHTML = '<option value="">백업 파일이 없습니다.</option>';
      state.textContent = "_backup 폴더에 백업이 없습니다.";
      state.dataset.state = "idle";
      return;
    }
    select.innerHTML = items
      .map(
        (item) =>
          `<option value="${escapeHtml(item.name)}">${escapeHtml(formatBackupLabel(item))}</option>`,
      )
      .join("");
    confirmButton.disabled = false;
    state.textContent = `${items.length}개 백업 확인됨`;
    state.dataset.state = "ok";
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    select.innerHTML = '<option value="">목록을 불러오지 못했습니다.</option>';
    state.textContent = isBridgeDownError(error)
      ? bridgeDownMessage()
      : error.message;
    state.dataset.state = "error";
  }
}

async function openRestoreDialog() {
  if (!bridgeAlive) return showToast(bridgeDownMessage());
  if (!bridgeConnected || !linkedFileName)
    return showToast("먼저 연결된 공유 파일이 필요합니다.");
  $("restoreDialog").showModal();
  await refreshBackupList();
}

async function confirmRestoreBackup() {
  const backup = $("restoreFileSelect").value;
  if (!backup) return showToast("복원할 백업을 선택하세요.");
  if (
    !confirm(
      `"${backup}" 백업으로 현재 파일(${linkedFileName})을 덮어쓸까요?`,
    )
  )
    return;
  const button = $("restoreConfirmButton");
  const previous = button.textContent;
  button.disabled = true;
  button.textContent = "복원 중...";
  $("restoreState").textContent = "복원 중...";
  $("restoreState").dataset.state = "pending";
  try {
    await ensureBridgeConnection(linkedFileName);
    const response = await fetch(
      `${API_BASE}/api/restore?name=` +
        encodeURIComponent(linkedFileName) +
        "&backup=" +
        encodeURIComponent(backup),
      { method: "POST" },
    );
    if (!response.ok) throw new Error(await readErrorMessage(response));
    linkedFileRevision = response.headers.get("X-File-Revision") || "";
    const imported = await response.json();
    if (!Array.isArray(imported)) throw new Error("JSON 배열 형식이 아닙니다.");
    schedules = imported.map(normalizeScheduleItem);
    setSchedulesBase(schedules);
    lastSaveAt = new Date();
    lastSaveOk = true;
    clearNasDirty(linkedFileName);
    saveLocal({ fromNas: true });
    render();
    updateConnectionUi({
      state: "linked",
      label: "공유 파일 연결됨",
      detail: `백업 복원 완료 · ${backup}`,
      pathText: linkedFileName,
      syncText: `복원됨 · ${formatClock(lastSaveAt)}`,
      syncState: "linked",
    });
    updateSaveStatusUi(`${formatClock(lastSaveAt)}에 복원 완료`, "ok");
    $("restoreState").textContent = "복원 완료";
    $("restoreState").dataset.state = "ok";
    $("restoreDialog").close();
    showToast(`${schedules.length}개 일정을 백업에서 복원했습니다.`);
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    $("restoreState").textContent = isBridgeDownError(error)
      ? bridgeDownMessage()
      : error.message;
    $("restoreState").dataset.state = "error";
    showToast(`복원 실패: ${error.message}`);
  } finally {
    button.textContent = previous;
    button.disabled = !$("restoreFileSelect").value;
  }
}

async function saveSharedFile({
  closeDialog = false,
  quiet = false,
  isRetry = false,
} = {}) {
  if (!bridgeAlive) {
    if (!quiet) showToast(bridgeDownMessage());
    return false;
  }
  const fileName = closeDialog
    ? $("nasFile").value || linkedFileName
    : linkedFileName || $("nasFile").value;
  if (!bridgeConnected && !fileName) {
    if (!quiet) showToast("먼저 SMB 연결을 확인하세요.");
    return false;
  }
  if (!fileName) {
    if (!quiet) showToast("먼저 공유 파일을 선택하세요.");
    return false;
  }
  const saveButton = closeDialog ? $("nasSaveButton") : $("saveNasButton");
  const previousLabel = saveButton.textContent;
  if (!quiet) {
    saveButton.disabled = true;
    saveButton.textContent = "저장 중...";
    setNasDialogState("연결 유효성 확인 중...", "pending");
  }
  updateSaveStatusUi(
    quiet ? "자동 저장 중…" : "연결 확인 후 저장 중...",
    "pending",
  );
  try {
    await ensureBridgeConnection(fileName);
    if (!quiet) setNasDialogState("연결 확인됨. 저장 중...", "ok");
    const headers = { "Content-Type": "application/json" };
    if (linkedFileRevision) headers["X-Expected-Revision"] = linkedFileRevision;
    const response = await fetch(
      `${API_BASE}/api/file?name=` + encodeURIComponent(fileName),
      {
        method: "PUT",
        headers,
        body: JSON.stringify(schedules, null, 2),
      },
    );

    if (response.status === 409) {
      if (isRetry) throw new Error("충돌 병합 후에도 저장에 실패했습니다.");
      let payload;
      try {
        payload = await response.json();
      } catch {
        throw new Error("파일이 다른 곳에서 변경되었습니다.");
      }
      const remoteRaw = Array.isArray(payload.content)
        ? payload.content
        : typeof payload.content === "string"
          ? JSON.parse(payload.content)
          : null;
      if (!Array.isArray(remoteRaw))
        throw new Error("충돌 원격 데이터를 읽지 못했습니다.");
      const remote = remoteRaw.map(normalizeScheduleItem);
      const { merged, conflictCount } = mergeSchedules(
        schedulesBase,
        schedules,
        remote,
      );
      schedules = merged;
      linkedFileRevision = payload.revision || "";
      setSchedulesBase(remote);
      localStorage.setItem(STORAGE_KEY, JSON.stringify(schedules));
      render();
      const mergeMessage =
        conflictCount > 0
          ? `다른 PC 변경과 병합했습니다. 같은 일정 ${conflictCount}건은 이 PC 내용 유지`
          : "다른 PC 변경과 병합했습니다.";
      showToast(mergeMessage);
      updateSaveStatusUi("병합 후 다시 저장 중…", "pending");
      return await saveSharedFile({ closeDialog, quiet, isRetry: true });
    }

    if (!response.ok) throw new Error(await readErrorMessage(response));
    let savedRevision = "";
    try {
      const saved = await response.json();
      savedRevision = saved.revision || "";
    } catch {
      savedRevision = "";
    }
    linkedFileName = fileName;
    linkedFileRevision = savedRevision || linkedFileRevision;
    setSchedulesBase(schedules);
    lastSaveAt = new Date();
    lastSaveOk = true;
    clearNasDirty(fileName);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(schedules));
    updateConnectionUi({
      state: "linked",
      label: "공유 파일 연결됨",
      detail: connectionRoot
        ? `${connectionRoot} 에 연결됨`
        : "SMB 공유 폴더에 연결됨",
      pathText: fileName,
      syncText: `NAS 저장됨 · ${formatClock(lastSaveAt)}`,
      syncState: "linked",
    });
    updateSaveStatusUi(`${formatClock(lastSaveAt)}에 저장 완료`, "ok");
    void syncOfficialDocumentIndex();
    if (!quiet) {
      setNasDialogState("저장 완료", "ok");
      showToast("NAS 공유 파일에 저장했습니다.");
      if (closeDialog) $("nasDialog").close();
    }
    return true;
  } catch (error) {
    if (isBridgeDownError(error)) markBridgeOffline();
    lastSaveAt = new Date();
    lastSaveOk = false;
    const message = isBridgeDownError(error)
      ? bridgeDownMessage()
      : error.message;
    updateSaveStatusUi(
      `${formatClock(lastSaveAt)} 저장 실패: ${message}`,
      "error",
    );
    if (!quiet) setNasDialogState(`저장 실패: ${message}`, "error");
    $("syncStatus").textContent = bridgeAlive
      ? "NAS 저장 실패"
      : "서버 꺼짐 · 로컬만 사용";
    $("syncStatus").dataset.state = "error";
    showToast(`NAS 저장 실패: ${message}`);
    return false;
  } finally {
    if (!quiet) saveButton.textContent = previousLabel;
    const canUseNas = bridgeAlive && bridgeConnected;
    $("saveNasButton").disabled = !(canUseNas && linkedFileName);
    $("nasSaveButton").disabled = !(
      canUseNas &&
      (linkedFileName || $("nasFile").value)
    );
  }
}
async function testLocalBridge() {
  await connectNasSession("dialog");
}
async function loadBridgeFile() {
  if (!bridgeAlive) return showToast(bridgeDownMessage());
  if (!bridgeConnected || !$("nasFile").value) return;
  const fileName = $("nasFile").value;
  setNasDialogState("파일 불러오는 중...", "pending");
  try {
    const count = await loadSelectedBridgeFile(fileName);
    if ($("loginFile")) $("loginFile").value = fileName;
    setNasDialogState(`불러오기 완료 · ${count}개 일정`, "ok");
    showToast(`${count}개 일정을 NAS에서 불러왔습니다.`);
    if (!appEntered) enterAppShell();
    else render();
  } catch (error) {
    if (isBridgeDownError(error)) {
      markBridgeOffline();
      setNasDialogState(bridgeDownMessage(), "error");
      showToast(bridgeDownMessage());
      return;
    }
    setNasDialogState(`불러오기 실패: ${error.message}`, "error");
    showToast(`NAS 불러오기 실패: ${error.message}`);
  }
}
async function connectAndEnterBoard(event) {
  event.preventDefault();
  setLoginState("연결하고 시작하는 중...", "pending");
  const result = await connectNasSession("login-enter");
  if (!result?.ok) return;
  const fileName = $("loginFile").value || result.preferred || "";
  try {
    if (fileName) {
      setLoginState("파일 불러오는 중...", "pending");
      const count = await loadSelectedBridgeFile(fileName);
      if ($("nasFile")) $("nasFile").value = fileName;
      setLoginState(`불러오기 완료 · ${count}개 일정`, "ok");
      showToast(`${count}개 일정을 NAS에서 불러왔습니다.`);
    } else {
      setLoginState("로컬 데이터로 시작합니다.", "ok");
      showToast("파일 없이 로컬 데이터로 시작합니다.");
    }
    enterAppShell();
  } catch (error) {
    if (isBridgeDownError(error)) {
      markBridgeOffline();
      setLoginState(bridgeDownMessage(), "error");
      return showToast(bridgeDownMessage());
    }
    setLoginState(`시작 실패: ${error.message}`, "error");
    showToast(`시작 실패: ${error.message}`);
  }
}
function openNasDialog() {
  $("nasDialog").showModal();
}
function webdavUrl(path) {
  const scheme = Number($("nasPort").value) === 5006 ? "https" : "http";
  const host = $("nasHost").value.trim();
  const port = $("nasPort").value.trim();
  const base = $("nasSharePath").value.trim().replace(/\/$/, "");
  return `${scheme}://${host}:${port}${path || base}`;
}
function webdavHeaders() {
  return {
    Authorization: `Basic ${btoa(`${$("nasUser").value}:${$("nasPassword").value}`)}`,
  };
}
async function testNasConnection() {
  const state = $("nasConnectionState");
  const fileSelect = $("nasFile");
  state.textContent = "연결 확인 중...";
  fileSelect.disabled = true;
  try {
    const response = await fetch(webdavUrl(), {
      method: "PROPFIND",
      headers: { ...webdavHeaders(), Depth: "1" },
    });
    if (!response.ok && response.status !== 207)
      throw new Error(`HTTP ${response.status}`);
    const xml = new DOMParser().parseFromString(
      await response.text(),
      "application/xml",
    );
    const files = [...xml.getElementsByTagNameNS("*", "response")]
      .map((node) => node.getElementsByTagNameNS("*", "href")[0]?.textContent)
      .filter((href) => href && /\.json(?:$|\?)/i.test(href))
      .map((href) => decodeURIComponent(href));
    fileSelect.innerHTML = files.length
      ? files
          .map(
            (path) =>
              `<option value="${escapeHtml(path)}">${escapeHtml(path.split("/").pop())}</option>`,
          )
          .join("")
      : '<option value="">schedule-data.json을 찾을 수 없습니다.</option>';
    fileSelect.disabled = !files.length;
    state.textContent = files.length
      ? `${files.length}개 JSON 파일 확인됨`
      : "연결됨, JSON 파일 없음";
    $("nasLoadButton").disabled = !files.length;
    $("nasSaveButton").disabled = !files.length;
    nasConfig = { connected: true };
  } catch (error) {
    nasConfig = null;
    $("nasLoadButton").disabled = true;
    $("nasSaveButton").disabled = true;
    state.textContent = `연결 실패: ${error.message}`;
    showToast("WebDAV 연결에 실패했습니다. CORS 또는 DSM 설정을 확인하세요.");
  }
}
async function loadNasFile() {
  if (!nasConfig || !$("nasFile").value) return;
  try {
    const response = await fetch(webdavUrl($("nasFile").value), {
      headers: webdavHeaders(),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const imported = JSON.parse(await response.text());
    if (!Array.isArray(imported)) throw new Error("JSON 배열 형식이 아닙니다.");
    schedules = imported.map((item) => normalizeScheduleItem(item));
    saveLocal();
    render();
    $("syncStatus").textContent = "WebDAV 데이터 사용 중";
    showToast(`${schedules.length}개 일정을 NAS에서 불러왔습니다.`);
  } catch (error) {
    showToast(`NAS 불러오기 실패: ${error.message}`);
  }
}
async function saveNasFile() {
  if (!nasConfig || !$("nasFile").value) return;
  try {
    const response = await fetch(webdavUrl($("nasFile").value), {
      method: "PUT",
      headers: { ...webdavHeaders(), "Content-Type": "application/json" },
      body: JSON.stringify(schedules, null, 2),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    showToast("현재 데이터를 NAS에 저장했습니다.");
  } catch (error) {
    showToast(`NAS 저장 실패: ${error.message}`);
  }
}
initTimePickerControls();
document.querySelector(".place-picker").onclick = (event) => {
  const button = event.target.closest("[data-place]");
  if (!button) return;
  setPlaceValue(button.dataset.place);
};
$("colorSwatches").onclick = (event) => {
  const button = event.target.closest("[data-color]");
  if (!button) return;
  setScheduleColorValue(button.dataset.color);
  activePresetId = null;
  renderPresetList();
};
document.querySelector("#scheduleDialog .color-picker .color-mode-row")?.addEventListener("click", (event) => {
  const button = event.target.closest("[data-color-mode]");
  if (!button) return;
  const mode = button.dataset.colorMode;
  if (mode === COLOR_MODE_CUSTOM) {
    const current = getFormColorState();
    const customColor =
      current.colorMode === COLOR_MODE_CUSTOM
        ? current.color
        : DEFAULT_SCHEDULE_COLOR;
    setFormColorState(COLOR_MODE_CUSTOM, customColor);
  } else {
    setFormColorState(mode, mode);
  }
  activePresetId = null;
  renderPresetList();
});
$("scheduleColorCustom") &&
  ($("scheduleColorCustom").oninput = (event) => {
    const value = event.target.value;
    if (!isHexColor(value)) return;
    setScheduleColorValue(value.toLowerCase());
    activePresetId = null;
    renderPresetList();
  });
$("presetList").onclick = (event) => {
  const deleteButton = event.target.closest("[data-preset-delete]");
  if (deleteButton) {
    event.preventDefault();
    event.stopPropagation();
    const id = deleteButton.dataset.presetDelete;
    if (confirm("이 프리셋을 삭제할까요?")) deleteUserPreset(id);
    return;
  }
  const editButton = event.target.closest("[data-preset-edit]");
  if (editButton) {
    event.preventDefault();
    beginEditPreset(editButton.dataset.presetEdit);
    return;
  }
  const upButton = event.target.closest("[data-preset-up]");
  if (upButton) {
    event.preventDefault();
    movePreset(upButton.dataset.presetUp, -1);
    return;
  }
  const downButton = event.target.closest("[data-preset-down]");
  if (downButton) {
    event.preventDefault();
    movePreset(downButton.dataset.presetDown, 1);
    return;
  }
  if (presetManageMode) return;
  const chip = event.target.closest("[data-preset-id]");
  if (!chip || chip.closest(".preset-manage-row")) return;
  const preset = getAllPresets().find((item) => item.id === chip.dataset.presetId);
  if (preset) applyPreset(preset);
};
$("presetManageToggle")?.addEventListener("click", () => {
  setPresetManageMode(!presetManageMode);
});
$("presetCreateToggle").onclick = () => {
  const panel = $("presetCreatePanel");
  const opening = panel?.classList.contains("hidden");
  if (opening) {
    editingPresetId = null;
    const saveBtn = $("presetSaveButton");
    if (saveBtn) saveBtn.textContent = "현재 내용으로 저장";
    setPresetFieldChecks(Object.keys(PRESET_FIELD_LABELS));
    setPresetManageMode(false);
  }
  setPresetCreatePanelOpen(opening);
};
$("presetCreateCancel").onclick = () => setPresetCreatePanelOpen(false);
$("presetSaveButton").onclick = () => savePresetFromForm();
$("newScheduleButton").onclick = () => openDialog();
$("bulkScheduleButton").onclick = () => openBulkScheduleDialog();
$("bulkScheduleCloseButton").onclick = closeBulkScheduleDialog;
$("bulkScheduleCancel").onclick = closeBulkScheduleDialog;
$("bulkScheduleAddRow")?.addEventListener("click", (event) => {
  event.preventDefault();
  event.stopPropagation();
  appendBulkScheduleRow();
});
$("bulkScheduleDialog")?.addEventListener("click", (event) => {
  const btn = event.target.closest("#bulkScheduleAddRow");
  if (!btn) return;
  event.preventDefault();
  event.stopPropagation();
  appendBulkScheduleRow();
});
$("bulkScheduleSave").onclick = () => submitBulkSchedule();
$("importButton").onclick = () => openImportModeDialog();
$("exportButton").onclick = downloadJson;
$("exportSelectToggle").onclick = () =>
  setExportSelectMode(!exportSelectMode);
$("exportSelectAllButton").onclick = () => selectVisibleSchedulesForExport();
$("exportSelectClearButton").onclick = () => clearExportSelection();
$("exportSelectedButton").onclick = () => downloadSelectedJson();
$("exportSelectExitButton").onclick = () => setExportSelectMode(false);
$("importModeClose").onclick = () => $("importModeDialog").close();
$("importModeCancel").onclick = () => $("importModeDialog").close();
document.querySelectorAll("[data-import-mode]").forEach((button) => {
  button.onclick = () => chooseImportMode(button.dataset.importMode);
});
$("connectNasButton").onclick = selectSharedFile;
$("nasPath")?.addEventListener("click", () => {
  const full = $("nasPath")?.dataset.fullPath || $("nasPath")?.title || "";
  if (!full || full === "아직 연결된 파일이 없습니다.") return;
  showToast(full);
});
$("nasPath")?.addEventListener("keydown", (event) => {
  if (event.key !== "Enter" && event.key !== " ") return;
  event.preventDefault();
  $("nasPath")?.click();
});
function applyBulkColorWrap(wrap, value) {
  if (!wrap) return;
  const state = normalizeColorState("", value);
  const hidden = wrap.querySelector('[data-bulk="color"]');
  if (hidden) hidden.value = encodeColorValue(state);
  wrap.querySelectorAll("[data-bulk-color-mode]").forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.bulkColorMode === state.colorMode);
  });
  wrap
    .querySelector(".bulk-color-custom-panel")
    ?.classList.toggle("hidden", state.colorMode !== COLOR_MODE_CUSTOM);
  if (state.colorMode === COLOR_MODE_CUSTOM) {
    wrap.querySelectorAll("[data-bulk-color]").forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.bulkColor === state.color);
    });
    wrap
      .querySelector(".bulk-color-custom")
      ?.classList.toggle("active", isHexColor(state.color));
  }
}
$("bulkScheduleBody")?.addEventListener("click", (event) => {
  const remove = event.target.closest("[data-entry-bulk-remove]");
  if (remove) {
    const body = $("bulkScheduleBody");
    const row = remove.closest("tr");
    if (!body || !row) return;
    if (body.querySelectorAll("tr").length <= 1) {
      showToast("최소 한 행은 남겨 주세요.");
      return;
    }
    row.remove();
    return;
  }
  const modeBtn = event.target.closest("[data-bulk-color-mode]");
  if (modeBtn) {
    const wrap = modeBtn.closest(".bulk-color-swatches");
    const mode = modeBtn.dataset.bulkColorMode;
    if (mode === COLOR_MODE_CUSTOM) {
      applyBulkColorWrap(wrap, DEFAULT_SCHEDULE_COLOR);
    } else {
      applyBulkColorWrap(wrap, mode);
    }
    return;
  }
  const colorBtn = event.target.closest("[data-bulk-color]");
  if (!colorBtn) return;
  const wrap = colorBtn.closest(".bulk-color-swatches");
  applyBulkColorWrap(wrap, colorBtn.dataset.bulkColor);
});
$("bulkScheduleBody")?.addEventListener("input", (event) => {
  const custom = event.target.closest("[data-bulk-color-custom]");
  if (!custom) return;
  if (!isHexColor(custom.value)) return;
  applyBulkColorWrap(custom.closest(".bulk-color-swatches"), custom.value.toLowerCase());
});
$("saveNasButton").onclick = () => {
  if (autoSaveTimer) {
    clearTimeout(autoSaveTimer);
    autoSaveTimer = null;
  }
  saveSharedFile({ closeDialog: false });
};
$("backupNasButton").onclick = backupLinkedFile;
$("restoreNasButton").onclick = openRestoreDialog;
$("restoreRefreshButton").onclick = refreshBackupList;
$("restoreCancelButton").onclick = () => $("restoreDialog").close();
$("restoreCloseButton").onclick = () => $("restoreDialog").close();
$("restoreConfirmButton").onclick = confirmRestoreBackup;
$("restoreFileSelect").onchange = () => {
  $("restoreConfirmButton").disabled = !$("restoreFileSelect").value;
};
$("nasTestButton").onclick = testLocalBridge;
$("nasLoadButton").onclick = loadBridgeFile;
$("nasSaveButton").onclick = () => {
  if (autoSaveTimer) {
    clearTimeout(autoSaveTimer);
    autoSaveTimer = null;
  }
  saveSharedFile({ closeDialog: true });
};
$("nasCloseButton").onclick = () => $("nasDialog").close();
$("loginForm").onsubmit = connectAndEnterBoard;
$("loginSkipButton").onclick = () => {
  enterAppShell();
  showToast("로컬 저장소로 시작합니다. NAS는 나중에 연결할 수 있습니다.");
};
$("loginShareDefaultButton").onclick = () => {
  applyDefaultSharePath({ force: true });
  showToast(`공유 폴더명을 기본값으로 설정했습니다: ${DEFAULT_SHARE_PATH}`);
};
$("nasShareDefaultButton").onclick = () => {
  applyDefaultSharePath({ force: true });
  showToast(`공유 폴더명을 기본값으로 설정했습니다: ${DEFAULT_SHARE_PATH}`);
};
document.querySelectorAll(".host-mode").forEach((button) => {
  button.onclick = () => setLoginHostMode(button.dataset.hostMode);
});
$("lanScanButton").onclick = (event) => {
  event.preventDefault();
  if (lanScanInFlight || Date.now() < lanScanCooldownUntil) {
    const waitSec = Math.max(
      1,
      Math.ceil((lanScanCooldownUntil - Date.now()) / 1000),
    );
    if ($("lanScanState")) {
      $("lanScanState").textContent = lanScanInFlight
        ? "이미 검색 중입니다…"
        : `${waitSec}초 후 다시 검색할 수 있습니다`;
    }
    return;
  }
  void scanLanDevices();
};
$("lanDeviceList").onclick = (event) => {
  const button = event.target.closest(".lan-device");
  if (!button) return;
  const ip = button.dataset.ip || "";
  $("loginHost").value = ip;
  if ($("nasHost")) $("nasHost").value = ip;
  syncLanDeviceSelection(ip);
  showToast(`NAS 주소 선택: ${ip}`);
};
$("loginHost").addEventListener("change", async () => {
  syncLanDeviceSelection($("loginHost").value.trim());
  const prefs = loadNasPrefs();
  if (prefs.last?.host === $("loginHost").value.trim()) {
    await fillLoginFromPrefs();
  }
});
$("loginHost").addEventListener("input", () => {
  syncLanDeviceSelection($("loginHost").value.trim());
});
$("nasFile").onchange = () => {
  $("nasLoadButton").disabled = !(bridgeConnected && $("nasFile").value);
  $("nasSaveButton").disabled = !(
    bridgeConnected &&
    (linkedFileName || $("nasFile").value)
  );
};
$("loginFile").onchange = () => {
  const fileName = $("loginFile").value;
  if ($("nasFile") && fileName) $("nasFile").value = fileName;
  if (fileName) rememberPreferredFile(fileName);
};
document.querySelectorAll(".view-mode").forEach((button) => {
  button.onclick = () => setViewMode(button.dataset.view);
});
$("todaySchedulesButton").onclick = () => {
  const today = new Date();
  calendarMonth = startOfMonth(today);
  updateMonthlyScheduleButtonLabel();
  updateMonthlyOfficialDocsButtonLabel();
  updateClearCalendarViewPreview();
  if (viewMode === "calendar") render();
  openDayDetail(dateInputValue(today));
};
$("monthlyScheduleButton") &&
  ($("monthlyScheduleButton").onclick = () => {
    openMonthlyScheduleImage().catch((error) =>
      showToast(error.message || String(error)),
    );
  });
$("monthlyOfficialDocsButton") &&
  ($("monthlyOfficialDocsButton").onclick = () => {
    openMonthlyOfficialDocsDialog().catch((error) =>
      showToast(error.message || String(error)),
    );
  });
$("monthlyOfficialDocsClose")?.addEventListener("click", () =>
  closeMonthlyOfficialDocsDialog(),
);
$("monthlyOfficialDocsDone")?.addEventListener("click", () =>
  closeMonthlyOfficialDocsDialog(),
);
$("monthlyOfficialDocsList")?.addEventListener("click", (event) => {
  const button = event.target.closest("[data-doc]");
  if (!button) return;
  const fileName = button.dataset.doc;
  if (!fileName) return;
  closeMonthlyOfficialDocsDialog();
  openOfficialDocumentViewer(fileName).catch((error) =>
    showToast(error.message || String(error)),
  );
});
$("calendarPrev").onclick = () => {
  calendarMonth = new Date(
    calendarMonth.getFullYear(),
    calendarMonth.getMonth() - 1,
    1,
  );
  updateClearCalendarViewPreview();
  render();
};
$("calendarNext").onclick = () => {
  calendarMonth = new Date(
    calendarMonth.getFullYear(),
    calendarMonth.getMonth() + 1,
    1,
  );
  updateClearCalendarViewPreview();
  render();
};
$("calendarToday").onclick = () => {
  calendarMonth = startOfMonth(new Date());
  updateClearCalendarViewPreview();
  render();
};
$("calendarGrid").onclick = (event) => {
  const selectTarget = event.target.closest("[data-export-select]");
  if (selectTarget) {
    event.stopPropagation();
    toggleExportSelection(selectTarget.dataset.exportSelect);
    render();
    return;
  }
  const dayHead = event.target.closest("[data-day]");
  if (dayHead) {
    event.stopPropagation();
    const dateKey = dayHead.classList.contains("calendar-event")
      ? dateKeyFromCalendarSpanClick(event, dayHead.dataset.day)
      : dayHead.dataset.day;
    openDayDetail(dateKey);
    return;
  }
  if (exportSelectMode) return;
  const cell = event.target.closest(".calendar-cell[data-date]");
  if (cell) openDayDetail(cell.dataset.date);
};
$("scheduleList").onclick = (event) => {
  const collapseBtn = event.target.closest("[data-collapse-day]");
  if (collapseBtn) {
    const day = collapseBtn.dataset.collapseDay;
    if (listCollapsedDates.has(day)) listCollapsedDates.delete(day);
    else listCollapsedDates.add(day);
    renderList();
    return;
  }
  const selectInput = event.target.closest("[data-export-select]");
  if (selectInput) {
    toggleExportSelection(
      selectInput.dataset.exportSelect,
      selectInput.checked,
    );
    const card = selectInput.closest(".schedule-card");
    card?.classList.toggle("is-export-selected", selectInput.checked);
    return;
  }
  const dayButton = event.target.closest("[data-day]");
  if (dayButton) {
    openDayDetail(dayButton.dataset.day);
    return;
  }
  if (exportSelectMode) return;
  const editButton = event.target.closest("[data-edit]");
  const deleteButton = event.target.closest("[data-delete]");
  if (editButton)
    openDialog(schedules.find((item) => item.id === editButton.dataset.edit));
  if (deleteButton) {
    removeScheduleWithLinkPrompt(deleteButton.dataset.delete);
  }
};
$("dayDetailList").onclick = (event) => {
  const selectInput = event.target.closest("[data-export-select]");
  if (selectInput) {
    toggleExportSelection(
      selectInput.dataset.exportSelect,
      selectInput.checked,
    );
    const row = selectInput.closest(".day-detail-item");
    row?.classList.toggle("is-export-selected", selectInput.checked);
    return;
  }
  const docButton = event.target.closest("[data-doc]");
  if (docButton && docButton.dataset.doc) {
    openOfficialDocumentViewer(docButton.dataset.doc);
    return;
  }
  if (exportSelectMode) return;
  const editButton = event.target.closest("[data-edit]");
  const deleteButton = event.target.closest("[data-delete]");
  if (editButton) {
    $("dayDetailDialog").close();
    openDialog(schedules.find((item) => item.id === editButton.dataset.edit));
    return;
  }
  if (deleteButton) {
    if (!removeScheduleWithLinkPrompt(deleteButton.dataset.delete)) return;
    openDayDetail($("dayDetailAdd").dataset.date);
  }
};
$("dayDetailAdd").onclick = () => {
  const date = $("dayDetailAdd").dataset.date;
  $("dayDetailDialog").close();
  openDialog(null, date);
};
$("dayDetailClose").onclick = () => $("dayDetailDialog").close();
$("dayDetailCloseButton").onclick = () => $("dayDetailDialog").close();
$("officialDocUploadButton").onclick = () => $("officialDocFileInput").click();
$("officialDocFileInput").onchange = async (event) => {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  await uploadOfficialDocumentFile(file);
};
$("officialDocLinkButton").onclick = () => openOfficialDocLinkDialog();
$("officialDocViewButton").onclick = () =>
  openOfficialDocumentViewer(pendingOfficialDocument);
$("dialogOfficialDocViewButton") &&
  ($("dialogOfficialDocViewButton").onclick = () =>
    openOfficialDocumentViewer(pendingOfficialDocument));
$("officialDocClearButton").onclick = () => {
  pendingOfficialDocument = "";
  updateOfficialDocumentUi();
  showToast("공문 연결을 해제했습니다. 일정 저장 시 반영됩니다.");
};
$("officialDocViewerClose").onclick = () => $("officialDocViewerDialog").close();
$("officialDocViewerCloseButton").onclick = () =>
  $("officialDocViewerDialog").close();
$("officialDocMakeScheduleButton") &&
  ($("officialDocMakeScheduleButton").onclick = () => {
    const current = officialViewerPages[officialViewerIndex];
    const fileName = current?.fileName || pendingOfficialDocument;
    openScheduleFromOfficialDocument(fileName);
  });
$("officialDocViewerPrev") &&
  ($("officialDocViewerPrev").onclick = () => {
    void showOfficialViewerPage(officialViewerIndex - 1).catch((error) =>
      showToast(`공문 페이지 이동 실패: ${error.message}`),
    );
  });
$("officialDocViewerNext") &&
  ($("officialDocViewerNext").onclick = () => {
    void showOfficialViewerPage(officialViewerIndex + 1).catch((error) =>
      showToast(`공문 페이지 이동 실패: ${error.message}`),
    );
  });
$("officialDocViewerZoomOut") &&
  ($("officialDocViewerZoomOut").onclick = () =>
    setOfficialViewerZoom(officialViewerZoom - OFFICIAL_VIEWER_ZOOM_STEP));
$("officialDocViewerZoomIn") &&
  ($("officialDocViewerZoomIn").onclick = () =>
    setOfficialViewerZoom(officialViewerZoom + OFFICIAL_VIEWER_ZOOM_STEP));
$("officialDocViewerZoomReset") &&
  ($("officialDocViewerZoomReset").onclick = () => resetOfficialViewerZoom());
$("officialDocLinkClose").onclick = () => $("officialDocLinkDialog").close();
$("officialDocLinkCancel").onclick = () => $("officialDocLinkDialog").close();
$("officialDocLinkConfirm").onclick = () => confirmOfficialDocLink();
$("officialDocLinkSelect").onchange = () => {
  $("officialDocLinkConfirm").disabled = !$("officialDocLinkSelect").value;
};
$("scheduleCloseButton").onclick = closeScheduleDialog;
$("cancelButton").onclick = closeScheduleDialog;
$("deleteScheduleButton").onclick = deleteCurrentSchedule;
$("scheduleForm").onsubmit = (event) => {
  event.preventDefault();
  submitSchedule();
};
document
  .querySelectorAll("#scheduleDialog .mode")
  .forEach((button) => (button.onclick = () => setMode(button.dataset.mode)));
$("bulkAddRowButton") &&
  ($("bulkAddRowButton").onclick = () => {
    const first = $("bulkScheduleRows")?.querySelector("tr");
    addBulkScheduleRows(1, {
      scheduleDate:
        first?.querySelector('[data-bulk="date"]')?.value ||
        $("startDate")?.value,
      releaseDate:
        first?.querySelector('[data-bulk="end"]')?.value ||
        first?.querySelector('[data-bulk="date"]')?.value ||
        $("startDate")?.value,
    });
  });
$("bulkClearRowsButton") &&
  ($("bulkClearRowsButton").onclick = () => clearBulkScheduleRows());
$("bulkFillDatesButton") &&
  ($("bulkFillDatesButton").onclick = () => fillBulkEmptyDatesFromFirst());
$("bulkScheduleRows")?.addEventListener("click", (event) => {
  const modeBtn = event.target.closest("[data-bulk-color-mode]");
  if (modeBtn) {
    const wrap = modeBtn.closest(".bulk-color-swatches");
    const mode = modeBtn.dataset.bulkColorMode;
    applyBulkColorWrap(
      wrap,
      mode === COLOR_MODE_CUSTOM ? DEFAULT_SCHEDULE_COLOR : mode,
    );
    return;
  }
  const colorBtn = event.target.closest("[data-bulk-color]");
  if (colorBtn) {
    applyBulkColorWrap(
      colorBtn.closest(".bulk-color-swatches"),
      colorBtn.dataset.bulkColor,
    );
    return;
  }
  const remove = event.target.closest("[data-bulk-remove]");
  if (!remove) return;
  const body = $("bulkScheduleRows");
  const row = remove.closest("tr");
  if (!body || !row) return;
  if (body.querySelectorAll("tr").length <= 1) {
    showToast("최소 한 행은 남겨 주세요.");
    return;
  }
  row.remove();
  updateBulkRowCountLabel();
});
$("bulkScheduleRows")?.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  const titleInput = event.target.closest('[data-bulk="title"]');
  if (!titleInput) return;
  event.preventDefault();
  const row = titleInput.closest("tr");
  const body = $("bulkScheduleRows");
  if (!row || !body) return;
  let next = row.nextElementSibling?.querySelector('[data-bulk="title"]');
  if (!next) {
    addBulkScheduleRows(1, {
      scheduleDate:
        row.querySelector('[data-bulk="date"]')?.value ||
        $("startDate")?.value,
      releaseDate:
        row.querySelector('[data-bulk="end"]')?.value ||
        row.querySelector('[data-bulk="date"]')?.value ||
        $("startDate")?.value,
    });
    next = body.lastElementChild?.querySelector('[data-bulk="title"]');
  }
  next?.focus();
});
$("bulkScheduleRows")?.addEventListener("input", (event) => {
  const custom = event.target.closest("[data-bulk-color-custom]");
  if (!custom) return;
  const value = custom.value;
  if (!isHexColor(value)) return;
  applyBulkColorWrap(custom.closest(".bulk-color-swatches"), value.toLowerCase());
});
["searchInput"].forEach((id) => {
  const el = $(id);
  if (el) el.oninput = render;
});
function onListRangeChange() {
  saveListRange();
  render();
}
["listFromDate", "listToDate"].forEach((id) => {
  const el = $(id);
  if (el) el.onchange = onListRangeChange;
});
$("listRangeClear") &&
  ($("listRangeClear").onclick = () => {
    if ($("listFromDate")) $("listFromDate").value = "";
    if ($("listToDate")) $("listToDate").value = "";
    saveListRange();
    render();
  });
$("speakerPaButton").onclick = () => appendSpeakerPaSuffix();
$("speakerGwangjuButton").onclick = (event) => {
  event.preventDefault();
  event.stopPropagation();
  toggleSpeakerPick("gwangju", event.currentTarget);
};
$("appSettingsButton").onclick = () => openAppSettings("speakers");
$("appSettingsClose").onclick = () => closeAppSettings();
$("appSettingsDone").onclick = () => closeAppSettings();
document.querySelectorAll("[data-settings-tab]").forEach((btn) => {
  btn.onclick = () => openAppSettings(btn.dataset.settingsTab || "speakers");
});
$("clearMonthInput") &&
  ($("clearMonthInput").oninput = () => updateClearMonthPreview());
$("clearMonthButton") &&
  ($("clearMonthButton").onclick = () => clearSchedulesForSelectedMonth());
$("clearCalendarViewButton") &&
  ($("clearCalendarViewButton").onclick = () =>
    clearSchedulesForCurrentCalendarView());
function tryAddImportExcludeFromForm() {
  const pattern = $("importExcludePattern")?.value;
  if (!addImportExcludeRule(pattern)) return;
  if ($("importExcludePattern")) $("importExcludePattern").value = "";
  renderImportExcludeEditor();
}
$("importExcludeAddBtn")?.addEventListener("click", () => tryAddImportExcludeFromForm());
$("importExcludePattern")?.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  event.preventDefault();
  tryAddImportExcludeFromForm();
});
$("importExcludeList")?.addEventListener("click", (event) => {
  const move = event.target.closest("[data-import-exclude-move]");
  if (move) {
    moveImportExcludeRule(
      move.dataset.importExcludeMove,
      Number(move.dataset.importExcludeDelta) || 0,
    );
    renderImportExcludeEditor();
    return;
  }
  const btn = event.target.closest("[data-import-exclude-remove]");
  if (!btn) return;
  removeImportExcludeRule(btn.dataset.importExcludeRemove);
  renderImportExcludeEditor();
});
window.addEventListener("resize", () => {
  if (viewMode === "calendar") syncCalendarWeekHeights();
});
function tryAddRosterFromInput(group, inputId) {
  const input = $(inputId);
  if (!addSpeakerRosterName(group, input?.value)) return;
  if (input) input.value = "";
  renderSpeakerRosterEditor();
}
$("speakerRosterAddGwangjuBtn").onclick = () =>
  tryAddRosterFromInput("gwangju", "speakerRosterAddGwangju");
$("speakerRosterAddGwangju")?.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  event.preventDefault();
  tryAddRosterFromInput("gwangju", "speakerRosterAddGwangju");
});
$("speakerRosterListGwangju")?.addEventListener("click", (event) => {
  const move = event.target.closest("[data-roster-move]");
  if (move) {
    moveSpeakerRosterName(
      move.dataset.rosterMove,
      move.dataset.rosterName,
      Number(move.dataset.rosterDelta) || 0,
    );
    renderSpeakerRosterEditor();
    return;
  }
  const btn = event.target.closest("[data-roster-remove]");
  if (!btn) return;
  removeSpeakerRosterName(btn.dataset.rosterRemove, btn.dataset.rosterName);
  renderSpeakerRosterEditor();
});
function tryAddColorRuleFromForm() {
  const pattern = $("colorRulePattern")?.value;
  const color = $("colorRuleColor")?.value || DEFAULT_SCHEDULE_COLOR;
  if (!addColorRule(pattern, color)) return;
  if ($("colorRulePattern")) $("colorRulePattern").value = "";
  renderColorRulesEditor();
}
$("colorRuleAddBtn")?.addEventListener("click", () => tryAddColorRuleFromForm());
$("colorRulePattern")?.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  event.preventDefault();
  tryAddColorRuleFromForm();
});
$("colorRuleList")?.addEventListener("click", (event) => {
  const move = event.target.closest("[data-color-rule-move]");
  if (move) {
    moveColorRule(move.dataset.colorRuleMove, Number(move.dataset.colorRuleDelta) || 0);
    renderColorRulesEditor();
    return;
  }
  const btn = event.target.closest("[data-color-rule-remove]");
  if (!btn) return;
  removeColorRule(btn.dataset.colorRuleRemove);
  renderColorRulesEditor();
});
$("colorRuleList")?.addEventListener("change", (event) => {
  const input = event.target.closest("[data-color-rule-color]");
  if (!input || input.type !== "color") return;
  updateColorRuleColor(input.dataset.colorRuleColor, input.value);
  renderColorRulesEditor();
});

function tryAddFontRuleFromForm() {
  const pattern = $("fontRulePattern")?.value;
  const titleColor = $("fontRuleTitleColor")?.value || DEFAULT_TITLE_FONT_COLOR;
  const speakerColor =
    $("fontRuleSpeakerColor")?.value || DEFAULT_SPEAKER_FONT_COLOR;
  if (!addFontRule(pattern, titleColor, speakerColor)) return;
  if ($("fontRulePattern")) $("fontRulePattern").value = "";
  renderFontRulesEditor();
}
$("fontRuleAddBtn")?.addEventListener("click", () => tryAddFontRuleFromForm());
$("fontRulePattern")?.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  event.preventDefault();
  tryAddFontRuleFromForm();
});
$("fontRuleList")?.addEventListener("click", (event) => {
  const move = event.target.closest("[data-font-rule-move]");
  if (move) {
    moveFontRule(move.dataset.fontRuleMove, Number(move.dataset.fontRuleDelta) || 0);
    renderFontRulesEditor();
    return;
  }
  const btn = event.target.closest("[data-font-rule-remove]");
  if (!btn) return;
  removeFontRule(btn.dataset.fontRuleRemove);
  renderFontRulesEditor();
});
$("fontRuleList")?.addEventListener("change", (event) => {
  const titleInput = event.target.closest("[data-font-rule-title]");
  if (titleInput) {
    updateFontRuleColor(titleInput.dataset.fontRuleTitle, "titleColor", titleInput.value);
    renderFontRulesEditor();
    return;
  }
  const speakerInput = event.target.closest("[data-font-rule-speaker]");
  if (speakerInput) {
    updateFontRuleColor(
      speakerInput.dataset.fontRuleSpeaker,
      "speakerColor",
      speakerInput.value,
    );
    renderFontRulesEditor();
  }
});
$("fontDefaultTitleColor")?.addEventListener("change", () => {
  persistFontDefaults(
    {
      titleColor: $("fontDefaultTitleColor")?.value,
      speakerColor: $("fontDefaultSpeakerColor")?.value,
    },
    { toastMessage: "기본 글자색을 저장했습니다." },
  );
});
$("fontDefaultSpeakerColor")?.addEventListener("change", () => {
  persistFontDefaults(
    {
      titleColor: $("fontDefaultTitleColor")?.value,
      speakerColor: $("fontDefaultSpeakerColor")?.value,
    },
    { toastMessage: "기본 글자색을 저장했습니다." },
  );
});

function persistColorAutoDefaultFromControls() {
  const enabled = $("colorAutoDefaultEnabled")?.checked;
  const color = $("colorAutoDefaultColor")?.value || DEFAULT_SCHEDULE_COLOR;
  saveColorAutoDefault(enabled ? color : null);
  void persistColorRules(loadColorRules(), {
    toastMessage: "자동 기본 배경색을 저장했습니다.",
  });
  syncColorAutoDefaultControls();
}
$("colorAutoDefaultEnabled")?.addEventListener("change", () => {
  persistColorAutoDefaultFromControls();
});
$("colorAutoDefaultColor")?.addEventListener("change", () => {
  if (!$("colorAutoDefaultEnabled")?.checked) return;
  persistColorAutoDefaultFromControls();
});

function persistHolidayBgFromControls() {
  const enabled = Boolean($("holidayBgEnabled")?.checked);
  const color = $("holidayBgColor")?.value || DEFAULT_HOLIDAY_BG_COLOR;
  saveHolidayBgSettings({ enabled, color });
  syncHolidayBgControls();
  if (viewMode === "calendar") renderCalendar();
}
$("holidayBgEnabled")?.addEventListener("change", () => {
  persistHolidayBgFromControls();
});
$("holidayBgColor")?.addEventListener("change", () => {
  if (!$("holidayBgEnabled")?.checked) return;
  persistHolidayBgFromControls();
});

function persistMiscSettingsFromControls() {
  const mode = $("miscPeriodColorMode")?.value || PERIOD_COLOR_MODE_SPEAKER;
  saveMiscSettings({
    showPeriodDates: Boolean($("miscShowPeriodDates")?.checked),
    periodColorMode: mode,
    periodColor: $("miscPeriodColor")?.value || DEFAULT_PERIOD_COLOR,
  });
  syncMiscSettingsControls();
  if (viewMode === "calendar") renderCalendar();
}
$("miscShowPeriodDates")?.addEventListener("change", () => {
  persistMiscSettingsFromControls();
});
$("miscPeriodColorMode")?.addEventListener("change", () => {
  persistMiscSettingsFromControls();
});
$("miscPeriodColor")?.addEventListener("change", () => {
  const mode = $("miscPeriodColorMode")?.value;
  if (mode !== PERIOD_COLOR_MODE_CUSTOM) return;
  persistMiscSettingsFromControls();
});

$("calendarFilterGear")?.addEventListener("click", () => {
  renderCalendarFilterEditor();
  $("calendarFilterDialog")?.showModal();
  $("calendarFilterPattern")?.focus();
});
$("calendarFilterClose")?.addEventListener("click", () =>
  $("calendarFilterDialog")?.close(),
);
$("calendarFilterDone")?.addEventListener("click", () =>
  $("calendarFilterDialog")?.close(),
);
function tryAddCalendarFilterFromForm() {
  const pattern = $("calendarFilterPattern")?.value;
  if (!addCalendarViewFilter(pattern)) return;
  if ($("calendarFilterPattern")) $("calendarFilterPattern").value = "";
}
$("calendarFilterAddBtn")?.addEventListener("click", () =>
  tryAddCalendarFilterFromForm(),
);
$("calendarFilterPattern")?.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  event.preventDefault();
  tryAddCalendarFilterFromForm();
});
$("calendarFilterRuleList")?.addEventListener("click", (event) => {
  const btn = event.target.closest("[data-calendar-filter-remove]");
  if (!btn) return;
  removeCalendarViewFilter(btn.dataset.calendarFilterRemove);
});
$("calendarFilterChips")?.addEventListener("change", (event) => {
  const other = event.target.closest("[data-calendar-filter-other]");
  const state = loadCalendarFiltersState();
  if (other) {
    state.otherChecked = Boolean(other.checked);
    saveCalendarFiltersState(state);
    renderCalendarFilterChips();
    render();
    return;
  }
  const box = event.target.closest("[data-calendar-filter-id]");
  if (!box) return;
  state.checked[box.dataset.calendarFilterId] = Boolean(box.checked);
  saveCalendarFiltersState(state);
  renderCalendarFilterChips();
  render();
});

$("speakerPickList")?.addEventListener("click", (event) => {
  const btn = event.target.closest("[data-pick-speaker]");
  if (!btn) return;
  applyPickedSpeaker(btn.dataset.pickSpeaker);
});
document.addEventListener("click", (event) => {
  const pop = $("speakerPickPopover");
  if (!pop || pop.hidden || pop.classList.contains("hidden")) return;
  if (event.target.closest("#speakerPickPopover")) return;
  if (event.target.closest("#speakerGwangjuButton")) return;
  closeSpeakerPickPopover();
});
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") closeSpeakerPickPopover();
});
window.addEventListener("resize", () => closeSpeakerPickPopover());
$("scheduleDialog")?.addEventListener("close", () => closeSpeakerPickPopover());
$("officialDocToggle").onclick = () => {
  const block = $("officialDocBlock");
  const collapsed = block?.classList.contains("is-collapsed");
  setOfficialDocExpanded(Boolean(collapsed));
};
$("sharedNoticeEditButton").onclick = () => {
  void toggleSharedNoticeEdit();
};
$("sharedNoticeOlderButton").onclick = () => showOlderSharedNotice();
$("sharedNoticeNewerButton").onclick = () => showNewerSharedNotice();
$("fileInput").onchange = async (event) => {
  const file = event.target.files[0];
  const mode = pendingImportMode || "replace";
  pendingImportMode = null;
  if (!file) return;
  try {
    const imported = JSON.parse(await file.text());
    if (!Array.isArray(imported)) throw new Error("배열 형식이 아닙니다.");
    applyImportedSchedules(imported, mode);
  } catch (error) {
    showToast(`가져오기 실패: ${error.message}`);
  }
  event.target.value = "";
};
$("bridgeRetryButton").onclick = () => checkBridgeHealth({ quiet: false });

initThemeControls();

const SESSION_PING_MS = 4000;

function pingBridgeSession() {
  fetch(`${API_BASE}/api/session/ping`, {
    method: "POST",
    cache: "no-store",
    keepalive: true,
  }).catch(() => {});
}

function byeBridgeSession() {
  fetch(`${API_BASE}/api/session/bye`, {
    method: "POST",
    cache: "no-store",
    keepalive: true,
  }).catch(() => {});
}

function startBridgeSessionWatch() {
  pingBridgeSession();
  setInterval(() => {
    if (document.visibilityState === "hidden") return;
    pingBridgeSession();
  }, SESSION_PING_MS);
  window.addEventListener("pageshow", pingBridgeSession);
}

function releaseNoticeLockKeepalive() {
  if (!sharedNoticeEditing) return;
  if (!(bridgeAlive && bridgeConnected)) return;
  const payload = JSON.stringify(
    buildNoticePayload({
      text: sharedNoticeText,
      updatedAt: sharedNoticeUpdatedAt,
      history: sharedNoticeHistory,
      lock: null,
    }),
  );
  const headers = {
    "Content-Type": "application/json",
    ...(sharedNoticeRevision
      ? { "X-Expected-Revision": sharedNoticeRevision }
      : {}),
  };
  fetch(`${API_BASE}/api/file?name=` + encodeURIComponent(NOTICE_FILE), {
    method: "PUT",
    headers,
    body: payload,
    keepalive: true,
  }).catch(() => {});
}

document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "hidden") {
    flushPendingSaveOnExit();
    return;
  }
  pingBridgeSession();
  checkBridgeHealth({ quiet: true });
  void refreshSharedNotice({ quiet: true });
  void refreshPresetsFromNas({ quiet: true });
  void refreshSpeakerRosterFromNas({ quiet: true });
  void refreshColorRulesFromNas({ quiet: true });
  void refreshFontRulesFromNas({ quiet: true });
});
window.addEventListener("pagehide", () => {
  flushPendingSaveOnExit();
  releaseNoticeLockKeepalive();
  byeBridgeSession();
});
window.addEventListener("beforeunload", () => {
  flushPendingSaveOnExit();
  releaseNoticeLockKeepalive();
  byeBridgeSession();
});
window.addEventListener("online", () => checkBridgeHealth({ quiet: true }));
if ("serviceWorker" in navigator && !isBridgeHosted()) {
  navigator.serviceWorker.register("/sw.js").catch(() => {});
}
startBridgeSessionWatch();
startBridgeWatch();

$("connectFailedRetry")?.addEventListener("click", () => {
  void tryEnterFromBridgeOrLogin();
});
$("connectFailedLocal")?.addEventListener("click", () => {
  enterAppShell();
  showToast("로컬 저장소로 시작합니다. NAS는 나중에 연결할 수 있습니다.");
});
$("connectFailedLogin")?.addEventListener("click", () => {
  showLoginScreen();
});

async function tryEnterFromBridgeOrLogin() {
  showConnectChecking("브리지·NAS 상태를 확인하는 중입니다…");
  resetConnectSteps();
  setConnectStep("bridge", "active");
  try {
    const r = await fetch(`${API_BASE}/api/status`, { cache: "no-store" });
    if (!r.ok) throw new Error(`브리지 응답 오류 (HTTP ${r.status})`);
    const s = await r.json();
    setConnectStep("bridge", "done");
    setConnectStep("nas", "active");
    if (!s.connected) {
      setConnectStep("nas", "fail");
      if (isBridgeHosted()) {
        showConnectFailed({
          message: "브리지는 켜져 있지만 NAS가 연결되어 있지 않습니다.",
          detail: "허브의 NAS 설정에서 연결한 뒤 다시 시도하세요.",
        });
        return;
      }
      showLoginScreen();
      return;
    }
    bridgeAlive = true;
    bridgeConnected = true;
    connectionRoot = s.root || "";
    setConnectStep("nas", "done");
    setConnectStep("file", "active");
    showConnectChecking("일정 파일을 불러오는 중입니다…", connectionRoot);
    const filesRes = await fetch(`${API_BASE}/api/files`, { cache: "no-store" });
    const files = filesRes.ok ? await filesRes.json() : [];
    const list = Array.isArray(files) ? files : [];
    const preferred =
      typeof applyJsonFileOptions === "function" ? applyJsonFileOptions(list) : "";
    if (preferred) {
      try {
        await loadSelectedBridgeFile(preferred);
      } catch (fileErr) {
        setConnectStep("file", "fail");
        showConnectFailed({
          message: "일정 파일을 불러오지 못했습니다.",
          detail: fileErr?.message || String(fileErr),
        });
        return;
      }
    }
    setConnectStep("file", "done");
    setConnectStep("ready", "active");
    if (typeof updateConnectionUi === "function") {
      updateConnectionUi({
        state: "linked",
        label: "공유 연결됨",
        detail: connectionRoot || "브리지 NAS 연결됨",
        pathText: preferred || "",
        syncText: preferred ? `NAS 연결됨 · ${preferred}` : "NAS 연결됨",
        syncState: "linked",
      });
    }
    try {
      enterAppShell();
    } catch (bootErr) {
      setConnectStep("ready", "fail");
      showConnectFailed({
        message: "일정 보드를 열지 못했습니다.",
        detail: bootErr?.message || String(bootErr),
      });
      return;
    }
    setConnectStep("ready", "done");
  } catch (err) {
    setConnectStep("bridge", "fail");
    showConnectFailed({
      message: isBridgeHosted()
        ? "브리지에 연결하지 못했습니다."
        : "일정 서버에 연결하지 못했습니다.",
      detail: err?.message || String(err),
    });
  }
}

tryEnterFromBridgeOrLogin();