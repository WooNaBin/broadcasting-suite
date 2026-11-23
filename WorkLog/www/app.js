import {
  DEFAULT_PROFILES,
  DEFAULT_DEFAULTS,
  PREPARATION_LABELS,
  templateLabel as templateLabelBase,
  placeLabel,
  normalizeDefaults,
  normalizeAppendTarget,
  allTemplates,
} from "./templates.js";

const PREFS_KEY = "worklog.prefs.v1";
const WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const WEEKDAYS_KO = ["일", "월", "화", "수", "목", "금", "토"];

const $ = (id) => document.getElementById(id);

/** BroadcastNasBridge `/worklog` 로 서빙될 때 상대 API 경로 사용 */
function isBridgeHosted() {
  try {
    return typeof location !== "undefined" && location.pathname.startsWith("/worklog");
  } catch {
    return false;
  }
}

function defaultApiBase() {
  if (typeof window !== "undefined" && window.__UI_DEMO__) return "";
  return isBridgeHosted() ? "/worklog" : "http://127.0.0.1:17822";
}

let apiBase = defaultApiBase();
let connected = false;
let profile = null;
let profiles = DEFAULT_PROFILES;
let profilesRevision = "0";
let defaultsDoc = structuredClone(DEFAULT_DEFAULTS);
let defaultsRevision = "0";
const PIN_ITERATIONS = 100000;
const PIN_MAX_LEN = 8;
/** @type {{ resolve: (ok: boolean) => void, mode: "setup"|"verify", profile: object } | null} */
let pinGatePending = null;
let monthCursor = startOfMonth(new Date());
let monthDoc = { version: 1, entries: [] };
let monthRevision = "0";
let monthDirty = false;
let selectedDate = null;
/** 에디터에 열린 항목 (아직 monthDoc에 없을 수 있음) */
let editorEntry = null;
let schedules = [];
let scheduleFileName = "";
/** @type {Array<{ type: "date"|"notes", date?: string }>} 인쇄 칸 (1~7, 마지막은 날짜/특이사항) */
let printSlots = [];
/** 날짜선택 모드 칸 수 (1~7) */
let printSlotCount = 4;
/** @type {"slots"|"week"} */
let printMode = "slots";
/** 일주일보기 기준일 (선택일) */
let printWeekAnchor = null;
/** 1페이지에 포함할 마지막 요일 (0=일 … 6=토). 기본 수요일 = 4칸+4칸 */
let printWeekSplitAfter = 3;
/** 일주일보기에서 기타 특이사항(+α) 인쇄 포함 */
let printWeekIncludeNotes = true;
const PRINT_EMPTY_LINES_MIN = 0;
const PRINT_EMPTY_LINES_MAX = 20;
const PRINT_EMPTY_LINES_DEFAULT = 4;
let printEmptyJournalLines = PRINT_EMPTY_LINES_DEFAULT;
let printEmptyNotesLines = PRINT_EMPTY_LINES_DEFAULT;
const SPECIAL_NOTES_PATH = "special-notes.json";
/** @type {{ id: string, text: string, updatedAt: string, updatedBy: string }[]} */
let specialNotes = [];
let specialNotesRevision = "0";
let specialNoteIndex = 0;
let specialNotesDirty = false;
/** 브라우저 탭 고유 세션 — NAS 작성 잠금 소유권 */
const editSessionId = crypto.randomUUID();
let editMode = false;
let editLockHolder = null; // { actorId, actorName, sessionId, ... } | null
let editHeartbeatTimer = null;
let editLockPollTimer = null;
const EDIT_LOCK_HEARTBEAT_MS = 15000;
const EDIT_LOCK_POLL_MS = 4000;

function templateLabel(id) {
  return templateLabelBase(id, defaultsDoc);
}

function catalogTemplates() {
  return allTemplates(defaultsDoc);
}

function entryHasContent(entry) {
  if (!entry) return false;
  normalizeEntryLogs(entry);
  if ((entry.logA || "").trim()) return true;
  if ((entry.logB || "").trim()) return true;
  if ((entry.log || "").trim()) return true;
  if ((entry.templates || []).length) return true;
  if (entry.isPrinted) return true;
  return false;
}

/** 구버전 `log` → logA/logB 마이그레이션 + 동기화 */
function normalizeEntryLogs(entry) {
  if (!entry || typeof entry !== "object") return entry;
  const hasA = Object.prototype.hasOwnProperty.call(entry, "logA");
  const hasB = Object.prototype.hasOwnProperty.call(entry, "logB");
  if (!hasA && !hasB) {
    entry.logA = entry.log || "";
    entry.logB = "";
  } else {
    if (!hasA) entry.logA = entry.log || "";
    if (!hasB) entry.logB = "";
    entry.logA = entry.logA ?? "";
    entry.logB = entry.logB ?? "";
  }
  // 하위 호환: log 는 A와 동일하게 유지
  entry.log = entry.logA || "";
  return entry;
}

function specialNotesMax() {
  const n = Number(defaultsDoc?.specialNotesMax);
  if (!Number.isFinite(n)) return 10;
  return Math.min(50, Math.max(1, Math.round(n)));
}

/** 스케줄 날짜 문자열 정규화 */
function scheduleDateKey(s) {
  const d = s?.scheduleDate ?? s?.date;
  if (!d) return "";
  if (typeof d === "string") return d.slice(0, 10);
  if (typeof d === "object" && d.yearData != null) {
    const y = 2000 + Number(d.yearData);
    const m = String(d.monthData).padStart(2, "0");
    const day = String(d.dayData).padStart(2, "0");
    return `${y}-${m}-${day}`;
  }
  return String(d).slice(0, 10);
}

function startOfMonth(d) {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

function ymd(d) {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

function monthKey(d = monthCursor) {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  return `${y}-${m}`;
}

function todayYmd() {
  return ymd(new Date());
}

function profileName(idOrProfile) {
  if (!idOrProfile) return "";
  if (typeof idOrProfile === "object") return idOrProfile.displayName || "";
  return profiles.find((p) => p.id === idOrProfile)?.displayName || idOrProfile;
}

function showToast(message) {
  const el = $("toast");
  el.textContent = message;
  el.classList.remove("hidden");
  clearTimeout(showToast._t);
  showToast._t = setTimeout(() => el.classList.add("hidden"), 2800);
}

function showScreen(name) {
  $("screen-status").classList.toggle("hidden", name !== "status");
  $("screen-profile").classList.toggle("hidden", name !== "profile");
  $("screen-main").classList.toggle("hidden", name !== "main");
  $("screen-print").classList.toggle("hidden", name !== "print");
}

function loadPrefs() {
  try {
    return JSON.parse(localStorage.getItem(PREFS_KEY) || "{}");
  } catch {
    return {};
  }
}

function savePrefs(patch) {
  const next = { ...loadPrefs(), ...patch };
  localStorage.setItem(PREFS_KEY, JSON.stringify(next));
  return next;
}

function xorEncode(text, key = "worklog-local") {
  const bytes = new TextEncoder().encode(text);
  const out = new Uint8Array(bytes.length);
  for (let i = 0; i < bytes.length; i++) out[i] = bytes[i] ^ key.charCodeAt(i % key.length);
  return btoa(String.fromCharCode(...out));
}

function xorDecode(encoded, key = "worklog-local") {
  try {
    const raw = atob(encoded);
    const bytes = Uint8Array.from(raw, (c) => c.charCodeAt(0));
    const out = new Uint8Array(bytes.length);
    for (let i = 0; i < bytes.length; i++) out[i] = bytes[i] ^ key.charCodeAt(i % key.length);
    return new TextDecoder().decode(out);
  } catch {
    return "";
  }
}

function resolveApiBaseFromUi() {
  // 브리지 UI에서는 절대 단독 17822로 되돌리지 않음
  if (isBridgeHosted()) return "/worklog";
  const prefs = loadPrefs();
  if ((prefs.apiMode || "bridge") === "remote") {
    const url = String(prefs.remoteApiBase || "").trim().replace(/\/$/, "");
    if (!url) throw new Error("원격 API URL이 없습니다. 브리지를 사용하세요.");
    return url;
  }
  return "http://127.0.0.1:17822";
}

function setConnBadge(state, label) {
  const badge = $("connBadge");
  if (!badge) return;
  badge.dataset.state = state;
  badge.textContent = label;
}

function setConnStep(step, state) {
  const li = document.querySelector(`#connSteps [data-step="${step}"]`);
  if (!li) return;
  li.classList.remove("is-pending", "is-active", "is-done", "is-fail");
  li.classList.add(`is-${state}`);
}

function resetConnSteps() {
  document.querySelectorAll("#connSteps [data-step]").forEach((li) => {
    li.classList.remove("is-active", "is-done", "is-fail");
    li.classList.add("is-pending");
  });
}

function setConnUi({ state, badge, message, detail, error }) {
  if (badge) setConnBadge(state || "checking", badge);
  if (message != null && $("connMessage")) $("connMessage").textContent = message;
  if (detail != null && $("connDetail")) $("connDetail").textContent = detail;
  if ($("loginError")) $("loginError").textContent = error || "";
}

/** NAS 연결 후 작성 프로필 선택(PIN) — NAS 로그인 폼과는 별개 */
async function enterAfterNasConnected() {
  connected = true;
  setConnStep("store", "done");
  setConnStep("ready", "active");
  setConnUi({
    state: "checking",
    badge: "준비 중",
    message: "작성 프로필 목록을 불러오는 중입니다…",
  });
  await loadProfilesAndDefaults();
  renderProfiles();
  setConnStep("ready", "done");

  const prefs = loadPrefs();
  const remembered = profiles.find((p) => p.id === prefs.profileId);
  if (remembered) {
    setConnUi({
      state: "ok",
      badge: "연결됨",
      message: `저장된 프로필(${remembered.displayName})로 들어갑니다. PIN을 확인하세요.`,
    });
    const ok = await selectProfile(remembered);
    if (!ok) {
      setConnUi({
        state: "ok",
        badge: "연결됨",
        message: "프로필을 선택하고 PIN을 설정·확인하세요.",
      });
      showScreen("profile");
    }
    return;
  }

  setConnUi({
    state: "ok",
    badge: "연결됨",
    message: "NAS 연결 확인. 작성 프로필을 선택하고 PIN을 설정·확인하세요.",
  });
  showScreen("profile");
}

async function api(path, options = {}) {
  const opts = { ...options, headers: { ...(options.headers || {}) } };
  if (!opts.signal && typeof AbortSignal !== "undefined" && typeof AbortSignal.timeout === "function") {
    opts.signal = AbortSignal.timeout(15000);
  }
  return fetch(`${apiBase}${path}`, opts);
}

async function readJsonFile(relativePath) {
  const res = await api(`/api/file?path=${encodeURIComponent(relativePath)}`);
  const revision = res.headers.get("X-File-Revision") || "0";
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || `파일 읽기 실패: ${relativePath}`);
  }
  const text = await res.text();
  if (text === "null" || text.trim() === "") return { data: null, revision };
  return { data: JSON.parse(text), revision };
}

async function writeJsonFile(relativePath, data, expectedRevision) {
  const headers = { "Content-Type": "application/json" };
  if (expectedRevision != null) headers["X-Expected-Revision"] = expectedRevision;
  const res = await api(`/api/file?path=${encodeURIComponent(relativePath)}`, {
    method: "PUT",
    headers,
    body: JSON.stringify(data),
  });
  if (res.status === 409) {
    const body = await res.json();
    const err = new Error(body.message || "충돌");
    err.conflict = true;
    err.revision = body.revision;
    err.content = body.content;
    throw err;
  }
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || "저장 실패");
  }
  return res.json();
}

async function appendAudit(action, entry, summary) {
  if (!profile) return;
  const month = (entry?.date || todayYmd()).slice(0, 7);
  const payload = {
    ts: new Date().toISOString(),
    actor: profile.id,
    action,
    entryId: entry?.id || "",
    date: entry?.date || "",
    summary: summary || action,
  };
  const res = await api(`/api/audit/append?month=${encodeURIComponent(month)}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    console.warn("audit failed", err.message);
  }
}

function prefsCredentials() {
  const prefs = loadPrefs();
  return {
    host: String(prefs.host || "").trim(),
    share: String(prefs.share || "Temp DATA\\_data\\_work_log").trim(),
    username: String(prefs.username || "").trim(),
    password: prefs.remember && prefs.passwordEnc ? xorDecode(prefs.passwordEnc) : "",
    remember: Boolean(prefs.remember),
    apiMode: prefs.apiMode || "bridge",
    remoteApiBase: String(prefs.remoteApiBase || "").trim(),
  };
}

async function bindWorkLogStore() {
  const res = await api("/api/connect", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: "{}",
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || "작업일지 폴더를 열 수 없습니다.");
  }
  await api("/api/ensure-layout", { method: "POST" }).catch(() => {});
}

/**
 * 로그인 폼 없이 상태창만 갱신하며 접속.
 * 브리지 호스팅이면 NAS 설정은 허브(/setup)에서만 한다.
 */
async function runConnectionFlow() {
  showScreen("status");
  resetConnSteps();
  setConnUi({
    state: "checking",
    badge: "확인 중",
    message: "접속 상태를 확인하는 중입니다…",
    detail: "",
    error: "",
  });

  const setupLink = $("btnOpenBridgeSetup");
  if (setupLink) setupLink.classList.add("hidden");

  const showNasSetupHint = () => {
    if (setupLink && isBridgeHosted()) setupLink.classList.remove("hidden");
  };

  try {
    apiBase = defaultApiBase();
    if (!isBridgeHosted() && !(typeof window !== "undefined" && window.__UI_DEMO__)) {
      try {
        apiBase = resolveApiBaseFromUi();
      } catch (err) {
        apiBase = "http://127.0.0.1:17822";
      }
    }

    setConnStep("bridge", "active");
    setConnUi({ message: "브리지·로컬 API에 연결하는 중…" });

    let statusRes;
    try {
      statusRes = await api("/api/status");
    } catch (err) {
      setConnStep("bridge", "fail");
      throw new Error(
        isBridgeHosted()
          ? "브리지 API에 연결할 수 없습니다. BroadcastNasBridge가 실행 중인지 확인하세요."
          : "WorkLog API(17822)에 연결할 수 없습니다. 브리지(/worklog) 사용을 권장합니다.",
      );
    }
    if (!statusRes.ok) {
      setConnStep("bridge", "fail");
      throw new Error(`상태 확인 실패 (HTTP ${statusRes.status})`);
    }
    setConnStep("bridge", "done");

    const status = await statusRes.json();
    setConnStep("nas", "active");
    setConnUi({
      message: "NAS 세션을 확인하는 중…",
      detail: status.root || status.scheduleRoot || "",
    });

    if (status.connected) {
      setConnStep("nas", "done");
      setConnStep("store", "done");
      setConnUi({
        state: "ok",
        badge: "연결됨",
        message: "이미 연결되어 있습니다. 프로필 단계로 이동합니다…",
        detail: status.root || "",
      });
      await enterAfterNasConnected();
      return true;
    }

    if (isBridgeHosted()) {
      if (!(status.via === "BroadcastNasBridge" || status.nasConnected)) {
        setConnStep("nas", "fail");
        showNasSetupHint();
        throw new Error(
          "브리지에 NAS가 연결되어 있지 않습니다. 허브에서 NAS를 연결한 뒤 다시 시도하세요. (WorkLog에서는 주소·계정을 입력하지 않습니다.)",
        );
      }
      setConnStep("nas", "done");
      setConnStep("store", "active");
      setConnUi({ message: "작업일지 폴더를 여는 중…", detail: "" });
      await bindWorkLogStore();
      setConnStep("store", "done");
      await enterAfterNasConnected();
      return true;
    }

    // 단독 실행: 기억된 NAS 계정으로만 조용히 시도 (주소·계정 폼 없음)
    const cred = prefsCredentials();
    if (!cred.host || !cred.username || !cred.password) {
      setConnStep("nas", "fail");
      throw new Error(
        "단독 실행용 저장 NAS 계정이 없습니다. BroadcastNasBridge(17820) 허브에서 NAS 연결 후 /worklog 로 열어주세요.",
      );
    }
    setConnUi({ message: "저장된 NAS 계정으로 연결하는 중…", detail: cred.host });
    const res = await api("/api/connect", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        host: cred.host,
        share: cred.share,
        username: cred.username,
        password: cred.password,
      }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      setConnStep("nas", "fail");
      throw new Error(err.message || "NAS 연결 실패");
    }
    setConnStep("nas", "done");
    setConnStep("store", "active");
    await api("/api/ensure-layout", { method: "POST" }).catch(() => {});
    setConnStep("store", "done");
    await enterAfterNasConnected();
    return true;
  } catch (err) {
    const msg = err?.message || String(err);
    setConnUi({
      state: "error",
      badge: "연결 실패",
      message: msg,
      error: msg,
    });
    showScreen("status");
    return false;
  }
}

async function connectNas() {
  return runConnectionFlow();
}

/** @deprecated 로그인 폼 제거 — 상태창 재시도만 */
async function tryAutoConnectFromPrefs() {
  return runConnectionFlow();
}

async function loadProfilesAndDefaults() {
  try {
    const { data, revision } = await readJsonFile("profiles.json");
    if (data?.profiles?.length) profiles = data.profiles;
    profilesRevision = revision;
  } catch {
    profiles = DEFAULT_PROFILES;
    profilesRevision = "0";
  }
  try {
    const { data, revision } = await readJsonFile("defaults.json");
    defaultsDoc = normalizeDefaults(data);
    defaultsRevision = revision;
  } catch {
    defaultsDoc = structuredClone(DEFAULT_DEFAULTS);
    defaultsRevision = "0";
  }
}

function renderProfiles() {
  const list = $("profileList");
  list.innerHTML = "";
  for (const p of profiles) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "profile-card";
    btn.innerHTML = `<strong>${escapeHtml(p.displayName)}</strong>`;
    btn.addEventListener("click", () => {
      selectProfile(p).catch((e) => showToast(e.message || String(e)));
    });
    list.appendChild(btn);
  }
}

function bufferToBase64(buf) {
  const bytes = new Uint8Array(buf);
  let s = "";
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  return btoa(s);
}

function base64ToBytes(b64) {
  const raw = atob(b64);
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}

function isDigitsPin(value) {
  return /^\d{1,8}$/.test(value);
}

function profileHasPin(p) {
  return Boolean(p?.pin?.salt && p?.pin?.hash);
}

async function derivePinHash(pin, saltBytes, iterations = PIN_ITERATIONS) {
  const keyMaterial = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(pin),
    "PBKDF2",
    false,
    ["deriveBits"]
  );
  return crypto.subtle.deriveBits(
    {
      name: "PBKDF2",
      hash: "SHA-256",
      salt: saltBytes,
      iterations,
    },
    keyMaterial,
    256
  );
}

async function createPinRecord(pin) {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const hash = await derivePinHash(pin, salt, PIN_ITERATIONS);
  return {
    algo: "pbkdf2-sha256",
    iterations: PIN_ITERATIONS,
    salt: bufferToBase64(salt.buffer),
    hash: bufferToBase64(hash),
  };
}

async function verifyPin(pin, pinRecord) {
  if (!pinRecord?.salt || !pinRecord?.hash) return false;
  const salt = base64ToBytes(pinRecord.salt);
  const iterations = Number(pinRecord.iterations) || PIN_ITERATIONS;
  const derived = new Uint8Array(await derivePinHash(pin, salt, iterations));
  const expected = base64ToBytes(pinRecord.hash);
  if (derived.length !== expected.length) return false;
  let diff = 0;
  for (let i = 0; i < derived.length; i++) diff |= derived[i] ^ expected[i];
  return diff === 0;
}

function closePinModal(ok) {
  $("modalPin").classList.add("hidden");
  $("pinInput").value = "";
  $("pinInputConfirm").value = "";
  $("pinModalError").textContent = "";
  const pending = pinGatePending;
  pinGatePending = null;
  if (pending) pending.resolve(Boolean(ok));
}

function openPinModal(mode, p) {
  return new Promise((resolve) => {
    pinGatePending = { resolve, mode, profile: p };
    const isSetup = mode === "setup";
    $("pinModalTitle").textContent = isSetup ? "PIN 설정" : "PIN 확인";
    $("pinModalLead").textContent = isSetup
      ? `${p.displayName} — 숫자 PIN(1–8자리)을 설정하세요.`
      : `${p.displayName} — PIN을 입력하세요.`;
    $("pinFieldConfirm").classList.toggle("hidden", !isSetup);
    $("pinModalError").textContent = "";
    $("pinInput").value = "";
    $("pinInputConfirm").value = "";
    $("modalPin").classList.remove("hidden");
    $("pinInput").focus();
  });
}

async function saveProfilePin(profileId, pinRecord) {
  const idx = profiles.findIndex((x) => x.id === profileId);
  if (idx < 0) throw new Error("프로필을 찾을 수 없습니다.");
  const nextProfiles = profiles.map((x, i) =>
    i === idx ? { ...x, pin: pinRecord } : { ...x }
  );
  const payload = { version: 1, profiles: nextProfiles };
  const result = await writeJsonFile("profiles.json", payload, profilesRevision);
  profiles = nextProfiles;
  profilesRevision = result.revision;
  return profiles[idx];
}

function enterMainWithProfile(p) {
  profile = p;
  savePrefs({ profileId: p.id });
  $("profileBadge").textContent = p.displayName;
  showScreen("main");
  loadMonth({ selectToday: true }).catch((e) => showToast(e.message));
}

async function selectProfile(p) {
  if (typeof window !== "undefined" && window.__UI_DEMO__) {
    enterMainWithProfile(p);
    return true;
  }
  showScreen("profile");
  const mode = profileHasPin(p) ? "verify" : "setup";
  const ok = await openPinModal(mode, p);
  if (!ok) return false;
  const current = profiles.find((x) => x.id === p.id) || p;
  enterMainWithProfile(current);
  return true;
}

async function submitPinModal() {
  if (!pinGatePending) return;
  const { mode, profile: target } = pinGatePending;
  const pin = $("pinInput").value.trim();
  $("pinModalError").textContent = "";

  if (!isDigitsPin(pin) || pin.length > PIN_MAX_LEN) {
    $("pinModalError").textContent = "PIN은 숫자 1–8자리여야 합니다.";
    return;
  }

  if (mode === "setup") {
    const confirm = $("pinInputConfirm").value.trim();
    if (pin !== confirm) {
      $("pinModalError").textContent = "PIN이 일치하지 않습니다.";
      return;
    }
    try {
      const pinRecord = await createPinRecord(pin);
      await saveProfilePin(target.id, pinRecord);
      closePinModal(true);
    } catch (err) {
      if (err.conflict) {
        showToast("충돌 — 프로필을 다시 불러옵니다.");
        try {
          const { data, revision } = await readJsonFile("profiles.json");
          if (data?.profiles?.length) profiles = data.profiles;
          profilesRevision = revision;
          renderProfiles();
        } catch {
          // ignore
        }
        closePinModal(false);
        return;
      }
      $("pinModalError").textContent = err.message || "PIN 저장 실패";
    }
    return;
  }

  const current = profiles.find((x) => x.id === target.id) || target;
  const matched = await verifyPin(pin, current.pin);
  if (!matched) {
    showToast("PIN이 올바르지 않습니다.");
    closePinModal(false);
    return;
  }
  closePinModal(true);
}

async function disconnectAll() {
  releaseEditLockBeacon();
  editMode = false;
  stopEditHeartbeat();
  stopEditLockPoll();
  try {
    await api("/api/disconnect", { method: "POST" });
  } catch {
    // ignore
  }
  connected = false;
  profile = null;
  resetConnSteps();
  setConnUi({
    state: "checking",
    badge: "해제됨",
    message: "연결이 해제되었습니다. 다시 시도하면 상태를 다시 확인합니다.",
    detail: "",
    error: "",
  });
  showScreen("status");
}

function escapeHtml(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function daysInMonth(d) {
  return new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
}

function entryForDate(dateStr) {
  return monthDoc.entries.find((e) => e.date === dateStr) || null;
}

function formatStartTime(startTime) {
  if (startTime == null || startTime === "") return "-";
  const n = Number(startTime);
  if (Number.isNaN(n)) return String(startTime);
  const s = String(Math.trunc(n)).padStart(4, "0");
  return `${s.slice(0, 2)}:${s.slice(2)}`;
}

function preparationList(prep) {
  if (!prep) return [];
  if (Array.isArray(prep)) return prep;
  if (typeof prep === "string") return prep.split(",").map((x) => x.trim()).filter(Boolean);
  if (typeof prep === "number") {
    const flags = [];
    if (prep & 1) flags.push("Recording");
    if (prep & 2) flags.push("Praise");
    if (prep & 4) flags.push("Streaming");
    if (prep & 8) flags.push("OtherDisplay");
    return flags;
  }
  return [];
}

function schedulesForDate(dateStr) {
  return schedules.filter((s) => scheduleDateKey(s) === dateStr);
}

function scheduleVars(schedule, dateStr) {
  return {
    schedule_title: schedule.scheduleTitle || "",
    place: schedule.place || "",
    place_label: placeLabel(schedule.place),
    start_time: formatStartTime(schedule.startTime),
    sermon_speaker: schedule.sermonSpeaker || "",
    accessory_info: schedule.accessoryInfo || "",
    preparation_note: schedule.preaparationNote || schedule.preparationNote || "",
    date: dateStr || "",
  };
}

function applyPlaceholders(template, vars) {
  return String(template || "").replace(/\{([a-z_]+)\}/gi, (_, key) => {
    const k = key.toLowerCase();
    return vars[k] != null ? String(vars[k]) : "";
  });
}

function appendUniqueLines(base, lines) {
  const existing = new Set(
    String(base || "")
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter(Boolean)
  );
  const out = String(base || "");
  const add = [];
  for (const line of lines) {
    const t = String(line || "").trim();
    if (!t || existing.has(t)) continue;
    existing.add(t);
    add.push(t);
  }
  if (!add.length) return out;
  return out ? `${out.replace(/\s+$/, "")}\n${add.join("\n")}` : add.join("\n");
}

/** 기록 규칙 조건 필드 */
const RULE_CONDITION_FIELDS = [
  { id: "schedule_title", label: "일정 제목 (schedule_title)" },
  { id: "accessory_info", label: "부가정보 (accessory_info)" },
  { id: "preparation_note", label: "설명/준비메모 (preparation_note)" },
];

function normalizeConditionType(type) {
  if (type === "title_contains" || !type) return "schedule_title";
  if (RULE_CONDITION_FIELDS.some((f) => f.id === type)) return type;
  return "schedule_title";
}

function scheduleFieldText(schedule, field) {
  const key = normalizeConditionType(field);
  if (key === "accessory_info") return schedule.accessoryInfo || "";
  if (key === "preparation_note") {
    return schedule.preaparationNote || schedule.preparationNote || "";
  }
  return schedule.scheduleTitle || "";
}

/**
 * 값 표현식 매칭.
 * - "집회or지원" → 둘 중 하나라도 포함
 * - "집회and지원" → 둘 다 포함
 * - or가 and보다 먼저 분리됨 (AandBorC → (A and B) or C)
 */
function matchKeywordExpression(text, expression) {
  const hay = String(text || "");
  const expr = String(expression || "").trim();
  if (!expr) return false;

  const orParts = expr.split(/\s*or\s*/i).map((s) => s.trim()).filter(Boolean);
  if (!orParts.length) return false;

  return orParts.some((part) => {
    const andParts = part.split(/\s*and\s*/i).map((s) => s.trim()).filter(Boolean);
    if (!andParts.length) return false;
    return andParts.every((keyword) => hay.includes(keyword));
  });
}

function ruleMatchesSchedule(rule, schedule) {
  const field = normalizeConditionType(rule.conditionType);
  const text = scheduleFieldText(schedule, field);
  return matchKeywordExpression(text, rule.value);
}

function renderDayList() {
  $("monthLabel").textContent = `${monthCursor.getFullYear()}년 ${monthCursor.getMonth() + 1}월`;
  const ul = $("dayList");
  ul.innerHTML = "";
  const total = daysInMonth(monthCursor);
  const today = todayYmd();
  for (let day = 1; day <= total; day++) {
    const date = new Date(monthCursor.getFullYear(), monthCursor.getMonth(), day);
    const key = ymd(date);
    if (key > today) continue; // 오늘 이후 날짜는 목록에서 숨김
    const entry = entryForDate(key);
    const li = document.createElement("li");
    const btn = document.createElement("button");
    btn.type = "button";
    if (selectedDate === key) btn.classList.add("active");
    const label = `${day}일 (${WEEKDAYS_KO[date.getDay()]})`;
    // 실제 작성 내용이 있을 때만 점 표시 (빈 초안은 표시 안 함)
    const right = entryHasContent(entry)
      ? `<span class="dot${entry._draft ? " draft" : ""}" title="${entry._draft ? "미저장" : "기록"}"></span>`
      : `<span class="muted">—</span>`;
    btn.innerHTML = `<span>${label}</span>${right}`;
    btn.addEventListener("click", () => selectDay(key));
    li.appendChild(btn);
    ul.appendChild(li);
  }
}

function selectDay(dateStr) {
  if (editMode && selectedDate && selectedDate !== dateStr) {
    abandonEditThenSelect(dateStr).catch((e) => showToast(e.message || String(e)));
    return;
  }
  flushEditorToDoc();
  selectedDate = dateStr;
  let entry = entryForDate(dateStr);
  if (!entry) {
    entry = createEmptyEntry(dateStr);
    entry._ephemeral = true; // monthDoc에 아직 넣지 않음
  }
  // 스케줄이 없으면 템플릿 체크를 강제 해제 (스케줄 기준으로만 켜짐)
  if (!schedulesForDate(dateStr).length) {
    entry.templates = [];
  }
  editorEntry = entry;
  renderDayList();
  openEditor(entry);
}

/** 작성 모드에서 다른 날짜로 이동 시: 미저장 초안을 버리고 저장된 내용으로 복원 */
async function abandonEditThenSelect(dateStr) {
  if (monthDirty && !confirm("작성 중인 내용이 있습니다. 다른 날짜로 이동할까요? (저장하지 않은 내용은 버려집니다)")) {
    return;
  }
  await releaseEditLock({ silent: true });
  editMode = false;
  stopEditHeartbeat();
  monthDirty = false;
  try {
    const path = `work-logs/${monthKey()}.json`;
    const { data, revision } = await readJsonFile(path);
    monthRevision = revision;
    monthDoc = data && Array.isArray(data.entries) ? data : { version: 1, entries: [] };
    monthDoc.entries.forEach((e) => {
      delete e._draft;
    });
  } catch {
    // NAS 재로드 실패 시에도 로컬 초안 표시는 하지 않도록 draft 제거
    monthDoc.entries = (monthDoc.entries || []).filter((e) => !e._draft && !e._ephemeral);
  }
  await loadScheduleForMonth().catch(() => {});
  selectedDate = dateStr;
  let entry = entryForDate(dateStr);
  if (!entry) {
    entry = createEmptyEntry(dateStr);
    entry._ephemeral = true;
  }
  if (!schedulesForDate(dateStr).length) entry.templates = [];
  editorEntry = entry;
  renderDayList();
  openEditor(entry);
  applyEditModeUi();
  startEditLockPoll();
}

function createEmptyEntry(dateStr) {
  const now = new Date().toISOString();
  const daySchedules = schedulesForDate(dateStr);
  return {
    id: crypto.randomUUID(),
    date: dateStr,
    templates: [],
    workTemplateNote: daySchedules.map((s) => s.scheduleTitle).filter(Boolean).join(", "),
    log: "",
    logA: "",
    logB: "",
    isPrinted: false,
    sourceScheduleIds: daySchedules.map((s) => s.id).filter(Boolean),
    createdBy: profile?.id || "",
    updatedBy: profile?.id || "",
    createdAt: now,
    updatedAt: now,
  };
}

function openEditor(entry) {
  normalizeEntryLogs(entry);
  $("emptyEditor").classList.add("hidden");
  $("entryEditor").classList.remove("hidden");
  $("entryDateLabel").textContent = formatDateHeading(entry.date);
  $("entryPrinted").checked = Boolean(entry.isPrinted);
  $("entryLogA").value = entry.logA || "";
  $("entryLogB").value = entry.logB || "";
  updateNotTodayWarn(entry.date);
  $("entryMeta").textContent = [
    entry.createdBy ? `작성: ${profileName(entry.createdBy)}` : null,
    entry.updatedBy ? `수정: ${profileName(entry.updatedBy)}` : null,
    entry._draft || entry._ephemeral ? "미저장" : null,
  ]
    .filter(Boolean)
    .join(" · ");

  renderTemplateChips(entry);
  renderScheduleDetail(entry.date);
  renderSpecialNotesUi();
  applyEditModeUi();
  refreshEditLockStatus().catch(() => {});
}

/* ---- 날짜별 작성 잠금 (NAS locks/*.json) ---- */
function stopEditHeartbeat() {
  if (editHeartbeatTimer) {
    clearInterval(editHeartbeatTimer);
    editHeartbeatTimer = null;
  }
}

function stopEditLockPoll() {
  if (editLockPollTimer) {
    clearInterval(editLockPollTimer);
    editLockPollTimer = null;
  }
}

function startEditHeartbeat() {
  stopEditHeartbeat();
  editHeartbeatTimer = setInterval(() => {
    if (!editMode || !selectedDate) return;
    heartbeatEditLock().catch(() => {});
  }, EDIT_LOCK_HEARTBEAT_MS);
}

function startEditLockPoll() {
  stopEditLockPoll();
  editLockPollTimer = setInterval(() => {
    if (!selectedDate || editMode) return;
    refreshEditLockStatus().catch(() => {});
  }, EDIT_LOCK_POLL_MS);
}

function applyEditModeUi() {
  const editor = $("entryEditor");
  const logA = $("entryLogA");
  const logB = $("entryLogB");
  const startBtn = $("btnStartEdit");
  const cancelBtn = $("btnCancelEdit");
  const deleteBtn = $("btnDeleteEntry");
  const status = $("entryLockStatus");
  if (!editor || !logA || !logB) return;

  editor.classList.toggle("is-readonly", !editMode);
  logA.readOnly = !editMode;
  logB.readOnly = !editMode;
  $("btnBasicRecord").disabled = !editMode;
  $("entryPrinted").disabled = !editMode;

  if (cancelBtn) cancelBtn.classList.toggle("hidden", !editMode);

  const heldByOther =
    editLockHolder &&
    editLockHolder.sessionId &&
    editLockHolder.sessionId !== editSessionId;
  const holderName =
    editLockHolder?.actorName ||
    profileName(editLockHolder?.actorId) ||
    "다른 사용자";

  if (status) {
    if (editMode) {
      status.classList.remove("hidden", "is-busy");
      status.classList.add("is-mine");
      status.textContent = "작성 모드입니다. 저장하면 잠금이 해제됩니다.";
    } else if (heldByOther) {
      status.classList.remove("hidden", "is-mine");
      status.classList.add("is-busy");
      status.textContent = `${holderName}님이 작성 중입니다.`;
    } else {
      status.classList.add("hidden");
      status.classList.remove("is-busy", "is-mine");
      status.textContent = "";
    }
  }

  if (startBtn) {
    startBtn.disabled = Boolean(heldByOther);
    startBtn.textContent = editMode ? "저장" : "일지 작성/수정";
  }

  if (deleteBtn) {
    const hideDelete = editMode || Boolean(heldByOther);
    deleteBtn.classList.toggle("hidden", hideDelete);
    deleteBtn.disabled = hideDelete;
  }
}

async function fetchEditLock(dateStr) {
  const res = await api(`/api/edit-lock?date=${encodeURIComponent(dateStr)}`);
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || "잠금 조회 실패");
  }
  return res.json();
}

async function refreshEditLockStatus() {
  if (!selectedDate || !connected) {
    editLockHolder = null;
    applyEditModeUi();
    return;
  }
  try {
    const data = await fetchEditLock(selectedDate);
    editLockHolder = data?.lock || null;
    if (
      editMode &&
      editLockHolder &&
      editLockHolder.sessionId &&
      editLockHolder.sessionId !== editSessionId
    ) {
      // 다른 세션이 잠금을 가져간 경우
      editMode = false;
      stopEditHeartbeat();
      showToast(
        `${editLockHolder.actorName || profileName(editLockHolder.actorId) || "다른 사용자"}님이 작성 중이라 작성 모드가 해제되었습니다.`,
      );
    }
    applyEditModeUi();
  } catch (err) {
    console.warn("edit-lock poll", err);
  }
  if (!editMode) startEditLockPoll();
}

async function acquireEditLock() {
  if (!selectedDate || !profile) throw new Error("날짜와 프로필이 필요합니다.");
  const res = await api("/api/edit-lock/acquire", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      date: selectedDate,
      sessionId: editSessionId,
      actorId: profile.id,
      actorName: profile.displayName || profileName(profile.id),
    }),
  });
  const body = await res.json().catch(() => ({}));
  if (res.status === 409) {
    const lock = body.lock || body;
    const err = new Error("locked");
    err.lock = lock;
    throw err;
  }
  if (!res.ok) throw new Error(body.message || "작성 잠금 실패");
  return body.lock || body;
}

async function heartbeatEditLock() {
  if (!selectedDate || !editMode) return;
  const res = await api("/api/edit-lock/heartbeat", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      date: selectedDate,
      sessionId: editSessionId,
    }),
  });
  if (res.status === 409) {
    editMode = false;
    stopEditHeartbeat();
    await refreshEditLockStatus();
    showToast("작성 잠금이 만료되었거나 다른 사용자가 가져갔습니다.");
    return;
  }
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    console.warn("heartbeat", err.message);
  }
}

function releaseEditLockBeacon() {
  if (!editMode || !selectedDate) return;
  const payload = JSON.stringify({
    date: selectedDate,
    sessionId: editSessionId,
  });
  const url = `${apiBase}/api/edit-lock/release`;
  try {
    if (navigator.sendBeacon) {
      navigator.sendBeacon(url, new Blob([payload], { type: "application/json" }));
      return;
    }
  } catch {
    // fall through
  }
  fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: payload,
    keepalive: true,
    cache: "no-store",
  }).catch(() => {});
}

async function releaseEditLock({ silent = false } = {}) {
  stopEditHeartbeat();
  const date = selectedDate;
  const wasEditing = editMode;
  editMode = false;
  if (!date || !wasEditing) {
    applyEditModeUi();
    startEditLockPoll();
    return;
  }
  try {
    await api("/api/edit-lock/release", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ date, sessionId: editSessionId }),
    });
  } catch (err) {
    if (!silent) console.warn("release lock", err);
  }
  editLockHolder = null;
  applyEditModeUi();
  startEditLockPoll();
}

async function startEditMode() {
  if (!selectedDate || !profile) return;
  if (editMode) return;
  await refreshEditLockStatus();
  if (
    editLockHolder &&
    editLockHolder.sessionId &&
    editLockHolder.sessionId !== editSessionId
  ) {
    const name =
      editLockHolder.actorName ||
      profileName(editLockHolder.actorId) ||
      "다른 사용자";
    showToast(`현재 ${name}님이 작성 중이기 때문에 작성할 수 없습니다.`);
    applyEditModeUi();
    return;
  }
  try {
    const lock = await acquireEditLock();
    editLockHolder = lock;
    editMode = true;
    stopEditLockPoll();
    startEditHeartbeat();
    try {
      const path = `work-logs/${monthKey()}.json`;
      const { data, revision } = await readJsonFile(path);
      monthRevision = revision;
      monthDoc = data && Array.isArray(data.entries) ? data : { version: 1, entries: [] };
      monthDoc.entries.forEach((e) => {
        delete e._draft;
      });
      monthDirty = false;
      let entry = entryForDate(selectedDate);
      if (!entry) {
        entry = createEmptyEntry(selectedDate);
        entry._ephemeral = true;
      }
      if (!schedulesForDate(selectedDate).length) entry.templates = [];
      editorEntry = entry;
      openEditor(entry);
    } catch {
      openEditor(editorEntry || entryForDate(selectedDate) || createEmptyEntry(selectedDate));
    }
    editMode = true;
    applyEditModeUi();
    $("entryLogA")?.focus();
    showToast("작성 모드를 시작했습니다.");
  } catch (err) {
    if (err.message === "locked" || err.lock) {
      const lock = err.lock || {};
      editLockHolder = lock;
      const name = lock.actorName || profileName(lock.actorId) || "다른 사용자";
      showToast(`현재 ${name}님이 작성 중이기 때문에 작성할 수 없습니다.`);
      applyEditModeUi();
      return;
    }
    showToast(err.message || "작성 모드를 시작할 수 없습니다.");
  }
}

async function cancelEditMode() {
  if (!editMode) return;
  if (monthDirty && !confirm("저장하지 않은 변경을 버리고 작성 모드를 끝낼까요?")) {
    return;
  }
  const date = selectedDate;
  await releaseEditLock({ silent: true });
  editMode = false;
  stopEditHeartbeat();
  monthDirty = false;
  if (date) {
    try {
      await loadMonth({ selectDate: date, skipFlush: true });
    } catch {
      selectDay(date);
    }
  }
  showToast("작성 모드를 취소했습니다.");
}

function updateNotTodayWarn(dateStr) {
  const warn = $("entryNotTodayWarn");
  if (!warn) return;
  const isToday = dateStr === todayYmd();
  warn.classList.toggle("hidden", isToday);
}

function formatDateHeading(dateStr) {
  const d = new Date(dateStr + "T12:00:00");
  return `${dateStr} (${WEEKDAYS_KO[d.getDay()]})`;
}

async function goToToday() {
  const today = todayYmd();
  if (selectedDate === today) {
    showToast("이미 오늘 날짜입니다.");
    return;
  }
  const targetMonth = startOfMonth(new Date(today + "T12:00:00"));
  if (monthKey(monthCursor) !== monthKey(targetMonth)) {
    if (monthDirty && !confirm("저장하지 않은 변경이 있습니다. 계속할까요?")) return;
    monthDirty = false;
    monthCursor = targetMonth;
    await loadMonth({ selectToday: true });
    return;
  }
  selectDay(today);
}

function renderTemplateChips(entry) {
  const chips = $("templateChips");
  chips.innerHTML = "";
  const active = new Set(entry.templates || []);
  const hasSchedule = schedulesForDate(entry.date).length > 0;
  for (const t of catalogTemplates()) {
    const chip = document.createElement("button");
    chip.type = "button";
    chip.className = `chip${active.has(t.id) ? " on" : ""}`;
    chip.textContent = t.label;
    chip.dataset.id = t.id;
    if (t.custom) chip.classList.add("custom");
    chip.addEventListener("click", () => onTemplateChipClick(chip, t.id));
    chips.appendChild(chip);
  }
  // 스케줄 없는 날은 시각적으로도 모두 꺼진 상태 유지
  if (!hasSchedule) {
    chips.querySelectorAll(".chip.on").forEach((c) => c.classList.remove("on"));
  }
}

function onTemplateChipClick(chip, templateId) {
  if (!editMode) {
    showToast("일지 작성/수정 버튼을 눌러 작성 모드로 들어가세요.");
    return;
  }
  const turningOn = !chip.classList.contains("on");
  chip.classList.toggle("on", turningOn);
  flushEditorToDoc();
  const entry = editorEntry || entryForDate(selectedDate);
  if (!entry) return;

  const daySchedules = schedulesForDate(selectedDate);
  // 스케줄 없는 날짜에서는 템플릿을 켤 수 없음
  if (!daySchedules.length) {
    chip.classList.remove("on");
    entry.templates = [];
    showToast("스케줄이 없는 날짜입니다. 템플릿은 스케줄 기준으로만 켜집니다.");
    return;
  }

  if (turningOn) {
    const cfg = defaultsDoc.templateButtons?.[templateId];
    const primary = daySchedules[0] || {};
    const vars = scheduleVars(primary, selectedDate);
    entry.workTemplateNote = daySchedules.map((s) => s.scheduleTitle).filter(Boolean).join(", ");
    if (cfg?.appendLog) {
      const line = applyPlaceholders(cfg.appendLog, vars);
      normalizeEntryLogs(entry);
      const target = normalizeAppendTarget(cfg.appendTarget);
      if (target === "B") {
        entry.logB = appendUniqueLines($("entryLogB")?.value || entry.logB || "", [line]);
        if ($("entryLogB")) $("entryLogB").value = entry.logB;
      } else {
        entry.logA = appendUniqueLines($("entryLogA")?.value || entry.logA || "", [line]);
        entry.log = entry.logA;
        if ($("entryLogA")) $("entryLogA").value = entry.logA;
      }
    }
  }
  monthDirty = true;
  $("entryMeta").textContent = [
    entry.createdBy ? `작성: ${profileName(entry.createdBy)}` : null,
    `수정: ${profileName(profile)}`,
    entry._draft || entry._ephemeral ? "미저장" : null,
  ]
    .filter(Boolean)
    .join(" · ");
}

function renderScheduleDetail(dateStr) {
  const box = $("scheduleDetail");
  const related = schedulesForDate(dateStr);
  if (!related.length) {
    box.innerHTML = `<p class="muted">이 날짜의 스케줄 없음${scheduleFileName ? ` (${escapeHtml(scheduleFileName)})` : ""}</p>`;
    return;
  }
  box.innerHTML = related
    .map((s) => {
      const selectedPrep = new Set(preparationList(s.preparation));
      const prepHtml = Object.entries(PREPARATION_LABELS)
        .map(
          ([id, label]) =>
            `<span class="prep-chip${selectedPrep.has(id) ? " on" : ""}">${escapeHtml(label)}</span>`,
        )
        .join("");
      return `<article class="schedule-card">
        <h4>${escapeHtml(s.scheduleTitle || "(제목 없음)")}</h4>
        <dl>
          <div><dt>시작시간</dt><dd>${escapeHtml(formatStartTime(s.startTime))}</dd></div>
          <div><dt>위치</dt><dd>${escapeHtml(placeLabel(s.place))}</dd></div>
          <div><dt>강사명</dt><dd>${escapeHtml(s.sermonSpeaker || "-")}</dd></div>
          <div><dt>부가정보</dt><dd>${escapeHtml(s.accessoryInfo || "-")}</dd></div>
          <div><dt>설명</dt><dd>${escapeHtml(s.preaparationNote || s.preparationNote || "-")}</dd></div>
          <div class="schedule-prep"><dt>사전준비</dt><dd class="prep-row">${prepHtml}</dd></div>
        </dl>
      </article>`;
    })
    .join("");
}

function readEditorInto(entry) {
  entry.isPrinted = $("entryPrinted").checked;
  entry.logA = $("entryLogA")?.value || "";
  entry.logB = $("entryLogB")?.value || "";
  entry.log = entry.logA;
  const daySchedules = schedulesForDate(entry.date);
  if (!daySchedules.length) {
    entry.templates = [];
  } else {
    entry.templates = [...$("templateChips").querySelectorAll(".chip.on")].map((el) => el.dataset.id);
  }
  entry.workTemplateNote = daySchedules.map((s) => s.scheduleTitle).filter(Boolean).join(", ");
  entry.sourceScheduleIds = daySchedules.map((s) => s.id).filter(Boolean);
}

function flushEditorToDoc() {
  if (!selectedDate || !editorEntry) return;
  readEditorInto(editorEntry);

  if (entryHasContent(editorEntry)) {
    if (editorEntry._ephemeral) {
      delete editorEntry._ephemeral;
      editorEntry._draft = true;
      if (!entryForDate(selectedDate)) monthDoc.entries.push(editorEntry);
    }
    monthDirty = true;
  } else {
    // 내용 없는 빈 항목은 목록에서 제거
    if (editorEntry._ephemeral) {
      // monthDoc에 안 넣음
    } else {
      monthDoc.entries = monthDoc.entries.filter((e) => e.date !== selectedDate);
      editorEntry._ephemeral = true;
      monthDirty = true;
    }
  }
}

/** 선택 날짜를 스케줄 + 제목 규칙으로만 채움 (스케줄 필수) */
function applyBasicScheduleRecord() {
  if (!editMode) {
    showToast("일지 작성/수정 버튼을 눌러 작성 모드로 들어가세요.");
    return;
  }
  if (!selectedDate || !profile) return;
  flushEditorToDoc();
  const daySchedules = schedulesForDate(selectedDate);
  if (!daySchedules.length) {
    showToast("이 날짜의 스케줄이 없습니다.");
    return;
  }

  let entry = editorEntry || entryForDate(selectedDate);
  if (!entry) {
    entry = createEmptyEntry(selectedDate);
    entry._ephemeral = true;
  }
  editorEntry = entry;

  // 기존 템플릿을 이어받지 않고, 매칭된 규칙으로만 설정
  const templateSet = new Set();
  const logLinesA = [];
  const logLinesB = [];

  for (const schedule of daySchedules) {
    const vars = scheduleVars(schedule, selectedDate);
    for (const rule of defaultsDoc.titleRules || []) {
      if (!ruleMatchesSchedule(rule, schedule)) continue;
      if (rule.record) {
        const line = applyPlaceholders(rule.record, vars);
        if (normalizeAppendTarget(rule.appendTarget) === "B") logLinesB.push(line);
        else logLinesA.push(line);
      }
      for (const tid of rule.templates || []) templateSet.add(tid);
      break; // 위에서부터 처음 맞는 규칙만 적용
    }
  }

  entry.templates = [...templateSet];
  normalizeEntryLogs(entry);
  entry.logA = appendUniqueLines(entry.logA || "", logLinesA);
  entry.logB = appendUniqueLines(entry.logB || "", logLinesB);
  entry.log = entry.logA;
  entry.workTemplateNote = daySchedules.map((s) => s.scheduleTitle).filter(Boolean).join(", ");
  entry.sourceScheduleIds = daySchedules.map((s) => s.id).filter(Boolean);
  entry.updatedBy = profile.id;
  entry.updatedAt = new Date().toISOString();

  if (entry._ephemeral && entryHasContent(entry)) {
    delete entry._ephemeral;
    entry._draft = true;
    if (!entryForDate(selectedDate)) monthDoc.entries.push(entry);
  }
  monthDirty = true;
  openEditor(entry);
  renderDayList();
  showToast("기본 일정 기록 반영");
  appendAudit("basic_record", entry, "기본 일정 기록");
}

async function loadScheduleForMonth() {
  schedules = [];
  scheduleFileName = "";
  try {
    const filesRes = await api("/api/schedule/files");
    if (!filesRes.ok) return;
    const files = await filesRes.json();
    const preferred = ["schedule-data.json", "scheduledata.json", "schedule.json"];
    const pick =
      preferred.find((n) => files.some((f) => f.toLowerCase() === n)) ||
      files.find((f) => f.toLowerCase().includes("schedule")) ||
      files[0];
    if (!pick) return;
    scheduleFileName = pick;
    const res = await api(`/api/schedule/file?name=${encodeURIComponent(pick)}`);
    if (!res.ok) return;
    const data = await res.json();
    schedules = Array.isArray(data) ? data : data.entries || data.schedules || [];
  } catch (err) {
    console.warn("schedule load", err);
  }
}

async function loadMonth(options = {}) {
  if (!options.skipFlush) flushEditorToDoc();
  if (monthDirty && !confirm("저장하지 않은 변경이 있습니다. 계속할까요?")) return;

  const path = `work-logs/${monthKey()}.json`;
  const { data, revision } = await readJsonFile(path);
  monthRevision = revision;
  monthDoc = data && Array.isArray(data.entries) ? data : { version: 1, entries: [] };
  monthDoc.entries.forEach((e) => {
    delete e._draft;
    normalizeEntryLogs(e);
  });
  monthDirty = false;
  editMode = false;
  stopEditHeartbeat();
  await loadScheduleForMonth();
  await loadSpecialNotes().catch(() => {});
  renderDayList();

  const today = todayYmd();
  const inThisMonth = today.startsWith(monthKey());
  if (options.selectToday && inThisMonth) {
    selectDay(today);
  } else if (options.selectDate) {
    selectDay(options.selectDate);
  } else if (selectedDate && selectedDate.startsWith(monthKey())) {
    selectDay(selectedDate);
  } else if (inThisMonth) {
    selectDay(today);
  } else {
    selectedDate = null;
    $("entryEditor").classList.add("hidden");
    $("emptyEditor").classList.remove("hidden");
    $("emptyEditor").textContent = "날짜를 선택하세요.";
  }
  applyEditModeUi();
  startEditLockPoll();
}

async function saveCurrentEntry() {
  if (!editMode) {
    showToast("일지 작성/수정 버튼을 눌러 작성 모드로 들어가세요.");
    return;
  }
  flushEditorToDoc();
  const entry = editorEntry || entryForDate(selectedDate);
  if (entry) {
    entry.updatedBy = profile?.id || entry.updatedBy;
    entry.updatedAt = new Date().toISOString();
    appendAudit("update", entry, `일지 저장: ${templateLabel((entry.templates || [])[0] || "") || "내용 수정"}`);
  }
  await saveMonth();
}

async function saveMonth() {
  flushEditorToDoc();
  if (!profile) return showToast("프로필이 없습니다.");

  // 빈 항목 제거
  monthDoc.entries = monthDoc.entries.filter((e) => entryHasContent(e));

  const now = new Date().toISOString();
  for (const entry of monthDoc.entries) {
    normalizeEntryLogs(entry);
    if (entry._draft) {
      entry.createdBy = entry.createdBy || profile.id;
      entry.createdAt = entry.createdAt || now;
    }
    entry.updatedBy = profile.id;
    entry.updatedAt = now;
    delete entry._draft;
    delete entry._ephemeral;
  }

  const path = `work-logs/${monthKey()}.json`;
  const payload = { version: 1, entries: monthDoc.entries };
  try {
    const result = await writeJsonFile(path, payload, monthRevision);
    monthRevision = result.revision;
    monthDirty = false;
    if (specialNotesDirty) await saveSpecialNotes({ quiet: true });
    await appendAudit("save_month", { id: "", date: `${monthKey()}-01` }, `${monthKey()} 월 저장 (${monthDoc.entries.length}건)`);
    if (editMode) await releaseEditLock({ silent: true });
    showToast("저장됨");
    renderDayList();
    if (selectedDate) {
      const e = entryForDate(selectedDate);
      if (e) openEditor(e);
    }
  } catch (err) {
    if (err.conflict) {
      showToast("충돌: 다른 기기에서 수정됨. 다시 불러옵니다.");
      monthDirty = false;
      await loadMonth({ selectDate: selectedDate });
      return;
    }
    showToast(err.message || "저장 실패");
  }
}

function shiftMonth(delta) {
  if (editMode) {
    if (monthDirty && !confirm("저장하지 않은 변경이 있습니다. 계속할까요? (저장하지 않은 내용은 버려집니다)")) return;
    releaseEditLock({ silent: true });
    editMode = false;
    stopEditHeartbeat();
  } else if (monthDirty && !confirm("저장하지 않은 변경이 있습니다. 계속할까요?")) {
    return;
  }
  monthDirty = false;
  monthCursor = new Date(monthCursor.getFullYear(), monthCursor.getMonth() + delta, 1);
  loadMonth({ selectToday: true }).catch((e) => showToast(e.message));
}

/* ---- Rules modal ---- */
function openRulesModal() {
  renderTitleRulesEditor();
  renderTemplateButtonsEditor();
  const maxInput = $("specialNotesMaxInput");
  if (maxInput) maxInput.value = String(specialNotesMax());
  switchRulesTab("titleRules");
  $("modalRules").classList.remove("hidden");
}

function switchRulesTab(name) {
  document.querySelectorAll(".tab").forEach((t) => t.classList.toggle("on", t.dataset.tab === name));
  $("tab-titleRules")?.classList.toggle("hidden", name !== "titleRules");
  $("tab-templateButtons")?.classList.toggle("hidden", name !== "templateButtons");
  $("tab-general")?.classList.toggle("hidden", name !== "general");
}

function renderTitleRulesEditor() {
  const list = $("titleRulesList");
  list.innerHTML = "";
  const rules = defaultsDoc.titleRules || [];
  if (!rules.length) {
    list.innerHTML = `<p class="muted">규칙 없음 — 규칙 추가로 만드세요.</p>`;
  }
  rules.forEach((rule, index) => {
    const cond = normalizeConditionType(rule.conditionType);
    const condLabel = RULE_CONDITION_FIELDS.find((f) => f.id === cond)?.label || cond;
    const valueLabel = (rule.value || "").trim() || "새 규칙";
    const recordPreview = (rule.record || "").trim().slice(0, 40);
    const tplCount = (rule.templates || []).length;
    const target = normalizeAppendTarget(rule.appendTarget);
    const details = document.createElement("details");
    details.className = "fold rule-fold";
    details.dataset.index = String(index);
    if (rule.id) details.dataset.ruleId = rule.id;
    if (!rule.value && !rule.record) details.open = true;

    const condOptions = RULE_CONDITION_FIELDS.map(
      (f) => `<option value="${f.id}" ${cond === f.id ? "selected" : ""}>${escapeHtml(f.label)}</option>`
    ).join("");

    details.innerHTML = `
      <summary class="fold-summary has-order">
        <span class="fold-title">${escapeHtml(valueLabel)}</span>
        <span class="fold-meta">${escapeHtml(condLabel)} · ${target} · ${tplCount ? `템플릿 ${tplCount}` : "템플릿 없음"}${recordPreview ? ` · ${escapeHtml(recordPreview)}${(rule.record || "").length > 40 ? "…" : ""}` : ""}</span>
        <span class="fold-order">
          <button type="button" class="btn ghost btn-rule-up" title="우선순위 올리기" ${index === 0 ? "disabled" : ""}>↑</button>
          <button type="button" class="btn ghost btn-rule-down" title="우선순위 내리기" ${index >= rules.length - 1 ? "disabled" : ""}>↓</button>
          <button type="button" class="btn danger btn-remove-rule fold-remove" title="삭제">삭제</button>
        </span>
      </summary>
      <div class="fold-body rule-grid">
        <label>조건 필드
          <select data-field="conditionType">${condOptions}</select>
        </label>
        <label>값 (or / and)
          <input data-field="value" type="text" value="${escapeHtml(rule.value || "")}" placeholder="예: 집회or지원 또는 집회and지원" />
        </label>
        <label class="span-2">기록 위치
          <select data-field="appendTarget">
            <option value="A"${target === "A" ? " selected" : ""}>A. 교회 스케줄에 따른 업무내용</option>
            <option value="B"${target === "B" ? " selected" : ""}>B. 교회 일정 외 작업/업무내용</option>
          </select>
        </label>
        <label class="span-2">기록
          <textarea data-field="record" rows="2" placeholder="{schedule_title} 대강당 방송 및 유튜브 송출 준비.">${escapeHtml(rule.record || "")}</textarea>
        </label>
        <details class="fold nested-fold span-2">
          <summary class="fold-summary compact">
            <span class="fold-title">체크할 템플릿</span>
            <span class="fold-meta tpl-count">${tplCount}개 선택</span>
          </summary>
          <div class="fold-body">
            <div class="mini-chips" data-field="templates"></div>
          </div>
        </details>
      </div>
    `;

    const chips = details.querySelector(".mini-chips");
    const countEl = details.querySelector(".tpl-count");
    const selected = new Set(rule.templates || []);
    const updateTplCount = () => {
      const n = chips.querySelectorAll(".chip.on").length;
      countEl.textContent = `${n}개 선택`;
      const meta = details.querySelector(":scope > .fold-summary > .fold-meta");
      const val = details.querySelector('[data-field="value"]')?.value.trim() || "새 규칙";
      const rec = details.querySelector('[data-field="record"]')?.value.trim() || "";
      const fieldId = details.querySelector('[data-field="conditionType"]')?.value;
      const fieldLabel = RULE_CONDITION_FIELDS.find((f) => f.id === fieldId)?.label || fieldId;
      const tgt = normalizeAppendTarget(details.querySelector('[data-field="appendTarget"]')?.value);
      details.querySelector(":scope > .fold-summary > .fold-title").textContent = val;
      meta.textContent = `${fieldLabel} · ${tgt} · ${n ? `템플릿 ${n}` : "템플릿 없음"}${rec ? ` · ${rec.slice(0, 40)}${rec.length > 40 ? "…" : ""}` : ""}`;
    };
    details.querySelector('[data-field="appendTarget"]')?.addEventListener("change", updateTplCount);
    details.querySelector('[data-field="value"]')?.addEventListener("input", updateTplCount);
    details.querySelector('[data-field="record"]')?.addEventListener("input", updateTplCount);
    details.querySelector('[data-field="conditionType"]')?.addEventListener("change", updateTplCount);
    for (const t of catalogTemplates()) {
      const b = document.createElement("button");
      b.type = "button";
      b.className = `chip${selected.has(t.id) ? " on" : ""}`;
      b.textContent = t.label;
      b.dataset.id = t.id;
      b.addEventListener("click", () => {
        b.classList.toggle("on");
        updateTplCount();
      });
      chips.appendChild(b);
    }

    details.querySelector(".btn-remove-rule").addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      const all = collectTitleRulesFromEditor();
      all.splice(Number(details.dataset.index), 1);
      defaultsDoc.titleRules = all;
      renderTitleRulesEditor();
    });
    details.querySelector(".btn-rule-up").addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      moveTitleRule(Number(details.dataset.index), -1);
    });
    details.querySelector(".btn-rule-down").addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      moveTitleRule(Number(details.dataset.index), 1);
    });
    list.appendChild(details);
  });
}

function moveTitleRule(index, delta) {
  const all = collectTitleRulesFromEditor();
  const next = index + delta;
  if (next < 0 || next >= all.length) return;
  const [item] = all.splice(index, 1);
  all.splice(next, 0, item);
  defaultsDoc.titleRules = all;
  renderTitleRulesEditor();
}

function collectTitleRulesFromEditor() {
  const rows = [...$("titleRulesList").querySelectorAll(".rule-fold")];
  return rows.map((row) => ({
    id: row.dataset.ruleId || crypto.randomUUID(),
    conditionType: normalizeConditionType(row.querySelector('[data-field="conditionType"]').value),
    value: row.querySelector('[data-field="value"]').value.trim(),
    record: row.querySelector('[data-field="record"]').value,
    appendTarget: normalizeAppendTarget(row.querySelector('[data-field="appendTarget"]')?.value),
    templates: [...row.querySelectorAll(".mini-chips .chip.on")].map((c) => c.dataset.id),
  }));
}

function renderTemplateButtonsEditor() {
  const list = $("templateButtonsList");
  list.innerHTML = "";
  const map = defaultsDoc.templateButtons || {};
  for (const t of catalogTemplates()) {
    const cfg = map[t.id] || { appendLog: "", appendTarget: "A" };
    const target = normalizeAppendTarget(cfg.appendTarget);
    const hasText = Boolean((cfg.appendLog || "").trim());
    const details = document.createElement("details");
    details.className = "fold rule-fold";
    details.innerHTML = `
      <summary class="fold-summary has-actions">
        <span class="fold-title">${escapeHtml(t.label)}${t.custom ? " · 사용자" : ""}</span>
        <span class="fold-meta">${hasText ? `${target} · ${escapeHtml(cfg.appendLog.trim().slice(0, 32))}${cfg.appendLog.trim().length > 32 ? "…" : ""}` : `추가 기록 없음 · ${target}`}</span>
        ${t.custom ? `<span class="fold-order"><button type="button" class="btn danger fold-remove btn-remove-custom" data-id="${escapeHtml(t.id)}">삭제</button></span>` : ""}
      </summary>
      <div class="fold-body">
        <label class="field-block">기록 위치
          <select data-template-target="${escapeHtml(t.id)}">
            <option value="A"${target === "A" ? " selected" : ""}>A. 교회 스케줄에 따른 업무내용</option>
            <option value="B"${target === "B" ? " selected" : ""}>B. 교회 일정 외 작업/업무내용</option>
          </select>
        </label>
        <label class="field-block">추가 기록
          <textarea data-template-id="${escapeHtml(t.id)}" rows="2" placeholder="버튼을 켤 때 선택한 영역에 추가">${escapeHtml(cfg.appendLog || "")}</textarea>
        </label>
      </div>
    `;
    const ta = details.querySelector("textarea");
    const sel = details.querySelector("select[data-template-target]");
    const meta = details.querySelector(".fold-meta");
    const refreshMeta = () => {
      const v = ta.value.trim();
      const tgt = normalizeAppendTarget(sel.value);
      meta.textContent = v ? `${tgt} · ${v.slice(0, 32)}${v.length > 32 ? "…" : ""}` : `추가 기록 없음 · ${tgt}`;
    };
    ta.addEventListener("input", refreshMeta);
    sel.addEventListener("change", refreshMeta);
    const removeBtn = details.querySelector(".btn-remove-custom");
    if (removeBtn) {
      removeBtn.addEventListener("click", (e) => {
        e.preventDefault();
        e.stopPropagation();
        removeCustomTemplate(t.id);
      });
    }
    list.appendChild(details);
  }
}

function addCustomTemplate(label) {
  const name = (label || "").trim();
  if (!name) {
    showToast("템플릿 이름을 입력하세요.");
    return false;
  }
  defaultsDoc.customTemplates = defaultsDoc.customTemplates || [];
  const id = `custom_${Date.now().toString(36)}`;
  defaultsDoc.customTemplates.push({ id, label: name });
  defaultsDoc.templateButtons = defaultsDoc.templateButtons || {};
  defaultsDoc.templateButtons[id] = { appendLog: "", appendTarget: "A" };
  return true;
}

function removeCustomTemplate(id) {
  defaultsDoc.customTemplates = (defaultsDoc.customTemplates || []).filter((t) => t.id !== id);
  if (defaultsDoc.templateButtons) delete defaultsDoc.templateButtons[id];
  for (const rule of defaultsDoc.titleRules || []) {
    rule.templates = (rule.templates || []).filter((tid) => tid !== id);
  }
  renderTemplateButtonsEditor();
  if (editorEntry) renderTemplateChips(editorEntry);
  showToast("사용자 템플릿 삭제됨 (저장을 눌러 NAS에 반영)");
}

function collectTemplateButtonsFromEditor() {
  const map = {};
  $("templateButtonsList").querySelectorAll("textarea[data-template-id]").forEach((ta) => {
    const id = ta.dataset.templateId;
    const appendLog = ta.value.trim();
    const sel = $("templateButtonsList").querySelector(`select[data-template-target="${CSS.escape(id)}"]`);
    const appendTarget = normalizeAppendTarget(sel?.value);
    if (appendLog) map[id] = { appendLog, appendTarget };
  });
  return map;
}

async function saveRules() {
  try {
    // 열린 탭의 DOM 값을 메모리에 반영
    if (!$("tab-titleRules").classList.contains("hidden")) {
      defaultsDoc.titleRules = collectTitleRulesFromEditor();
    }
    if ($("templateButtonsList")?.children.length) {
      defaultsDoc.templateButtons = collectTemplateButtonsFromEditor();
    }

    const maxRaw = Number($("specialNotesMaxInput")?.value);
    const specialMax = Number.isFinite(maxRaw)
      ? Math.min(50, Math.max(1, Math.round(maxRaw)))
      : specialNotesMax();

    const next = {
      version: 2,
      specialNotesMax: specialMax,
      customTemplates: defaultsDoc.customTemplates || [],
      titleRules: defaultsDoc.titleRules || [],
      templateButtons: defaultsDoc.templateButtons || {},
    };
    const result = await writeJsonFile("defaults.json", next, defaultsRevision);
    defaultsDoc = normalizeDefaults(next);
    defaultsRevision = result.revision;
    await appendAudit("update_defaults", { id: "", date: todayYmd() }, "기록 규칙 저장");
    $("modalRules").classList.add("hidden");
    if (editorEntry) renderTemplateChips(editorEntry);
    if (specialNotes.length > specialNotesMax()) {
      showToast(`특이사항이 ${specialNotes.length}건입니다. 최대 ${specialNotesMax()}건으로 줄이려면 메모를 삭제하세요.`);
    }
    renderSpecialNotesUi();
    showToast("기록 규칙 저장됨");
  } catch (err) {
    if (err.conflict) {
      showToast("충돌 — 규칙을 다시 불러옵니다.");
      const { data, revision } = await readJsonFile("defaults.json");
      defaultsDoc = normalizeDefaults(data);
      defaultsRevision = revision;
      openRulesModal();
      return;
    }
    showToast(err.message || "저장 실패");
  }
}

/* ---- 방송실 특이사항 ---- */
function createEmptySpecialNote() {
  return {
    id: crypto.randomUUID(),
    text: "",
    updatedAt: new Date().toISOString(),
    updatedBy: profile?.id || "",
  };
}

function syncSpecialNoteFromUi() {
  const ta = $("specialNoteText");
  if (!ta || !specialNotes.length) return;
  if (specialNoteIndex < 0 || specialNoteIndex >= specialNotes.length) return;
  const next = ta.value;
  const cur = specialNotes[specialNoteIndex];
  if ((cur.text || "") === next) return;
  cur.text = next;
  cur.updatedAt = new Date().toISOString();
  cur.updatedBy = profile?.id || cur.updatedBy || "";
  specialNotesDirty = true;
}

function renderSpecialNotesUi() {
  const ta = $("specialNoteText");
  const idxEl = $("specialNoteIndex");
  if (!ta || !idxEl) return;
  if (!specialNotes.length) {
    specialNotes = [createEmptySpecialNote()];
    specialNoteIndex = 0;
    specialNotesDirty = true;
  }
  if (specialNoteIndex < 0) specialNoteIndex = 0;
  if (specialNoteIndex >= specialNotes.length) specialNoteIndex = specialNotes.length - 1;
  ta.value = specialNotes[specialNoteIndex]?.text || "";
  idxEl.textContent = `${specialNoteIndex + 1} / ${specialNotes.length}`;
  const atFirst = specialNoteIndex <= 0;
  const atLast = specialNoteIndex >= specialNotes.length - 1;
  if ($("btnSpecialNotePrev")) $("btnSpecialNotePrev").disabled = atFirst;
  if ($("btnSpecialNoteNext")) $("btnSpecialNoteNext").disabled = atLast;
}

async function loadSpecialNotes() {
  try {
    const { data, revision } = await readJsonFile(SPECIAL_NOTES_PATH);
    specialNotesRevision = revision || "0";
    const notes = Array.isArray(data?.notes) ? data.notes : [];
    specialNotes = notes
      .filter((n) => n && typeof n === "object")
      .map((n) => ({
        id: n.id || crypto.randomUUID(),
        text: String(n.text || ""),
        updatedAt: n.updatedAt || "",
        updatedBy: n.updatedBy || "",
      }));
    specialNotesDirty = false;
    if (!specialNotes.length) {
      specialNotes = [createEmptySpecialNote()];
      specialNoteIndex = 0;
    } else {
      specialNoteIndex = specialNotes.length - 1;
    }
  } catch {
    specialNotes = [createEmptySpecialNote()];
    specialNotesRevision = "0";
    specialNoteIndex = 0;
    specialNotesDirty = false;
  }
  renderSpecialNotesUi();
}

async function saveSpecialNotes({ quiet = false } = {}) {
  syncSpecialNoteFromUi();
  // 빈 메모만 여러 개면 하나만 남김
  const cleaned = specialNotes.filter((n, i, arr) => (n.text || "").trim() || arr.length === 1);
  specialNotes = cleaned.length ? cleaned : [createEmptySpecialNote()];
  if (specialNoteIndex >= specialNotes.length) specialNoteIndex = specialNotes.length - 1;
  const payload = {
    version: 1,
    notes: specialNotes.map((n) => ({
      id: n.id,
      text: n.text || "",
      updatedAt: n.updatedAt || new Date().toISOString(),
      updatedBy: n.updatedBy || profile?.id || "",
    })),
  };
  try {
    const result = await writeJsonFile(SPECIAL_NOTES_PATH, payload, specialNotesRevision);
    specialNotesRevision = result?.revision || specialNotesRevision;
    specialNotesDirty = false;
    renderSpecialNotesUi();
    if (!quiet) showToast("방송실 특이사항을 저장했습니다.");
    return true;
  } catch (err) {
    if (err.conflict) {
      showToast("특이사항이 다른 기기에서 변경됨. 다시 불러옵니다.");
      await loadSpecialNotes();
      return false;
    }
    if (!quiet) showToast(err.message || "특이사항 저장 실패");
    return false;
  }
}

function specialNoteNew() {
  syncSpecialNoteFromUi();
  if (specialNotes.length >= specialNotesMax()) {
    showToast(`특이사항은 최대 ${specialNotesMax()}개까지입니다. (기록 규칙 → 일반에서 변경)`);
    return;
  }
  specialNotes.push(createEmptySpecialNote());
  specialNoteIndex = specialNotes.length - 1;
  specialNotesDirty = true;
  renderSpecialNotesUi();
  $("specialNoteText")?.focus();
}

function specialNoteDelete() {
  syncSpecialNoteFromUi();
  if (specialNotes.length <= 1) {
    specialNotes[0].text = "";
    specialNotes[0].updatedAt = new Date().toISOString();
    specialNotes[0].updatedBy = profile?.id || "";
    specialNotesDirty = true;
    renderSpecialNotesUi();
    showToast("메모 내용을 비웠습니다.");
    return;
  }
  if (!confirm("현재 특이사항 메모를 삭제할까요?")) return;
  specialNotes.splice(specialNoteIndex, 1);
  if (specialNoteIndex >= specialNotes.length) specialNoteIndex = specialNotes.length - 1;
  specialNotesDirty = true;
  renderSpecialNotesUi();
}

function specialNotePrev() {
  syncSpecialNoteFromUi();
  if (specialNoteIndex <= 0) return;
  specialNoteIndex -= 1;
  renderSpecialNotesUi();
}

function specialNoteNext() {
  syncSpecialNoteFromUi();
  if (specialNoteIndex >= specialNotes.length - 1) return;
  specialNoteIndex += 1;
  renderSpecialNotesUi();
}

/** 인쇄용: 가장 최근(갱신 시각) 비어 있지 않은 특이사항 */
function latestSpecialNoteText() {
  syncSpecialNoteFromUi();
  const ranked = [...specialNotes]
    .filter((n) => (n.text || "").trim())
    .sort((a, b) => String(b.updatedAt || "").localeCompare(String(a.updatedAt || "")));
  if (ranked.length) return ranked[0].text.trim();
  const cur = specialNotes[specialNoteIndex]?.text?.trim();
  return cur || "";
}

/* ---- Log (audit) modal ---- */
async function openAuditModal() {
  $("modalAudit").classList.remove("hidden");
  $("auditMonthLabel").textContent = monthKey();
  await loadAudit(monthKey());
}

async function loadAudit(month) {
  $("auditMonthLabel").textContent = month;
  const res = await api(`/api/audit?month=${encodeURIComponent(month)}`);
  const list = $("auditList");
  list.innerHTML = "";
  if (!res.ok) {
    list.innerHTML = "<li>로그를 불러오지 못했습니다.</li>";
    return;
  }
  const text = await res.text();
  const lines = text.split(/\r?\n/).filter(Boolean);
  if (!lines.length) {
    list.innerHTML = "<li>기록 없음</li>";
    return;
  }
  for (const line of lines.reverse()) {
    try {
      const row = JSON.parse(line);
      const li = document.createElement("li");
      const actorName = profileName(row.actor);
      li.innerHTML = `<div class="when">${escapeHtml(row.ts || "")}</div>
        <div><strong>${escapeHtml(actorName)}</strong> · ${escapeHtml(row.action || "")}
        ${row.date ? ` · ${escapeHtml(row.date)}` : ""}${row.ip ? ` · IP ${escapeHtml(row.ip)}` : ""}</div>
        <div>${escapeHtml(row.summary || "")}</div>`;
      list.appendChild(li);
    } catch {
      // skip
    }
  }
}

function shiftAuditMonth(delta) {
  const label = $("auditMonthLabel").textContent || monthKey();
  const [y, m] = label.split("-").map(Number);
  const d = new Date(y, m - 1 + delta, 1);
  const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
  loadAudit(key);
}

/* ---- Print ---- */
const PRINT_CARD_PREFS_KEY = "worklog.printCard.v1";
const PRINT_MIN_SLOTS = 1;
const PRINT_MAX_SLOTS = 7;
const PRINT_DEFAULT_SLOTS = 4;
const PRINT_CARD_STYLES = new Set(["bullet", "number", "plain", "line", "card"]);
let printCardStyle = "bullet";

function normalizePrintCardStyle(value) {
  const style = String(value || "").trim().toLowerCase();
  if (style === "line") return "plain";
  return PRINT_CARD_STYLES.has(style) && style !== "line" ? style : "bullet";
}

function clampPrintSlotCount(value) {
  const n = Number(value);
  if (!Number.isFinite(n)) return PRINT_DEFAULT_SLOTS;
  return Math.max(PRINT_MIN_SLOTS, Math.min(PRINT_MAX_SLOTS, Math.round(n)));
}

function loadPrintCardPrefs() {
  try {
    return JSON.parse(localStorage.getItem(PRINT_CARD_PREFS_KEY) || "{}");
  } catch {
    return {};
  }
}

function savePrintCardPrefs(patch) {
  const next = { ...loadPrintCardPrefs(), ...patch };
  if (next.style) next.style = normalizePrintCardStyle(next.style);
  if (next.slotCount != null) next.slotCount = clampPrintSlotCount(next.slotCount);
  localStorage.setItem(PRINT_CARD_PREFS_KEY, JSON.stringify(next));
  return next;
}

function applyPrintCardPrefsToUi() {
  const prefs = loadPrintCardPrefs();
  const style = normalizePrintCardStyle(printCardStyle || prefs.style || "bullet");
  printCardStyle = style;
  const sel = $("printCardStyle");
  if (sel) sel.value = style;
  printSlotCount = clampPrintSlotCount(printSlotCount || prefs.slotCount || PRINT_DEFAULT_SLOTS);
  updatePrintSlotCountUi();
}

function currentPrintCardStyle() {
  return normalizePrintCardStyle(
    $("printCardStyle")?.value || printCardStyle || loadPrintCardPrefs().style || "bullet",
  );
}

function emptyPrintSlot() {
  return { type: "date", date: "" };
}

function ensurePrintSlotsLength() {
  printSlotCount = clampPrintSlotCount(printSlotCount);
  while (printSlots.length < printSlotCount) printSlots.push(emptyPrintSlot());
  if (printSlots.length > printSlotCount) printSlots = printSlots.slice(0, printSlotCount);
}

function setPrintSlotCount(nextCount, { persist = true } = {}) {
  const next = clampPrintSlotCount(nextCount);
  if (next === printSlotCount && printSlots.length === next) {
    updatePrintSlotCountUi();
    return;
  }
  // 칸을 줄일 때 마지막(날짜/특이사항) 칸의 특이사항 선택을 새 마지막에 유지
  const prevLast = printSlots[printSlots.length - 1];
  printSlotCount = next;
  ensurePrintSlotsLength();
  if (prevLast?.type === "notes" && printSlots.length) {
    printSlots[printSlots.length - 1] = { type: "notes" };
  }
  if (persist) savePrintCardPrefs({ slotCount: printSlotCount });
  updatePrintSlotCountUi();
  renderPrintSlots();
  refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
}

function updatePrintSlotCountUi() {
  const label = $("printSlotCountLabel");
  if (label) label.textContent = `${printSlotCount}칸`;
  const dec = $("btnPrintSlotDec");
  const inc = $("btnPrintSlotInc");
  if (dec) dec.disabled = printSlotCount <= PRINT_MIN_SLOTS;
  if (inc) inc.disabled = printSlotCount >= PRINT_MAX_SLOTS;
}

function activePrintSlots() {
  return printSlots.filter((s) => {
    if (!s) return false;
    if (s.type === "notes") return true;
    return Boolean(s.date);
  });
}

function printSlotSummary(slot) {
  if (!slot) return "비움";
  if (slot.type === "notes") return "특이사항";
  return slot.date || "날짜 선택";
}

function renderPrintSlots() {
  ensurePrintSlotsLength();
  const wrap = $("printSlots");
  if (!wrap) return;
  wrap.innerHTML = "";
  wrap.style.setProperty("--print-slot-count", String(printSlotCount));
  wrap.dataset.count = String(printSlotCount);
  for (let i = 0; i < printSlotCount; i++) {
    const slot = printSlots[i] || emptyPrintSlot();
    const cell = document.createElement("div");
    cell.className = `print-slot${slot.type === "notes" ? " is-notes" : ""}${slot.date || slot.type === "notes" ? " is-set" : ""}`;
    cell.dataset.index = String(i);

    const label = document.createElement("div");
    label.className = "print-slot-num";
    label.textContent = `${i + 1}`;

    const summary = document.createElement("div");
    summary.className = "print-slot-summary";
    summary.textContent = printSlotSummary(slot);

    const actions = document.createElement("div");
    actions.className = "print-slot-actions";

    const dateInput = document.createElement("input");
    dateInput.type = "date";
    dateInput.className = "print-slot-date-input";
    dateInput.setAttribute("aria-label", `${i + 1}번 칸 날짜`);
    if (slot.type === "date" && slot.date) dateInput.value = slot.date;
    dateInput.addEventListener("change", () => {
      const d = (dateInput.value || "").slice(0, 10);
      if (!/^\d{4}-\d{2}-\d{2}$/.test(d)) {
        showToast("날짜를 선택하세요.");
        return;
      }
      printSlots[i] = { type: "date", date: d };
      renderPrintSlots();
      refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
    });
    // 마지막 칸만 날짜 / 특이사항 선택
    if (i === printSlotCount - 1) {
      const notesOn = slot.type === "notes";
      actions.classList.add("print-slot-actions--split");
      const dateWrap = document.createElement("div");
      dateWrap.className = "print-slot-date-wrap";
      const dateBtn = document.createElement("button");
      dateBtn.type = "button";
      dateBtn.className = `btn ghost print-slot-date-btn${notesOn ? "" : " is-on"}`;
      dateBtn.setAttribute("aria-pressed", notesOn ? "false" : "true");
      dateBtn.textContent = "날짜";
      dateInput.classList.add("print-slot-date-input--sr");
      const notesBtn = document.createElement("button");
      notesBtn.type = "button";
      notesBtn.className = `btn ghost print-slot-notes-btn${notesOn ? " is-on" : ""}`;
      notesBtn.setAttribute("aria-pressed", notesOn ? "true" : "false");
      notesBtn.textContent = "특이사항";
      dateBtn.addEventListener("click", () => {
        if (printSlots[i]?.type === "notes") {
          printSlots[i] = { type: "date", date: dateInput.value || "" };
          dateBtn.classList.add("is-on");
          dateBtn.setAttribute("aria-pressed", "true");
          notesBtn.classList.remove("is-on");
          notesBtn.setAttribute("aria-pressed", "false");
          cell.classList.remove("is-notes");
          if (!printSlots[i].date) cell.classList.remove("is-set");
          summary.textContent = printSlotSummary(printSlots[i]);
          refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
        }
        try {
          if (typeof dateInput.showPicker === "function") dateInput.showPicker();
          else dateInput.click();
        } catch {
          dateInput.focus();
          dateInput.click();
        }
      });
      notesBtn.addEventListener("click", () => {
        printSlots[i] = { type: "notes" };
        renderPrintSlots();
        refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
      });
      dateWrap.appendChild(dateBtn);
      dateWrap.appendChild(dateInput);
      actions.appendChild(dateWrap);
      actions.appendChild(notesBtn);
    } else {
      actions.appendChild(dateInput);
    }

    const clearBtn = document.createElement("button");
    clearBtn.type = "button";
    clearBtn.className = "btn icon print-slot-clear";
    clearBtn.setAttribute("aria-label", `${i + 1}번 칸 비우기`);
    clearBtn.textContent = "×";
    clearBtn.addEventListener("click", () => {
      printSlots[i] = emptyPrintSlot();
      renderPrintSlots();
      refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
    });

    cell.appendChild(label);
    cell.appendChild(summary);
    cell.appendChild(actions);
    cell.appendChild(clearBtn);
    wrap.appendChild(cell);
  }
  updatePrintSlotCountUi();
}

/** 선택일 기준 과거로 칸 채움 (내용 있는 날 우선). 마지막 칸은 비워 날짜/특이사항 선택용 */
async function buildDefaultPrintSlots(primary) {
  const prefs = loadPrintCardPrefs();
  printSlotCount = clampPrintSlotCount(prefs.slotCount || printSlotCount || PRINT_DEFAULT_SLOTS);
  const fillCount = Math.max(0, printSlotCount - 1);
  const slots = [];
  const used = new Set();
  const pushDate = (d) => {
    if (!d || used.has(d) || slots.length >= fillCount) return;
    used.add(d);
    slots.push({ type: "date", date: d });
  };

  pushDate(primary);

  const anchor = new Date(primary + "T12:00:00");
  const withContent = [];
  const without = [];
  for (let i = 1; i <= 60 && withContent.length + without.length < 20; i++) {
    const prev = new Date(anchor);
    prev.setDate(anchor.getDate() - i);
    const key = ymd(prev);
    let entry = entryForDate(key);
    if (!entry && !key.startsWith(monthKey())) {
      entry = await ensureEntryLoaded(key);
    }
    if (entry && entryHasContent(entry)) withContent.push(key);
    else without.push(key);
  }
  for (const d of withContent) pushDate(d);
  for (const d of without) pushDate(d);

  while (slots.length < fillCount) slots.push(emptyPrintSlot());
  // 마지막 칸: 날짜/특이사항 선택 카드 (비움)
  slots.push(emptyPrintSlot());
  return slots;
}

function sundayOfWeek(dateStr) {
  const raw = dateStr || todayYmd();
  const d = new Date(`${raw}T12:00:00`);
  const base = Number.isNaN(d.getTime()) ? new Date(`${todayYmd()}T12:00:00`) : d;
  base.setDate(base.getDate() - base.getDay());
  return base;
}

function buildWeekPrintSlots(anchorDate) {
  const start = sundayOfWeek(anchorDate);
  const slots = [];
  for (let i = 0; i < 7; i++) {
    const d = new Date(start);
    d.setDate(start.getDate() + i);
    slots.push({ type: "date", date: ymd(d) });
  }
  slots.push({ type: "notes" });
  return slots;
}

function formatPrintWeekRange(anchorDate) {
  const start = sundayOfWeek(anchorDate);
  const end = new Date(start);
  end.setDate(start.getDate() + 6);
  return `${formatDateHeading(ymd(start))} ~ ${formatDateHeading(ymd(end))}`;
}

function currentWeekSunday() {
  return sundayOfWeek(todayYmd());
}

function isPrintWeekAtCurrent(anchorDate) {
  return sundayOfWeek(anchorDate).getTime() >= currentWeekSunday().getTime();
}

function clampPrintWeekAnchor(dateStr) {
  const start = sundayOfWeek(dateStr || todayYmd());
  const maxStart = currentWeekSunday();
  if (start.getTime() > maxStart.getTime()) return ymd(maxStart);
  return dateStr || ymd(start);
}

function shiftPrintWeek(delta) {
  const start = sundayOfWeek(printWeekAnchor || selectedDate);
  const next = new Date(start);
  next.setDate(start.getDate() + delta * 7);
  const maxStart = currentWeekSunday();
  if (next.getTime() > maxStart.getTime()) return;
  printWeekAnchor = ymd(next);
  applyPrintModeUi();
  refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
}

function splitWeekPrintPages(slots) {
  const days = slots.filter((s) => s.type === "date");
  const notes = slots.find((s) => s.type === "notes");
  const split = Math.min(Math.max(printWeekSplitAfter, 0), 6);
  const page1 = days.slice(0, split + 1);
  const page2 = days.slice(split + 1);
  if (printWeekIncludeNotes && notes) page2.push(notes);
  return { page1, page2 };
}

function renderPrintPageSplitUi() {
  const host = $("printPageSplit");
  if (!host) return;
  const split = Math.min(Math.max(printWeekSplitAfter, 0), 6);
  const labels = [...WEEKDAYS_KO, "+α"];
  host.innerHTML = "";
  labels.forEach((label, i) => {
    if (i > 0) {
      const gap = document.createElement("span");
      gap.className = `print-page-split-gap${i === split + 1 ? " is-split" : ""}`;
      gap.setAttribute("aria-hidden", "true");
      gap.textContent = i === split + 1 ? "|" : "";
      host.appendChild(gap);
    }
    const btn = document.createElement("button");
    btn.type = "button";
    if (i < 7) {
      const page = i <= split ? 1 : 2;
      btn.className = `print-page-split-btn is-page${page}`;
      btn.textContent = label;
      btn.setAttribute("aria-pressed", String(i === split));
      btn.title = `${label}요일까지 1페이지`;
      btn.addEventListener("click", () => {
        printWeekSplitAfter = i;
        renderPrintPageSplitUi();
        refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
      });
    } else {
      btn.className = `print-page-split-btn print-page-split-btn--notes${printWeekIncludeNotes ? " is-on" : " is-off"}`;
      btn.textContent = label;
      btn.setAttribute("aria-pressed", String(printWeekIncludeNotes));
      btn.title = printWeekIncludeNotes ? "추가사항 인쇄 포함" : "추가사항 인쇄 제외";
      btn.addEventListener("click", () => {
        printWeekIncludeNotes = !printWeekIncludeNotes;
        renderPrintPageSplitUi();
        refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
      });
    }
    host.appendChild(btn);
  });
}

function applyPrintModeUi() {
  const isWeek = printMode === "week";
  $("printSlotsField")?.classList.toggle("hidden", isWeek);
  $("printWeekTools")?.classList.toggle("hidden", !isWeek);
  const weekMeta = $("printWeekMeta");
  if (weekMeta && isWeek) {
    weekMeta.textContent = formatPrintWeekRange(printWeekAnchor || selectedDate);
  }
  const atCurrent = isPrintWeekAtCurrent(printWeekAnchor || selectedDate);
  const btnPrev = $("btnPrintWeekPrev");
  const btnNext = $("btnPrintWeekNext");
  if (btnPrev) btnPrev.disabled = false;
  if (btnNext) btnNext.disabled = atCurrent;
  const btnSlots = $("btnPrintModeSlots");
  const btnWeek = $("btnPrintModeWeek");
  btnSlots?.classList.toggle("is-on", !isWeek);
  btnWeek?.classList.toggle("is-on", isWeek);
  btnSlots?.setAttribute("aria-pressed", String(!isWeek));
  btnWeek?.setAttribute("aria-pressed", String(isWeek));
  if (isWeek) renderPrintPageSplitUi();
}

function setPrintMode(mode) {
  const next = mode === "week" ? "week" : "slots";
  if (printMode === next) return;
  printMode = next;
  if (next === "week") printWeekAnchor = clampPrintWeekAnchor(printWeekAnchor || selectedDate);
  applyPrintModeUi();
  refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
}

async function openPrintPage() {
  flushEditorToDoc();
  syncSpecialNoteFromUi();
  if (specialNotesDirty) await saveSpecialNotes({ quiet: true });
  if (!selectedDate) {
    showToast("인쇄할 날짜를 먼저 선택하세요.");
    return;
  }
  printMode = "slots";
  printWeekAnchor = clampPrintWeekAnchor(selectedDate);
  printWeekSplitAfter = 3;
  printWeekIncludeNotes = true;
  printSlots = await buildDefaultPrintSlots(selectedDate);
  applyPrintCardPrefsToUi();
  applyPrintModeUi();
  renderPrintSlots();
  showScreen("print");
  await loadPrintSigns();
  await loadSpecialNotes().catch(() => {});
  await refreshPrintPreview();
}

async function ensureEntryLoaded(dateStr) {
  let entry = entryForDate(dateStr);
  if (entry) {
    normalizeEntryLogs(entry);
    return entry;
  }
  if (!dateStr.startsWith(monthKey())) {
    const mk = dateStr.slice(0, 7);
    try {
      const { data } = await readJsonFile(`work-logs/${mk}.json`);
      if (data?.entries) {
        const found = data.entries.find((e) => e.date === dateStr) || null;
        if (found) normalizeEntryLogs(found);
        return found;
      }
    } catch {
      return null;
    }
  }
  return null;
}

function clampPrintMinLines(value, fallback = PRINT_EMPTY_LINES_DEFAULT) {
  const n = Number.parseInt(value, 10);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(PRINT_EMPTY_LINES_MAX, Math.max(PRINT_EMPTY_LINES_MIN, n));
}

function entryLogIsEmpty(entry) {
  if (!entry) return true;
  normalizeEntryLogs(entry);
  return !String(entry.logA || "").trim() && !String(entry.logB || "").trim();
}

function formatPrintLines(text, style, { minLines = 4 } = {}) {
  const raw = String(text || "").replace(/\r\n/g, "\n");
  let lines = raw.trim() ? raw.split("\n") : [];
  while (lines.length < minLines) lines.push("");
  let contentIndex = 0;
  return lines
    .map((line) => {
      const hasContent = line.trim().length > 0;
      const t = escapeHtml(line) || "&nbsp;";
      if (!hasContent) {
        return `<div class="print-log-line print-log-line--empty"><span class="print-log-text">${t}</span></div>`;
      }
      if (style === "number") {
        contentIndex += 1;
        return `<div class="print-log-line"><span class="print-log-num">${contentIndex}.</span><span class="print-log-text">${t}</span></div>`;
      }
      if (style === "plain" || style === "card") {
        return `<div class="print-log-line"><span class="print-log-text">${t}</span></div>`;
      }
      return `<div class="print-log-line"><span class="print-log-bullet" aria-hidden="true"></span><span class="print-log-text">${t}</span></div>`;
    })
    .join("");
}

function fillJournalPrintCard(sectionEl, dateStr, entry, style) {
  const e = entry || createEmptyEntry(dateStr || todayYmd());
  normalizeEntryLogs(e);
  const empty = entryLogIsEmpty(e);
  const minLines = empty ? printEmptyJournalLines : 4;
  sectionEl.style.setProperty("--print-min-lines", String(minLines));
  sectionEl.classList.add("print-card", "print-card--journal");
  sectionEl.classList.toggle("print-style-plain", style === "plain");
  sectionEl.classList.toggle("print-style-card", style === "card");
  const head = sectionEl.querySelector(".print-card-head");
  if (head) head.textContent = dateStr ? formatDateHeading(dateStr) : "";
  const colA = sectionEl.querySelector(".print-col-a");
  const colB = sectionEl.querySelector(".print-col-b");
  if (colA) colA.innerHTML = formatPrintLines(e.logA || "", style, { minLines });
  if (colB) colB.innerHTML = formatPrintLines(e.logB || "", style, { minLines });
}

function fillNotesPrintCard(sectionEl, text) {
  const empty = !String(text || "").trim();
  const minLines = empty ? printEmptyNotesLines : 4;
  sectionEl.style.setProperty("--print-min-lines", String(minLines));
  sectionEl.classList.add("print-card", "print-card--notes");
  const head = sectionEl.querySelector(".print-card-head");
  if (head) head.textContent = printNotesCardTitle || DEFAULT_NOTES_CARD_TITLE;
  const body = sectionEl.querySelector(".print-notes-body");
  if (!body) return;
  body.innerHTML = formatPrintLines(text || "", "bullet", { minLines });
}

function journalCardHtml() {
  return `<section class="print-card print-card--journal">
    <div class="print-card-head"></div>
    <div class="print-card-body print-log-body">
      <div class="print-log-col print-col-a"></div>
      <div class="print-log-col print-col-b"></div>
    </div>
  </section>`;
}

function notesCardHtml() {
  return `<section class="print-card print-card--notes">
    <div class="print-card-head"></div>
    <div class="print-card-body print-notes-body"></div>
  </section>`;
}

function setPrintFitWarn(message) {
  const el = $("printFitWarn");
  if (!el) return;
  if (!message) {
    el.textContent = "";
    el.classList.add("hidden");
    return;
  }
  el.textContent = message;
  el.classList.remove("hidden");
}

function waitPrintLayout() {
  return new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
}

function printSheetMarkup() {
  const signs = DEFAULT_PRINT_SIGNS.map((_, i) => {
    const name = printSigns[i] || DEFAULT_PRINT_SIGNS[i] || "";
    return `<div class="sign-box"><div class="sign-name" data-sign-index="${i}">${escapeHtml(name)}</div><div class="sign-space"></div></div>`;
  }).join("");
  return `<div class="print-sheet">
    <header class="print-sheet-head">
      <div class="print-signs">${signs}</div>
      <h1>방송실 업무일지</h1>
    </header>
    <div class="print-cards print-cards--n1"></div>
  </div>`;
}

function ensurePrintSheets(count) {
  const host = $("printPreviewWrap");
  if (!host) return [];
  const n = Math.max(1, count);
  host.innerHTML = Array.from({ length: n }, () => printSheetMarkup()).join("");
  return [...host.querySelectorAll(".print-sheet")];
}

/** 카드 내용이 시트에 넘치면 false */
function printCardsFitInSheet(sheet) {
  const el = sheet || $("printPreviewWrap")?.querySelector(".print-sheet");
  const wrap = el?.querySelector(".print-cards");
  if (!el || !wrap) return true;
  const cards = [...wrap.querySelectorAll(".print-card")];
  if (!cards.length) return true;
  const sheetStyle = getComputedStyle(el);
  const padY =
    (parseFloat(sheetStyle.paddingTop) || 0) + (parseFloat(sheetStyle.paddingBottom) || 0);
  const head = el.querySelector(".print-sheet-head");
  const headH = head ? head.getBoundingClientRect().height : 0;
  const gap = parseFloat(getComputedStyle(wrap).gap) || 0;
  const available = el.clientHeight - padY - headH - gap * Math.max(0, cards.length - 1) - 2;
  const used = cards.reduce((sum, c) => sum + c.getBoundingClientRect().height, 0);
  return used <= available + 4;
}

async function renderPrintCardsForSlots(slots, style, wrap) {
  const host = wrap || $("printPreviewWrap")?.querySelector(".print-cards");
  if (!host) return;
  if (!slots.length) {
    host.className = "print-cards print-cards--n0";
    host.innerHTML = "";
    return;
  }
  const n = slots.length;
  host.className = `print-cards print-cards--n${n}`;
  host.innerHTML = slots
    .map((s) => (s.type === "notes" ? notesCardHtml() : journalCardHtml()))
    .join("");

  const sections = [...host.querySelectorAll(".print-card")];
  for (let i = 0; i < slots.length; i++) {
    const slot = slots[i];
    if (slot.type === "notes") {
      fillNotesPrintCard(sections[i], latestSpecialNoteText());
    } else {
      const entry = slot.date ? await ensureEntryLoaded(slot.date) : null;
      fillJournalPrintCard(sections[i], slot.date, entry, style);
    }
  }
}

const PRINT_SIGNS_PATH = "print-signs.json";
const DEFAULT_PRINT_SIGNS = ["김종희", "임원빈", "방송실장", "총무", "봉사회장"];
const DEFAULT_NOTES_CARD_TITLE = "기타 특이사항 (장소별 방송장비, 차량, 기타 등등)";
let printSigns = [...DEFAULT_PRINT_SIGNS];
let printNotesCardTitle = DEFAULT_NOTES_CARD_TITLE;
let printSignsRevision = "0";

function normalizePrintSigns(raw) {
  const list = Array.isArray(raw?.names)
    ? raw.names
    : Array.isArray(raw)
      ? raw
      : [];
  const next = DEFAULT_PRINT_SIGNS.map((fallback, i) => {
    const value = String(list[i] ?? "").trim();
    return value || fallback;
  });
  return next;
}

function normalizeNotesCardTitle(raw) {
  const title = String(raw?.notesCardTitle ?? raw?.notesTitle ?? "").trim();
  return title || DEFAULT_NOTES_CARD_TITLE;
}

function printSignsPayload(overrides = {}) {
  return {
    version: 4,
    names: printSigns,
    notesCardTitle: printNotesCardTitle,
    printCardStyle,
    emptyJournalMinLines: printEmptyJournalLines,
    emptyNotesMinLines: printEmptyNotesLines,
    ...overrides,
  };
}

function applyPrintEmptyLinesToUi() {
  const journal = $("printEmptyJournalLines");
  const notes = $("printEmptyNotesLines");
  if (journal) journal.value = String(printEmptyJournalLines);
  if (notes) notes.value = String(printEmptyNotesLines);
}

function applyPrintSignsToSheet() {
  document.querySelectorAll(".print-signs .sign-name").forEach((el) => {
    const i = Number(el.dataset.signIndex);
    el.textContent = printSigns[i] || DEFAULT_PRINT_SIGNS[i] || "";
  });
}

function renderPrintSignFields() {
  const wrap = $("printSignFields");
  if (!wrap) return;
  wrap.innerHTML = printSigns
    .map(
      (name, i) => `<div class="field">
        <label for="printSignInput${i}">${i + 1}</label>
        <input id="printSignInput${i}" type="text" value="${escapeHtml(name)}" maxlength="20" autocomplete="off" aria-label="서명란 ${i + 1}" />
      </div>`,
    )
    .join("");
  const titleInput = $("printNotesCardTitle");
  if (titleInput) titleInput.value = printNotesCardTitle || DEFAULT_NOTES_CARD_TITLE;
  applyPrintCardPrefsToUi();
  applyPrintEmptyLinesToUi();
}

async function loadPrintSigns() {
  try {
    const { data, revision } = await readJsonFile(PRINT_SIGNS_PATH);
    printSignsRevision = revision || "0";
    if (data) {
      printSigns = normalizePrintSigns(data);
      printNotesCardTitle = normalizeNotesCardTitle(data);
      printEmptyJournalLines = clampPrintMinLines(
        data.emptyJournalMinLines ?? data.emptyJournalLines,
        PRINT_EMPTY_LINES_DEFAULT,
      );
      printEmptyNotesLines = clampPrintMinLines(
        data.emptyNotesMinLines ?? data.emptyNotesLines,
        PRINT_EMPTY_LINES_DEFAULT,
      );
      const fromNas = data.printCardStyle || data.cardStyle;
      if (fromNas) {
        printCardStyle = normalizePrintCardStyle(fromNas);
        savePrintCardPrefs({ style: printCardStyle });
      } else {
        // NAS에 없으면 로컬 값을 올려 공유
        const localStyle = normalizePrintCardStyle(loadPrintCardPrefs().style || "bullet");
        printCardStyle = localStyle;
        try {
          const result = await writeJsonFile(
            PRINT_SIGNS_PATH,
            printSignsPayload({ printCardStyle: localStyle }),
            printSignsRevision,
          );
          printSignsRevision = result?.revision || printSignsRevision;
        } catch (migrateErr) {
          if (migrateErr.conflict) {
            printSignsRevision = migrateErr.revision || printSignsRevision;
            try {
              const again = await readJsonFile(PRINT_SIGNS_PATH);
              printSignsRevision = again.revision || printSignsRevision;
              const nasStyle = again.data?.printCardStyle || again.data?.cardStyle;
              if (nasStyle) {
                printCardStyle = normalizePrintCardStyle(nasStyle);
                savePrintCardPrefs({ style: printCardStyle });
              }
            } catch {
              /* keep local */
            }
          } else {
            showToast(`인쇄 양식 NAS 동기화 실패: ${migrateErr.message || migrateErr}`);
          }
        }
      }
    } else {
      printSigns = [...DEFAULT_PRINT_SIGNS];
      printNotesCardTitle = DEFAULT_NOTES_CARD_TITLE;
      printCardStyle = normalizePrintCardStyle(loadPrintCardPrefs().style || "bullet");
      printEmptyJournalLines = PRINT_EMPTY_LINES_DEFAULT;
      printEmptyNotesLines = PRINT_EMPTY_LINES_DEFAULT;
    }
  } catch {
    printSigns = [...DEFAULT_PRINT_SIGNS];
    printNotesCardTitle = DEFAULT_NOTES_CARD_TITLE;
    printCardStyle = normalizePrintCardStyle(loadPrintCardPrefs().style || "bullet");
    printEmptyJournalLines = PRINT_EMPTY_LINES_DEFAULT;
    printEmptyNotesLines = PRINT_EMPTY_LINES_DEFAULT;
    printSignsRevision = "0";
  }
  applyPrintSignsToSheet();
  applyPrintCardPrefsToUi();
  applyPrintEmptyLinesToUi();
}

async function persistPrintCardStyleToNas(style, { quiet = true } = {}) {
  const nextStyle = normalizePrintCardStyle(style);
  printCardStyle = nextStyle;
  savePrintCardPrefs({ style: nextStyle });
  try {
    const result = await writeJsonFile(
      PRINT_SIGNS_PATH,
      printSignsPayload({ printCardStyle: nextStyle }),
      printSignsRevision,
    );
    printSignsRevision = result?.revision || printSignsRevision;
    if (!quiet) showToast("인쇄 양식을 NAS에 저장했습니다.");
  } catch (err) {
    if (err.conflict) {
      printSignsRevision = err.revision || "0";
      await loadPrintSigns();
      await refreshPrintPreview().catch(() => {});
      if (!quiet) showToast("다른 기기에서 인쇄 설정이 변경됨. 다시 선택해 주세요.");
      return;
    }
    if (!quiet) showToast(`인쇄 양식 NAS 저장 실패: ${err.message}`);
  }
}

async function savePrintSignsFromModal() {
  const names = DEFAULT_PRINT_SIGNS.map((_, i) => {
    const input = $(`printSignInput${i}`);
    const value = String(input?.value || "").trim();
    return value || DEFAULT_PRINT_SIGNS[i];
  });
  const titleRaw = String($("printNotesCardTitle")?.value || "").trim();
  const notesTitle = titleRaw || DEFAULT_NOTES_CARD_TITLE;
  const style = currentPrintCardStyle();
  const emptyJournal = clampPrintMinLines($("printEmptyJournalLines")?.value, PRINT_EMPTY_LINES_DEFAULT);
  const emptyNotes = clampPrintMinLines($("printEmptyNotesLines")?.value, PRINT_EMPTY_LINES_DEFAULT);
  try {
    const result = await writeJsonFile(
      PRINT_SIGNS_PATH,
      printSignsPayload({
        names,
        notesCardTitle: notesTitle,
        printCardStyle: style,
        emptyJournalMinLines: emptyJournal,
        emptyNotesMinLines: emptyNotes,
      }),
      printSignsRevision,
    );
    printSigns = names;
    printNotesCardTitle = notesTitle;
    printCardStyle = style;
    printEmptyJournalLines = emptyJournal;
    printEmptyNotesLines = emptyNotes;
    savePrintCardPrefs({ style });
    printSignsRevision = result?.revision || printSignsRevision;
    applyPrintSignsToSheet();
    applyPrintCardPrefsToUi();
    applyPrintEmptyLinesToUi();
    $("modalPrintSigns").classList.add("hidden");
    showToast("인쇄 설정을 저장했습니다.");
    await refreshPrintPreview().catch(() => {});
  } catch (err) {
    if (err.conflict) {
      printSignsRevision = err.revision || "0";
      showToast("다른 기기에서 변경됨. 다시 열어 저장해 주세요.");
      return;
    }
    showToast(`인쇄 설정 저장 실패: ${err.message}`);
  }
}

async function openPrintSignSettings() {
  await loadPrintSigns();
  renderPrintSignFields();
  $("modalPrintSigns").classList.remove("hidden");
}

async function refreshPrintPreview() {
  applyPrintModeUi();
  const style = currentPrintCardStyle();
  printCardStyle = style;
  savePrintCardPrefs({ style });
  // printCardStyle NAS 저장은 양식 변경 시에만 (미리보기마다 PUT 하지 않음)

  if (printMode === "week") {
    const slots = buildWeekPrintSlots(printWeekAnchor || selectedDate);
    const { page1, page2 } = splitWeekPrintPages(slots);
    const sheetCount = page2.length ? 2 : 1;
    const sheets = ensurePrintSheets(sheetCount);
    await renderPrintCardsForSlots(page1, style, sheets[0]?.querySelector(".print-cards"));
    if (page2.length) {
      await renderPrintCardsForSlots(page2, style, sheets[1]?.querySelector(".print-cards"));
    }
    await waitPrintLayout();
    const overflow = sheets.some((sheet) => !printCardsFitInSheet(sheet));
    setPrintFitWarn(overflow ? "일주일 내용이 길어 일부만 보일 수 있습니다." : "");
    refreshPrintPreview._fitted = [...page1, ...page2];
    return;
  }

  ensurePrintSlotsLength();
  let slots = activePrintSlots();
  const sheets = ensurePrintSheets(1);
  const wrap = sheets[0]?.querySelector(".print-cards");
  if (!slots.length) {
    setPrintFitWarn("");
    if (wrap) {
      wrap.className = "print-cards print-cards--n1";
      wrap.innerHTML = journalCardHtml();
      fillJournalPrintCard(wrap.querySelector(".print-card"), "", null, style);
    }
    showToast("출력할 칸을 선택하세요.");
    refreshPrintPreview._fitted = [];
    return;
  }

  let fitSlots = [...slots];
  let dropped = 0;
  await renderPrintCardsForSlots(fitSlots, style, wrap);
  await waitPrintLayout();
  while (fitSlots.length > 1 && !printCardsFitInSheet(sheets[0])) {
    fitSlots = fitSlots.slice(0, -1);
    dropped += 1;
    await renderPrintCardsForSlots(fitSlots, style, wrap);
    await waitPrintLayout();
  }

  if (dropped > 0) {
    const msg = `내용 분량이 많아 한 페이지에 ${slots.length}칸을 모두 넣을 수 없어 ${fitSlots.length}칸만 인쇄합니다.`;
    setPrintFitWarn(msg);
    showToast(msg);
  } else if (!printCardsFitInSheet(sheets[0]) && fitSlots.length === 1) {
    setPrintFitWarn("선택한 1칸 내용도 길어서 일부만 보일 수 있습니다. 내용을 줄여 주세요.");
  } else {
    setPrintFitWarn("");
  }

  refreshPrintPreview._fitted = fitSlots;
}

async function markPrintedInOtherMonth(dateStr) {
  const mk = dateStr.slice(0, 7);
  const path = `work-logs/${mk}.json`;
  try {
    const { data, revision } = await readJsonFile(path);
    const entries = Array.isArray(data?.entries) ? [...data.entries] : [];
    let entry = entries.find((e) => e.date === dateStr);
    if (!entry) {
      entry = createEmptyEntry(dateStr);
      entries.push(entry);
    }
    entry.isPrinted = true;
    entry.updatedBy = profile?.id || entry.updatedBy;
    entry.updatedAt = new Date().toISOString();
    delete entry._draft;
    delete entry._ephemeral;
    await writeJsonFile(path, { version: 1, entries }, revision);
  } catch (err) {
    // 파일 없으면 새로 만듦
    try {
      const entry = createEmptyEntry(dateStr);
      entry.isPrinted = true;
      delete entry._draft;
      await writeJsonFile(path, { version: 1, entries: [entry] }, "");
    } catch (err2) {
      showToast(`인쇄됨 저장 실패 (${dateStr}): ${err2.message || err.message}`);
    }
  }
}

async function markPrintSuccessful(dateStrs) {
  const dates = [...new Set((dateStrs || []).filter(Boolean))];
  if (!dates.length) return;
  const currentMk = monthKey();
  let touchedCurrent = false;
  for (const dateStr of dates) {
    if (dateStr.startsWith(currentMk)) {
      let entry = entryForDate(dateStr);
      if (!entry) {
        entry = createEmptyEntry(dateStr);
        entry._draft = true;
        monthDoc.entries.push(entry);
      }
      entry.isPrinted = true;
      touchedCurrent = true;
      if (selectedDate === dateStr) {
        $("entryPrinted").checked = true;
        if (editorEntry?.date === dateStr) editorEntry.isPrinted = true;
      }
    } else {
      await markPrintedInOtherMonth(dateStr);
    }
  }
  if (touchedCurrent) {
    monthDirty = true;
    await saveMonth();
  } else {
    showToast("인쇄됨으로 표시했습니다.");
  }
}

function bindUi() {
  $("btnRetryConnect")?.addEventListener("click", () => {
    runConnectionFlow().catch((e) => showToast(e.message || String(e)));
  });
  $("btnBackLogin")?.addEventListener("click", () => {
    // 프로필 화면에서 연결 상태만 다시 확인 (NAS 로그인 폼으로 가지 않음)
    runConnectionFlow().catch((e) => showToast(e.message || String(e)));
  });
  $("btnDisconnect").addEventListener("click", async () => {
    if (monthDirty && !confirm("저장하지 않은 변경이 있습니다. 연결을 끊을까요?")) return;
    await disconnectAll();
  });
  $("btnSwitchProfile").addEventListener("click", () => showScreen("profile"));
  $("btnPrevMonth").addEventListener("click", () => shiftMonth(-1));
  $("btnNextMonth").addEventListener("click", () => shiftMonth(1));
  $("btnSaveMonth").addEventListener("click", () => saveMonth());
  $("btnBasicRecord").addEventListener("click", applyBasicScheduleRecord);
  $("btnGoToday").addEventListener("click", () => {
    goToToday().catch((e) => showToast(e.message || String(e)));
  });
  $("btnStartEdit").addEventListener("click", () => {
    if (editMode) {
      saveCurrentEntry().catch((e) => showToast(e.message || String(e)));
      return;
    }
    startEditMode().catch((e) => showToast(e.message || String(e)));
  });
  $("btnCancelEdit").addEventListener("click", () => {
    cancelEditMode().catch((e) => showToast(e.message || String(e)));
  });
  $("btnRules").addEventListener("click", openRulesModal);
  $("btnCloseRules").addEventListener("click", () => $("modalRules").classList.add("hidden"));
  $("btnClosePin").addEventListener("click", () => closePinModal(false));
  $("btnCancelPin").addEventListener("click", () => closePinModal(false));
  $("btnConfirmPin").addEventListener("click", () => {
    submitPinModal().catch((e) => {
      $("pinModalError").textContent = e.message || String(e);
    });
  });
  $("pinInput").addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      if (pinGatePending?.mode === "setup") $("pinInputConfirm").focus();
      else $("btnConfirmPin").click();
    }
  });
  $("pinInputConfirm").addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      $("btnConfirmPin").click();
    }
  });
  const stripNonDigits = (el) => {
    el.addEventListener("input", () => {
      const next = el.value.replace(/\D/g, "").slice(0, PIN_MAX_LEN);
      if (el.value !== next) el.value = next;
    });
  };
  stripNonDigits($("pinInput"));
  stripNonDigits($("pinInputConfirm"));
  $("btnAddTitleRule").addEventListener("click", () => {
    const collected = collectTitleRulesFromEditor();
    collected.push({
      id: crypto.randomUUID(),
      conditionType: "schedule_title",
      value: "",
      record: "{schedule_title} ",
      appendTarget: "A",
      templates: [],
    });
    defaultsDoc.titleRules = collected;
    renderTitleRulesEditor();
  });
  $("btnSaveRules").addEventListener("click", saveRules);
  document.querySelectorAll(".tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      if (tab.dataset.tab === "titleRules") {
        defaultsDoc.titleRules = collectTitleRulesFromEditor();
      } else if (tab.dataset.tab === "templateButtons") {
        defaultsDoc.templateButtons = collectTemplateButtonsFromEditor();
      }
      switchRulesTab(tab.dataset.tab);
      if (tab.dataset.tab === "titleRules") renderTitleRulesEditor();
      else if (tab.dataset.tab === "templateButtons") renderTemplateButtonsEditor();
      else if (tab.dataset.tab === "general") {
        const maxInput = $("specialNotesMaxInput");
        if (maxInput) maxInput.value = String(specialNotesMax());
      }
    });
  });

  $("btnAudit").addEventListener("click", openAuditModal);
  $("btnCloseAudit").addEventListener("click", () => $("modalAudit").classList.add("hidden"));
  $("btnReloadAudit").addEventListener("click", () => loadAudit($("auditMonthLabel").textContent));
  $("btnAuditPrev").addEventListener("click", () => shiftAuditMonth(-1));
  $("btnAuditNext").addEventListener("click", () => shiftAuditMonth(1));

  $("btnPrintPage").addEventListener("click", () => {
    openPrintPage().catch((e) => showToast(e.message || String(e)));
  });
  $("btnPrintModeSlots")?.addEventListener("click", () => setPrintMode("slots"));
  $("btnPrintModeWeek")?.addEventListener("click", () => setPrintMode("week"));
  $("btnPrintSlotDec")?.addEventListener("click", () =>
    setPrintSlotCount(printSlotCount - 1),
  );
  $("btnPrintSlotInc")?.addEventListener("click", () =>
    setPrintSlotCount(printSlotCount + 1),
  );
  $("btnPrintWeekPrev")?.addEventListener("click", () => shiftPrintWeek(-1));
  $("btnPrintWeekNext")?.addEventListener("click", () => shiftPrintWeek(1));
  $("btnBackFromPrint").addEventListener("click", () => showScreen("main"));
  $("btnPrintSignSettings")?.addEventListener("click", () => openPrintSignSettings());
  $("btnClosePrintSigns")?.addEventListener("click", () =>
    $("modalPrintSigns").classList.add("hidden"),
  );
  $("btnSavePrintSigns")?.addEventListener("click", () => savePrintSignsFromModal());
  $("btnDoPrint").addEventListener("click", async () => {
    await refreshPrintPreview();
    const fitted = refreshPrintPreview._fitted || activePrintSlots();
    const dates = fitted.filter((s) => s.type === "date" && s.date).map((s) => s.date);
    if (!fitted.length) {
      showToast("출력할 칸을 선택하세요.");
      return;
    }
    const onAfterPrint = () => {
      window.removeEventListener("afterprint", onAfterPrint);
      void markPrintSuccessful(dates);
    };
    window.addEventListener("afterprint", onAfterPrint);
    window.print();
  });
  $("printCardStyle")?.addEventListener("change", () => {
    const style = currentPrintCardStyle();
    void persistPrintCardStyleToNas(style, { quiet: false });
    refreshPrintPreview().catch((e) => showToast(e.message || String(e)));
  });

  $("btnSpecialNoteNew")?.addEventListener("click", () => specialNoteNew());
  $("btnSpecialNoteDelete")?.addEventListener("click", () => specialNoteDelete());
  $("btnSpecialNotePrev")?.addEventListener("click", () => specialNotePrev());
  $("btnSpecialNoteNext")?.addEventListener("click", () => specialNoteNext());
  $("btnSpecialNoteSave")?.addEventListener("click", () => {
    saveSpecialNotes().catch((e) => showToast(e.message || String(e)));
  });
  $("specialNoteText")?.addEventListener("input", () => {
    specialNotesDirty = true;
  });
  $("specialNoteText")?.addEventListener("change", () => {
    syncSpecialNoteFromUi();
  });

  $("btnAddTemplateInRules").addEventListener("click", () => {
    const input = $("newTemplateLabel");
    if (!addCustomTemplate(input.value)) return;
    input.value = "";
    renderTemplateButtonsEditor();
    if (editorEntry) renderTemplateChips(editorEntry);
  });

  $("btnDeleteEntry").addEventListener("click", async () => {
    if (editMode) return;
    if (!selectedDate) return;
    await refreshEditLockStatus();
    if (
      editLockHolder &&
      editLockHolder.sessionId &&
      editLockHolder.sessionId !== editSessionId
    ) {
      showToast("다른 사용자가 작성 중이라 삭제할 수 없습니다.");
      return;
    }
    if (!confirm(`${selectedDate} 일지를 삭제할까요?`)) return;
    const entry = entryForDate(selectedDate);
    monthDoc.entries = monthDoc.entries.filter((e) => e.date !== selectedDate);
    monthDirty = true;
    if (entry) appendAudit("delete", entry, "일지 삭제");
    await saveMonth();
    const today = todayYmd();
    if (today.startsWith(monthKey())) selectDay(today);
    else {
      selectedDate = null;
      $("entryEditor").classList.add("hidden");
      $("emptyEditor").classList.remove("hidden");
      $("emptyEditor").textContent = "날짜를 선택하세요.";
      renderDayList();
    }
  });

  const releaseOnLeave = () => {
    releaseEditLockBeacon();
  };
  window.addEventListener("pagehide", releaseOnLeave);
  window.addEventListener("beforeunload", (e) => {
    releaseOnLeave();
    if (monthDirty) {
      e.preventDefault();
      e.returnValue = "";
    }
  });
}

bindUi();

/** 창을 닫으면 브리지(exe)도 함께 종료되도록 세션을 유지한다. */
function startBridgeSessionGuard() {
  const ping = () => {
    fetch(`${apiBase}/api/session/ping`, { method: "POST", keepalive: true }).catch(() => {});
  };
  ping();
  setInterval(ping, 5000);

  const bye = () => {
    try {
      if (navigator.sendBeacon) {
        navigator.sendBeacon(`${apiBase}/api/session/bye`, new Blob([], { type: "application/json" }));
      }
      fetch(`${apiBase}/api/session/bye`, { method: "POST", keepalive: true, cache: "no-store" }).catch(() => {});
    } catch {
      // ignore
    }
  };
  window.addEventListener("pagehide", bye);
  window.addEventListener("beforeunload", bye);
}

startBridgeSessionGuard();

(async function boot() {
  try {
    apiBase = defaultApiBase();
    if (typeof window !== "undefined" && window.__UI_DEMO__) {
      connected = true;
      await loadProfilesAndDefaults();
      const demoProfile = profiles[0] || { id: "ko_yunseok", displayName: "고윤석" };
      enterMainWithProfile(demoProfile);
      return;
    }
    await runConnectionFlow();
  } catch (err) {
    const msg = err?.message || String(err);
    console.error("WorkLog boot failed", err);
    setConnStep("bridge", "fail");
    setConnUi({
      state: "error",
      badge: "오류",
      message: msg,
      error: msg,
    });
    showScreen("status");
  }
})();
