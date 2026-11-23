(() => {
  const $ = (id) => document.getElementById(id);

  const PLACE_OPTIONS = [
    ["None", "-"],
    ["Main_Large", "본당 대강당"],
    ["Main_Medium", "본당 중강당"],
    ["Main_Small", "본당 소강당"],
    ["Edu_Large", "교육원 대강당"],
    ["Edu_Medium", "교육원 중강당"],
    ["Edu_Small", "교육원 소강당"],
    ["Edu_Nongseon", "교육원 농선부실"],
    ["Edu_Siseon", "교육원 시선부실"],
    ["Edu_Go2", "교육원 고2실"],
    ["Etc", "기타"],
  ];

  const PREP_OPTIONS = [
    ["Recording", "녹화"],
    ["Praise", "특송"],
    ["Streaming", "스트리밍"],
    ["OtherDisplay", "외부입력"],
  ];

  const FILTER_TARGET_LABELS = {
    preparation: "사전준비",
    startTime: "시간",
    place: "장소",
    accessoryInfo: "부가정보",
    preaparationNote: "설명",
  };

  const WORD_FILTER_KEY = "schedule-reader-word-filters";
  const VIEW_TABS_KEY = "schedule-reader-view-tabs";
  const VIEW_TAB_EXPORT_EXCLUDE_KEY = "schedule-reader-view-tab-export-exclude";
  const EXCLUDE_HOLIDAYS_KEY = "schedule-reader-exclude-holidays";

  const state = {
    sessionId: null,
    extractReady: false,
    items: [],
    openIds: new Set(),
    viewTab: "all",
  };

  const fileLabel = $("fileLabel");
  const sheetSelect = $("sheetSelect");
  const statusText = $("statusText");
  const btnLoad = $("btnLoad");
  const btnExtract = $("btnExtract");
  const btnExport = $("btnExport");
  const btnAddRow = $("btnAddRow");
  const btnSplit = $("btnSplit");
  const btnWordFilters = $("btnWordFilters");
  const btnViewTabs = $("btnViewTabs");
  const excelInput = $("excelInput");
  const previewWrap = $("previewWrap");
  const previewMeta = $("previewMeta");
  const resultWrap = $("resultWrap");
  const resultMeta = $("resultMeta");
  const resultToolbar = $("resultToolbar");
  const viewTabBar = $("viewTabBar");
  const warningList = $("warningList");
  const wordFilterDialog = $("wordFilterDialog");
  const viewTabDialog = $("viewTabDialog");
  const splitDialog = $("splitDialog");
  const splitRows = $("splitRows");
  const chkExcludeHolidays = $("chkExcludeHolidays");
  const exportExcludeWrap = $("exportExcludeWrap");
  const chkExportExcludeTab = $("chkExportExcludeTab");
  let splitLockedPeriod = { scheduleDate: "", releaseDate: "" };

  try {
    chkExcludeHolidays.checked = localStorage.getItem(EXCLUDE_HOLIDAYS_KEY) === "1";
  } catch {
    /* ignore */
  }

  function setStatus(text) {
    statusText.textContent = text;
  }

  function syncButtons() {
    btnExtract.disabled = !state.sessionId;
    btnExport.disabled = !state.extractReady || state.items.length === 0;
  }

  function newId() {
    if (crypto?.randomUUID) return crypto.randomUUID();
    return `id-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  }

  function toDot(iso) {
    const m = String(iso || "")
      .trim()
      .match(/^(\d{4})-(\d{2})-(\d{2})$/);
    if (!m) return String(iso || "");
    return `${m[1]}.${m[2]}.${m[3]}`;
  }

  function formatPeriod(startIso, endIso) {
    const start = toDot(startIso);
    const end = toDot(endIso || startIso);
    if (!start) return "";
    if (!end || end === start) return `${start}~${start}`;
    return `${start}~${end}`;
  }

  function formatTime(value) {
    const n = Number(value || 0);
    if (!n) return "";
    const h = Math.floor(n / 100);
    const m = n % 100;
    return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
  }

  function parseTime(value) {
    const s = String(value || "").trim();
    if (!s) return 0;
    const m = s.match(/^(\d{1,2}):(\d{2})(?::\d{2})?$/);
    if (!m) return 0;
    const hour = Number(m[1]);
    const minute = Number(m[2]);
    if (hour > 23 || minute > 59) return 0;
    return hour * 100 + Math.floor(minute / 10) * 10;
  }

  function toIsoDate(value) {
    const s = String(value || "").trim();
    const iso = s.match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (iso) return `${iso[1]}-${iso[2]}-${iso[3]}`;
    const dotted = s.match(/^(\d{4})\.(\d{2})\.(\d{2})/);
    if (dotted) return `${dotted[1]}-${dotted[2]}-${dotted[3]}`;
    return "";
  }

  function escapeHtml(value) {
    return String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function placeOptionsHtml(selected) {
    const cur = selected || "None";
    return PLACE_OPTIONS.map(
      ([value, label]) =>
        `<option value="${value}" ${value === cur ? "selected" : ""}>${escapeHtml(label)}</option>`,
    ).join("");
  }

  function placeLabel(code) {
    const hit = PLACE_OPTIONS.find(([value]) => value === code);
    return hit ? hit[1] : code || "-";
  }

  function prepChecksHtml(selected) {
    const set = new Set(Array.isArray(selected) ? selected : []);
    return `<div class="prep-checks">${PREP_OPTIONS.map(
      ([value, label]) =>
        `<label><input type="checkbox" class="edit-prep" value="${value}" ${set.has(value) ? "checked" : ""} /> ${escapeHtml(label)}</label>`,
    ).join("")}</div>`;
  }

  function normalizeItem(raw) {
    const start = toIsoDate(raw.scheduleDate) || String(raw.scheduleDate || "").trim();
    let end =
      toIsoDate(raw.releaseDate) ||
      String(raw.releaseDate || "").trim() ||
      start;
    if (end && start && end < start) end = start;
    return {
      id: raw.id || newId(),
      scheduleDate: start,
      releaseDate: end || start,
      startTime: Number(raw.startTime || 0),
      scheduleTitle: raw.scheduleTitle || "",
      accessoryInfo: raw.accessoryInfo || "",
      place: raw.place || "None",
      sermonSpeaker: raw.sermonSpeaker || "",
      preparation: Array.isArray(raw.preparation) ? [...raw.preparation] : [],
      preaparationNote: raw.preaparationNote || "",
      color: raw.color || "auto",
      colorMode: raw.colorMode || "auto",
    };
  }

  /** SDM titleMatchesColorPattern 와 동일 + '=' 정확 일치 */
  function titleMatchesPattern(title, pattern) {
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
        if (token.startsWith("=")) {
          return text === token.slice(1).trim();
        }
        return text.includes(token);
      });
    });
  }

  function loadWordFilters() {
    try {
      const raw = JSON.parse(localStorage.getItem(WORD_FILTER_KEY) || "[]");
      return Array.isArray(raw) ? raw.filter((r) => r && r.pattern && r.target) : [];
    } catch {
      return [];
    }
  }

  function saveWordFilters(rules) {
    localStorage.setItem(WORD_FILTER_KEY, JSON.stringify(rules));
  }

  function parsePrepValue(value) {
    return String(value || "")
      .split(/[,|]/)
      .map((s) => s.trim())
      .filter(Boolean);
  }

  function formatFilterValue(rule) {
    if (rule.target === "preparation") {
      return parsePrepValue(rule.value)
        .map((code) => PREP_OPTIONS.find(([v]) => v === code)?.[1] || code)
        .join(", ");
    }
    if (rule.target === "place") return placeLabel(rule.value);
    if (rule.target === "startTime") {
      const n = Number(rule.value);
      return Number.isFinite(n) && n > 0 ? formatTime(n) : String(rule.value || "");
    }
    return String(rule.value || "");
  }

  function applyWordFilters(items) {
    const rules = loadWordFilters();
    if (!rules.length) return items.map(normalizeItem);
    return items.map((raw) => {
      const next = normalizeItem(raw);
      for (const rule of rules) {
        if (!titleMatchesPattern(next.scheduleTitle, rule.pattern)) continue;
        if (rule.target === "preparation") {
          const add = parsePrepValue(rule.value);
          const set = new Set(next.preparation);
          for (const code of add) set.add(code);
          next.preparation = [...set];
        } else if (rule.target === "startTime") {
          const asNum = Number(rule.value);
          next.startTime =
            Number.isFinite(asNum) && asNum > 0 ? asNum : parseTime(rule.value);
        } else if (rule.target === "place") {
          next.place = rule.value || "None";
        } else if (rule.target === "accessoryInfo") {
          const add = String(rule.value || "").trim();
          if (add) {
            next.accessoryInfo = next.accessoryInfo
              ? `${next.accessoryInfo} ${add}`
              : add;
          }
        } else if (rule.target === "preaparationNote") {
          next.preaparationNote = String(rule.value || "");
        }
      }
      return next;
    });
  }

  function appendJiphoeAndRefilter(id) {
    syncVisibleRowsToState();
    const index = state.items.findIndex((item) => item.id === id);
    if (index < 0) return;
    const current = normalizeItem(state.items[index]);
    const title = String(current.scheduleTitle || "");
    if (!title.trim()) {
      setStatus("제목이 비어 있어 +집회를 적용할 수 없습니다.");
      return;
    }
    if (!title.endsWith("집회")) {
      current.scheduleTitle = `${title}집회`;
    }
    state.items[index] = applyWordFilters([current])[0];
    refreshResultView({ openId: id });
    syncButtons();
    setStatus(`「${state.items[index].scheduleTitle}」에 단어 필터를 다시 적용했습니다.`);
  }

  function renderPreview(preview) {
    if (!preview?.rows?.length) {
      previewWrap.innerHTML = `<p class="empty">미리볼 셀이 없습니다.</p>`;
      previewMeta.textContent = "";
      return;
    }
    const colCount = Math.max(...preview.rows.map((r) => r.length), 1);
    const head = Array.from({ length: colCount }, (_, i) => `<th>${i + 1}</th>`).join("");
    const body = preview.rows
      .map(
        (row) =>
          `<tr>${Array.from({ length: colCount }, (_, i) => `<td>${escapeHtml(row[i] || "")}</td>`).join("")}</tr>`,
      )
      .join("");
    previewWrap.innerHTML = `<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
    previewMeta.textContent = `${preview.sheet} · 표시 ${preview.rows.length}행`;
  }

  function collectItemFromRow(row) {
    const start = row.querySelector(".edit-start")?.value || "";
    let end = row.querySelector(".edit-end")?.value || start;
    if (end && start && end < start) end = start;
    const preparation = [...row.querySelectorAll(".edit-prep:checked")].map(
      (box) => box.value,
    );
    return normalizeItem({
      id: row.dataset.id,
      scheduleDate: start,
      releaseDate: end || start,
      startTime: parseTime(row.querySelector(".edit-time")?.value || ""),
      scheduleTitle: row.querySelector(".edit-title")?.value || "",
      sermonSpeaker: row.querySelector(".edit-speaker")?.value || "",
      place: row.querySelector(".edit-place")?.value || "None",
      accessoryInfo: row.querySelector(".edit-accessory")?.value || "",
      preaparationNote: row.querySelector(".edit-note")?.value || "",
      preparation,
      color: row.dataset.color || "auto",
      colorMode: row.dataset.colorMode || "auto",
    });
  }

  /** 화면에 보이는 행만 state에 반영 (보기 탭으로 숨긴 항목 유지) */
  function syncVisibleRowsToState() {
    const updates = new Map(
      [...resultWrap.querySelectorAll("[data-row]")].map((row) => {
        const item = collectItemFromRow(row);
        return [item.id, item];
      }),
    );
    if (!updates.size) return;
    state.items = state.items.map((item) => {
      const updated = updates.get(item.id);
      if (!updated) return item;
      // 시간만 고칠 때 날짜 input이 비어 기간이 지워지지 않게 유지
      if (!toIsoDate(updated.scheduleDate) && toIsoDate(item.scheduleDate)) {
        updated.scheduleDate = item.scheduleDate;
        updated.releaseDate = item.releaseDate || item.scheduleDate;
      } else if (
        toIsoDate(updated.scheduleDate) &&
        !toIsoDate(updated.releaseDate)
      ) {
        updated.releaseDate = updated.scheduleDate;
      }
      return updated;
    });
  }

  function loadViewTabs() {
    try {
      const raw = JSON.parse(localStorage.getItem(VIEW_TABS_KEY) || "[]");
      return Array.isArray(raw)
        ? raw
            .filter((t) => t && String(t.word || "").trim())
            .map((t) => ({
              id: t.id || newId(),
              word: String(t.word).trim(),
            }))
        : [];
    } catch {
      return [];
    }
  }

  function saveViewTabs(tabs) {
    localStorage.setItem(VIEW_TABS_KEY, JSON.stringify(tabs));
  }

  function loadExportExcludeTabs() {
    try {
      const raw = JSON.parse(localStorage.getItem(VIEW_TAB_EXPORT_EXCLUDE_KEY) || "{}");
      return raw && typeof raw === "object" ? raw : {};
    } catch {
      return {};
    }
  }

  function saveExportExcludeTabs(map) {
    localStorage.setItem(VIEW_TAB_EXPORT_EXCLUDE_KEY, JSON.stringify(map));
  }

  function isCustomViewTab(tabId = state.viewTab) {
    return Boolean(tabId) && tabId !== "all" && tabId !== "default";
  }

  function syncExportExcludeOption() {
    if (!exportExcludeWrap || !chkExportExcludeTab) return;
    if (!state.items.length || !isCustomViewTab()) {
      exportExcludeWrap.classList.add("hidden");
      return;
    }
    exportExcludeWrap.classList.remove("hidden");
    const flags = loadExportExcludeTabs();
    chkExportExcludeTab.checked = Boolean(flags[state.viewTab]);
  }

  function itemsExcludedFromExport(item, tabs = loadViewTabs(), flags = loadExportExcludeTabs()) {
    return tabs.some(
      (tab) =>
        flags[tab.id] && titleMatchesPattern(item.scheduleTitle, tab.word),
    );
  }

  function titleMatchesAnyViewTab(title, tabs = loadViewTabs()) {
    return tabs.some((tab) => titleMatchesPattern(title, tab.word));
  }

  function itemsForActiveView(items) {
    const tabs = loadViewTabs();
    if (state.viewTab === "all") return items;
    if (state.viewTab === "default") {
      return items.filter(
        (item) => !titleMatchesAnyViewTab(item.scheduleTitle, tabs),
      );
    }
    const tab = tabs.find((entry) => entry.id === state.viewTab);
    if (!tab) return items;
    return items.filter((item) =>
      titleMatchesPattern(item.scheduleTitle, tab.word),
    );
  }

  function renderViewTabBar() {
    const tabs = loadViewTabs();
    const known = new Set(["all", "default", ...tabs.map((tab) => tab.id)]);
    if (!known.has(state.viewTab)) state.viewTab = "all";

    if (!state.items.length) {
      viewTabBar.classList.add("hidden");
      viewTabBar.innerHTML = "";
      return;
    }

    viewTabBar.classList.remove("hidden");
    const defaultCount = state.items.filter(
      (item) => !titleMatchesAnyViewTab(item.scheduleTitle, tabs),
    ).length;
    const buttons = [
      { id: "all", label: "전체", count: state.items.length },
      { id: "default", label: "기본", count: defaultCount },
      ...tabs.map((tab) => ({
        id: tab.id,
        label: tab.word,
        count: state.items.filter((item) =>
          titleMatchesPattern(item.scheduleTitle, tab.word),
        ).length,
      })),
    ];
    viewTabBar.innerHTML = buttons
      .map(
        (btn) =>
          `<button type="button" class="view-tab${state.viewTab === btn.id ? " is-active" : ""}" data-view-tab="${escapeHtml(btn.id)}" role="tab" aria-selected="${state.viewTab === btn.id}">${escapeHtml(btn.label)} <span class="view-tab-count">${btn.count}</span></button>`,
      )
      .join("");
  }

  function renderViewTabList() {
    const tabs = loadViewTabs();
    const list = $("viewTabList");
    if (!tabs.length) {
      list.innerHTML = `<li class="muted">추가된 탭이 없습니다.</li>`;
      return;
    }
    list.innerHTML = tabs
      .map(
        (tab) => `<li data-view-tab-id="${escapeHtml(tab.id)}">
          <div class="filter-rule-meta">
            <strong>${escapeHtml(tab.word)}</strong>
            <span>제목 조건 (or / and / =)</span>
          </div>
          <button type="button" class="button row-delete" data-remove-view-tab>삭제</button>
        </li>`,
      )
      .join("");
  }

  function openViewTabDialog() {
    renderViewTabList();
    viewTabDialog.showModal();
    $("viewTabWord").focus();
  }

  function refreshResultView({ openId = null } = {}) {
    if (openId) state.openIds.add(openId);
    renderResult(
      {
        ok: true,
        items: state.items,
        warnings: state.warnings || [],
        sheet: sheetSelect.value || "",
        rawItemCount: state.rawItemCount,
      },
      { openId },
    );
  }

  function syncPeriodLabel(row) {
    const start = row.querySelector(".edit-start")?.value || "";
    const end = row.querySelector(".edit-end")?.value || start;
    const label = row.querySelector(".period-label");
    if (label) label.textContent = formatPeriod(start, end || start);
  }

  function syncTitleLabel(row) {
    const title = row.querySelector(".edit-title")?.value || "";
    const label = row.querySelector(".title-label");
    if (!label) return;
    const text = title.trim();
    label.textContent = text || "(제목 없음)";
    label.classList.toggle("is-empty", !text);
  }

  function renderResultCard(item) {
    const open = state.openIds.has(item.id);
    const titleText = String(item.scheduleTitle || "").trim();
    return `<article class="result-item${open ? " is-open" : ""}" data-row data-id="${escapeHtml(item.id)}" data-color="${escapeHtml(item.color)}" data-color-mode="${escapeHtml(item.colorMode)}">
          <div class="result-item-head" data-toggle>
            <div class="result-item-summary">
              <span class="period-label">${escapeHtml(formatPeriod(item.scheduleDate, item.releaseDate))}</span>
              <span class="title-label${titleText ? "" : " is-empty"}">${escapeHtml(titleText || "(제목 없음)")}</span>
            </div>
            <div class="result-item-actions">
              <button type="button" class="button row-jiphoe" data-append-jiphoe title="제목 끝에 집회 붙이고 단어 필터 재적용">+집회</button>
              <button type="button" class="button row-delete" data-delete title="행 삭제">삭제</button>
            </div>
          </div>
          <div class="result-item-fields">
            <label class="field field-period">
              <span class="field-label">기간</span>
              <div class="period-inputs">
                <input type="date" class="edit-start" value="${escapeHtml(item.scheduleDate)}" aria-label="시작일" />
                <span class="period-tilde">~</span>
                <input type="date" class="edit-end" value="${escapeHtml(item.releaseDate || item.scheduleDate)}" aria-label="종료일" />
              </div>
            </label>
            <label class="field field-time">
              <span class="field-label">시간</span>
              <input type="time" class="edit-time" step="600" value="${escapeHtml(formatTime(item.startTime))}" />
            </label>
            <label class="field field-title">
              <span class="field-label">제목</span>
              <input type="text" class="edit-title" value="${escapeHtml(item.scheduleTitle)}" />
            </label>
            <label class="field field-speaker">
              <span class="field-label">강사</span>
              <input type="text" class="edit-speaker" value="${escapeHtml(item.sermonSpeaker)}" />
            </label>
            <label class="field field-place">
              <span class="field-label">장소</span>
              <select class="edit-place">${placeOptionsHtml(item.place)}</select>
            </label>
            <label class="field field-accessory">
              <span class="field-label">부가정보</span>
              <input type="text" class="edit-accessory" value="${escapeHtml(item.accessoryInfo)}" />
            </label>
            <label class="field field-note">
              <span class="field-label">설명</span>
              <input type="text" class="edit-note" value="${escapeHtml(item.preaparationNote)}" />
            </label>
            <div class="field field-prep">
              <span class="field-label">사전준비</span>
              ${prepChecksHtml(item.preparation)}
            </div>
          </div>
        </article>`;
  }

  function renderResult(payload, { openId = null } = {}) {
    const warnings = payload.warnings || [];
    state.warnings = warnings;
    state.rawItemCount = payload.rawItemCount;
    if (warnings.length) {
      warningList.classList.remove("hidden");
      warningList.innerHTML = warnings.map((w) => `<li>${escapeHtml(w)}</li>`).join("");
    } else {
      warningList.classList.add("hidden");
      warningList.innerHTML = "";
    }

    state.items = (payload.items || []).map(normalizeItem);
    if (openId) state.openIds.add(openId);
    const keep = new Set(state.items.map((item) => item.id));
    state.openIds = new Set([...state.openIds].filter((id) => keep.has(id)));

    const visible = itemsForActiveView(state.items);
    const rawCount = payload.rawItemCount;
    const viewNote =
      state.viewTab === "all"
        ? ""
        : ` · 보기 ${visible.length}/${state.items.length}`;
    resultMeta.textContent = payload.ok
      ? `${state.items.length}건(기간)${rawCount != null ? ` · 일별 ${rawCount}` : ""}${viewNote}${payload.sheet ? ` · ${payload.sheet}` : ""}`
      : "추출 실패";

    renderViewTabBar();

    if (!state.items.length) {
      resultToolbar.classList.add("hidden");
      syncExportExcludeOption();
      resultWrap.innerHTML = `<p class="empty">추출된 일정이 없습니다.</p>`;
      return;
    }

    resultToolbar.classList.remove("hidden");
    syncExportExcludeOption();
    if (!visible.length) {
      resultWrap.innerHTML = `<p class="empty">이 탭에 해당하는 일정이 없습니다.</p>`;
      return;
    }

    resultWrap.innerHTML = `<div class="result-list">${visible.map(renderResultCard).join("")}</div>`;
  }

  function fillSheets(sheets, active) {
    sheetSelect.innerHTML = (sheets || [])
      .map(
        (name) =>
          `<option value="${escapeHtml(name)}" ${name === active ? "selected" : ""}>${escapeHtml(name)}</option>`,
      )
      .join("");
    sheetSelect.disabled = !(sheets && sheets.length);
  }

  function clearResult() {
    state.items = [];
    state.extractReady = false;
    state.openIds = new Set();
    state.warnings = [];
    state.rawItemCount = undefined;
    state.viewTab = "all";
    resultToolbar.classList.add("hidden");
    viewTabBar.classList.add("hidden");
    viewTabBar.innerHTML = "";
    syncExportExcludeOption();
    resultWrap.innerHTML = `<p class="empty">추출이 성공하면 일정 목록이 표시됩니다.</p>`;
    resultMeta.textContent = "";
    warningList.classList.add("hidden");
  }

  function filterValueControlsHtml(target) {
    if (target === "preparation") {
      return prepChecksHtml([]).replaceAll('class="edit-prep"', 'class="filter-prep"');
    }
    if (target === "startTime") {
      return `<input type="time" step="600" class="filter-value-time" aria-label="시간 값" disabled />`;
    }
    if (target === "place") {
      return `<select class="filter-value-place" aria-label="장소 값" disabled>${placeOptionsHtml("None")}</select>`;
    }
    if (target === "accessoryInfo") {
      return `<input type="text" class="filter-value-text" placeholder="부가정보에 이어서 붙일 내용" autocomplete="off" disabled />`;
    }
    return `<input type="text" class="filter-value-text" placeholder="설명 내용" autocomplete="off" disabled />`;
  }

  function syncFilterTargetRow(row) {
    const enabled = row.querySelector(".filter-target-enable")?.checked;
    row.classList.toggle("is-off", !enabled);
    row.querySelectorAll("input, select").forEach((el) => {
      if (el.classList.contains("filter-target-enable")) return;
      el.disabled = !enabled;
    });
  }

  function renderFilterTargetRows() {
    const host = $("filterTargetRows");
    host.innerHTML = Object.entries(FILTER_TARGET_LABELS)
      .map(
        ([target, label]) => `<div class="filter-target-row is-off" data-target="${escapeHtml(target)}">
          <label class="filter-target-check">
            <input type="checkbox" class="filter-target-enable" value="${escapeHtml(target)}" />
            ${escapeHtml(label)}
          </label>
          <div class="filter-target-value">${filterValueControlsHtml(target)}</div>
        </div>`,
      )
      .join("");
    host.querySelectorAll(".filter-target-row").forEach((row) => syncFilterTargetRow(row));
  }

  function readFilterValueFromRow(row, target) {
    if (target === "preparation") {
      return [...row.querySelectorAll(".filter-prep:checked")].map((box) => box.value).join(",");
    }
    if (target === "startTime") {
      return String(parseTime(row.querySelector(".filter-value-time")?.value || "") || "");
    }
    if (target === "place") {
      return row.querySelector(".filter-value-place")?.value || "None";
    }
    return (row.querySelector(".filter-value-text")?.value || "").trim();
  }

  function collectCheckedFilterRules(pattern) {
    const added = [];
    const errors = [];
    for (const row of document.querySelectorAll("#filterTargetRows .filter-target-row")) {
      const enable = row.querySelector(".filter-target-enable");
      if (!enable?.checked) continue;
      const target = enable.value;
      const value = readFilterValueFromRow(row, target);
      if (target === "preparation" && !value) {
        errors.push("사전준비 항목을 하나 이상 선택해 주세요.");
        continue;
      }
      if ((target === "accessoryInfo" || target === "preaparationNote") && !value) {
        errors.push(`${FILTER_TARGET_LABELS[target]} 내용을 입력해 주세요.`);
        continue;
      }
      added.push({ id: newId(), pattern, target, value });
    }
    return { added, errors };
  }

  function renderFilterRuleList() {
    const rules = loadWordFilters();
    const list = $("filterRuleList");
    if (!rules.length) {
      list.innerHTML = `<li class="muted">저장된 조건이 없습니다.</li>`;
      return;
    }
    list.innerHTML = rules
      .map(
        (rule) => `<li data-rule-id="${escapeHtml(rule.id)}">
          <div class="filter-rule-meta">
            <strong>${escapeHtml(rule.pattern)}</strong>
            <span>${escapeHtml(FILTER_TARGET_LABELS[rule.target] || rule.target)} → ${escapeHtml(formatFilterValue(rule))}</span>
          </div>
          <button type="button" class="button row-delete" data-remove-rule>삭제</button>
        </li>`,
      )
      .join("");
  }

  function openWordFilterDialog() {
    renderFilterTargetRows();
    renderFilterRuleList();
    wordFilterDialog.showModal();
  }

  function renderSplitSourceInfo(item) {
    const host = $("splitSourceInfo");
    if (!item) {
      host.innerHTML = "";
      return;
    }
    const prepLabels = (item.preparation || [])
      .map((code) => PREP_OPTIONS.find(([v]) => v === code)?.[1] || code)
      .join(", ");
    host.innerHTML = `
      <div class="split-source-grid">
        <div><span class="muted">기간</span><strong>${escapeHtml(formatPeriod(item.scheduleDate, item.releaseDate))}</strong></div>
        <div><span class="muted">제목</span><strong>${escapeHtml(item.scheduleTitle || "(제목 없음)")}</strong></div>
        <div><span class="muted">강사</span><strong>${escapeHtml(item.sermonSpeaker || "-")}</strong></div>
        <div><span class="muted">시간</span><strong>${escapeHtml(formatTime(item.startTime) || "-")}</strong></div>
        <div><span class="muted">장소</span><strong>${escapeHtml(placeLabel(item.place))}</strong></div>
        <div class="split-source-wide"><span class="muted">부가정보</span><strong>${escapeHtml(item.accessoryInfo || "-")}</strong></div>
        <div class="split-source-wide"><span class="muted">설명</span><strong>${escapeHtml(item.preaparationNote || "-")}</strong></div>
        <div class="split-source-wide"><span class="muted">사전준비</span><strong>${escapeHtml(prepLabels || "-")}</strong></div>
      </div>`;
  }

  function createSplitRow(seed = {}) {
    const row = document.createElement("div");
    row.className = "split-row";
    row.innerHTML = `
      <div class="split-row-head">
        <strong class="split-row-label">복제</strong>
        <button type="button" class="button row-delete" data-remove-split-row>삭제</button>
      </div>
      <div class="split-fields">
        <label class="field">
          <span class="field-label">새 제목</span>
          <input type="text" class="split-title" value="${escapeHtml(seed.scheduleTitle || "")}" autocomplete="off" />
        </label>
        <label class="field">
          <span class="field-label">강사</span>
          <input type="text" class="split-speaker" value="${escapeHtml(seed.sermonSpeaker || "")}" autocomplete="off" />
        </label>
        <label class="field">
          <span class="field-label">시간</span>
          <input type="time" class="split-time" step="600" value="${escapeHtml(formatTime(seed.startTime || 0))}" />
        </label>
        <label class="field">
          <span class="field-label">장소</span>
          <select class="split-place">${placeOptionsHtml(seed.place || "None")}</select>
        </label>
        <label class="field field-accessory">
          <span class="field-label">부가정보</span>
          <input type="text" class="split-accessory" value="${escapeHtml(seed.accessoryInfo || "")}" autocomplete="off" />
        </label>
        <label class="field field-note">
          <span class="field-label">설명</span>
          <input type="text" class="split-note" value="${escapeHtml(seed.preaparationNote || "")}" autocomplete="off" />
        </label>
        <div class="field field-prep">
          <span class="field-label">사전준비</span>
          ${prepChecksHtml(seed.preparation || [])}
        </div>
      </div>`;
    row.querySelectorAll(".edit-prep").forEach((box) => {
      box.classList.remove("edit-prep");
      box.classList.add("split-prep");
    });
    return row;
  }

  function resetSplitRows(sourceItem) {
    splitRows.innerHTML = "";
    splitRows.appendChild(
      createSplitRow({
        scheduleTitle: "",
        sermonSpeaker: sourceItem?.sermonSpeaker || "",
        startTime: sourceItem?.startTime || 0,
        place: sourceItem?.place || "None",
        accessoryInfo: sourceItem?.accessoryInfo || "",
        preaparationNote: sourceItem?.preaparationNote || "",
        preparation: sourceItem?.preparation || [],
      }),
    );
  }

  function openSplitDialog() {
    syncVisibleRowsToState();
    if (!state.items.length) {
      setStatus("나눌 일정이 없습니다. 먼저 추출하거나 행을 추가하세요.");
      return;
    }
    const source = $("splitSource");
    source.innerHTML = state.items
      .map((item) => {
        const label = `${formatPeriod(item.scheduleDate, item.releaseDate)} · ${item.scheduleTitle || "(제목 없음)"}`;
        return `<option value="${escapeHtml(item.id)}">${escapeHtml(label)}</option>`;
      })
      .join("");
    fillSplitFromSource();
    splitDialog.showModal();
    splitRows.querySelector(".split-title")?.focus();
  }

  function fillSplitFromSource() {
    const id = $("splitSource").value;
    const item = state.items.find((entry) => entry.id === id);
    const start = toIsoDate(item?.scheduleDate) || item?.scheduleDate || "";
    const end =
      toIsoDate(item?.releaseDate) || item?.releaseDate || start;
    splitLockedPeriod = { scheduleDate: start, releaseDate: end || start };
    renderSplitSourceInfo(item);
    resetSplitRows(item);
  }

  function readSplitRow(row) {
    return {
      scheduleTitle: (row.querySelector(".split-title")?.value || "").trim(),
      sermonSpeaker: (row.querySelector(".split-speaker")?.value || "").trim(),
      startTime: parseTime(row.querySelector(".split-time")?.value || ""),
      place: row.querySelector(".split-place")?.value || "None",
      accessoryInfo: (row.querySelector(".split-accessory")?.value || "").trim(),
      preaparationNote: (row.querySelector(".split-note")?.value || "").trim(),
      preparation: [...row.querySelectorAll(".split-prep:checked")].map((box) => box.value),
    };
  }

  function scheduleCenterYearMonth(items) {
    const days = [];
    for (const item of items) {
      const start = toIsoDate(item.scheduleDate);
      const end = toIsoDate(item.releaseDate) || start;
      if (start) days.push(start);
      if (end) days.push(end);
    }
    if (!days.length) {
      const now = new Date();
      return `${now.getFullYear()}${String(now.getMonth() + 1).padStart(2, "0")}`;
    }
    days.sort();
    const min = days[0];
    const max = days[days.length - 1];
    const [y1, m1, d1] = min.split("-").map(Number);
    const [y2, m2, d2] = max.split("-").map(Number);
    const mid = new Date(
      Date.UTC(y1, m1 - 1, d1) +
        (Date.UTC(y2, m2 - 1, d2) - Date.UTC(y1, m1 - 1, d1)) / 2,
    );
    return `${mid.getUTCFullYear()}${String(mid.getUTCMonth() + 1).padStart(2, "0")}`;
  }

  function exportDownloadName(items) {
    const yyyymm = scheduleCenterYearMonth(items);
    const now = new Date();
    const stamp = [
      now.getFullYear(),
      String(now.getMonth() + 1).padStart(2, "0"),
      String(now.getDate()).padStart(2, "0"),
      String(now.getHours()).padStart(2, "0"),
      String(now.getMinutes()).padStart(2, "0"),
      String(now.getSeconds()).padStart(2, "0"),
    ].join("");
    return `ScheduleConvertedData_${yyyymm}_${stamp}.json`;
  }

  btnLoad.addEventListener("click", () => excelInput.click());

  excelInput.addEventListener("change", async () => {
    const file = excelInput.files?.[0];
    excelInput.value = "";
    if (!file) return;
    const body = new FormData();
    body.append("file", file);
    setStatus("엑셀 불러오는 중…");
    clearResult();
    syncButtons();
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 120000);
    try {
      const res = await fetch("/api/workbook", { method: "POST", body, signal: ctrl.signal });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.detail || data.message || "불러오기 실패");
      state.sessionId = data.sessionId;
      fileLabel.textContent = data.filename || file.name;
      fillSheets(data.sheets, data.activeSheet);
      renderPreview(data.preview);
      setStatus("엑셀을 불러왔습니다. 시트를 확인한 뒤 「일정 추출」을 누르세요.");
      syncButtons();
    } catch (err) {
      state.sessionId = null;
      syncButtons();
      if (err?.name === "AbortError") {
        setStatus("불러오기 시간이 너무 깁니다. 파일이 매우 크면 시트 수를 줄이거나 필요한 달만 새 파일로 저장해 보세요.");
      } else if (String(err?.message || err).includes("Failed to fetch")) {
        setStatus("서버 응답이 없습니다. ScheduleReader 창이 켜져 있는지 확인한 뒤 다시 시도하세요. (여러 시트·도형이 많은 파일은 최신 버전에서 개선됨)");
      } else {
        setStatus(err.message || String(err));
      }
    } finally {
      clearTimeout(timer);
    }
  });

  sheetSelect.addEventListener("change", async () => {
    if (!state.sessionId) return;
    const body = new FormData();
    body.append("sessionId", state.sessionId);
    body.append("sheet", sheetSelect.value);
    setStatus("시트 전환 중…");
    clearResult();
    syncButtons();
    try {
      const res = await fetch("/api/workbook/sheet", { method: "POST", body });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.detail || "시트 전환 실패");
      renderPreview(data.preview);
      setStatus(`시트「${sheetSelect.value}」미리보기입니다. 「일정 추출」을 누르세요.`);
    } catch (err) {
      setStatus(err.message || String(err));
    }
  });

  btnExtract.addEventListener("click", async () => {
    if (!state.sessionId) return;
    const body = new FormData();
    body.append("sessionId", state.sessionId);
    body.append("sheet", sheetSelect.value || "");
    body.append("excludeHolidays", chkExcludeHolidays.checked ? "1" : "0");
    setStatus("일정 추출 중…");
    btnExtract.disabled = true;
    try {
      const res = await fetch("/api/extract", { method: "POST", body });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.detail || "추출 실패");
      state.extractReady = Boolean(data.extractReady);
      state.openIds = new Set();
      state.viewTab = "all";
      const filtered = {
        ...data,
        items: applyWordFilters(data.items || []),
      };
      renderResult(filtered);
      const ruleCount = loadWordFilters().length;
      const holidayNote =
        chkExcludeHolidays.checked && data.holidayExcludedCount
          ? ` · 공휴일 ${data.holidayExcludedCount}건 제외`
          : chkExcludeHolidays.checked
            ? " · 공휴일 제외"
            : "";
      setStatus(
        data.ok
          ? `추출 완료: ${filtered.items.length}건(기간 합침)${holidayNote}${ruleCount ? ` · 단어 필터 ${ruleCount}개 적용` : ""}. 행을 눌러 수정한 뒤 내보내세요.`
          : "추출에 실패했습니다. 경고와 엑셀 표 형식을 확인하세요.",
      );
    } catch (err) {
      state.extractReady = false;
      state.items = [];
      setStatus(err.message || String(err));
    } finally {
      syncButtons();
    }
  });

  chkExcludeHolidays.addEventListener("change", () => {
    try {
      localStorage.setItem(EXCLUDE_HOLIDAYS_KEY, chkExcludeHolidays.checked ? "1" : "0");
    } catch {
      /* ignore */
    }
  });

  chkExportExcludeTab.addEventListener("change", () => {
    if (!isCustomViewTab()) return;
    const flags = loadExportExcludeTabs();
    if (chkExportExcludeTab.checked) flags[state.viewTab] = true;
    else delete flags[state.viewTab];
    saveExportExcludeTabs(flags);
    const tab = loadViewTabs().find((entry) => entry.id === state.viewTab);
    setStatus(
      chkExportExcludeTab.checked
        ? `「${tab?.word || "이 탭"}」 일정을 내보내기에서 제외합니다.`
        : `「${tab?.word || "이 탭"}」 일정을 다시 내보내기에 포함합니다.`,
    );
  });

  resultWrap.addEventListener("input", (event) => {
    const row = event.target.closest("[data-row]");
    if (!row) return;
    if (event.target.classList.contains("edit-start") || event.target.classList.contains("edit-end")) {
      syncPeriodLabel(row);
    }
    if (event.target.classList.contains("edit-title")) {
      syncTitleLabel(row);
    }
    syncVisibleRowsToState();
    renderViewTabBar();
    syncButtons();
  });

  resultWrap.addEventListener("change", (event) => {
    const row = event.target.closest("[data-row]");
    if (!row) return;
    if (event.target.classList.contains("edit-start") || event.target.classList.contains("edit-end")) {
      const start = row.querySelector(".edit-start");
      const end = row.querySelector(".edit-end");
      if (start?.value && end?.value && end.value < start.value) end.value = start.value;
      syncPeriodLabel(row);
    }
    syncVisibleRowsToState();
    renderViewTabBar();
    syncButtons();
  });

  resultWrap.addEventListener("click", (event) => {
    const jiphoeBtn = event.target.closest("[data-append-jiphoe]");
    if (jiphoeBtn) {
      event.preventDefault();
      event.stopPropagation();
      const id = jiphoeBtn.closest("[data-row]")?.dataset.id;
      if (id) appendJiphoeAndRefilter(id);
      return;
    }

    const deleteBtn = event.target.closest("[data-delete]");
    if (deleteBtn) {
      const row = deleteBtn.closest("[data-row]");
      const id = row?.dataset.id;
      syncVisibleRowsToState();
      if (id) {
        state.openIds.delete(id);
        state.items = state.items.filter((item) => item.id !== id);
      }
      if (!state.items.length) {
        resultToolbar.classList.add("hidden");
        viewTabBar.classList.add("hidden");
        resultWrap.innerHTML = `<p class="empty">일정이 없습니다. 「행 추가」로 넣거나 다시 추출하세요.</p>`;
        resultToolbar.classList.remove("hidden");
        resultMeta.textContent = `0건(기간)`;
      } else {
        refreshResultView();
      }
      syncButtons();
      return;
    }

    const toggle = event.target.closest("[data-toggle]");
    if (
      !toggle ||
      event.target.closest("[data-delete]") ||
      event.target.closest("[data-append-jiphoe]")
    ) {
      return;
    }
    const row = toggle.closest("[data-row]");
    if (!row?.dataset.id) return;
    const id = row.dataset.id;
    if (state.openIds.has(id)) state.openIds.delete(id);
    else state.openIds.add(id);
    row.classList.toggle("is-open", state.openIds.has(id));
  });

  btnAddRow.addEventListener("click", () => {
    syncVisibleRowsToState();
    const today = new Date();
    const iso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-${String(today.getDate()).padStart(2, "0")}`;
    const item = normalizeItem({
      scheduleDate: iso,
      releaseDate: iso,
      scheduleTitle: "",
      place: "None",
      color: "auto",
      colorMode: "auto",
    });
    state.items.push(item);
    state.viewTab = "all";
    refreshResultView({ openId: item.id });
    state.extractReady = true;
    syncButtons();
    const lastTitle = resultWrap.querySelector(`.result-item[data-id="${item.id}"] .edit-title`);
    lastTitle?.focus();
  });

  btnSplit.addEventListener("click", () => openSplitDialog());
  $("splitClose").addEventListener("click", () => splitDialog.close());
  $("splitCancel").addEventListener("click", () => splitDialog.close());
  $("splitSource").addEventListener("change", () => fillSplitFromSource());
  $("splitAddRow").addEventListener("click", () => {
    const id = $("splitSource").value;
    const item = state.items.find((entry) => entry.id === id);
    splitRows.appendChild(
      createSplitRow({
        scheduleTitle: "",
        sermonSpeaker: item?.sermonSpeaker || "",
        startTime: item?.startTime || 0,
        place: item?.place || "None",
        accessoryInfo: item?.accessoryInfo || "",
        preaparationNote: item?.preaparationNote || "",
        preparation: item?.preparation || [],
      }),
    );
    const rows = splitRows.querySelectorAll(".split-row");
    rows[rows.length - 1]?.querySelector(".split-title")?.focus();
  });
  splitRows.addEventListener("click", (event) => {
    const btn = event.target.closest("[data-remove-split-row]");
    if (!btn) return;
    const row = btn.closest(".split-row");
    if (!row) return;
    if (splitRows.querySelectorAll(".split-row").length <= 1) {
      setStatus("복제 행은 최소 1개가 필요합니다.");
      return;
    }
    row.remove();
  });
  $("splitConfirm").addEventListener("click", () => {
    syncVisibleRowsToState();
    const source = state.items.find((item) => item.id === $("splitSource").value);
    if (!source) {
      setStatus("원본 일정을 선택해 주세요.");
      return;
    }
    const drafts = [...splitRows.querySelectorAll(".split-row")].map(readSplitRow);
    if (!drafts.length) {
      setStatus("복제할 행이 없습니다.");
      return;
    }
    const missing = drafts.findIndex((draft) => !draft.scheduleTitle);
    if (missing >= 0) {
      setStatus("모든 복제 행에 새 제목을 입력해 주세요.");
      splitRows.querySelectorAll(".split-title")[missing]?.focus();
      return;
    }
    const periodStart =
      toIsoDate(splitLockedPeriod.scheduleDate) ||
      toIsoDate(source.scheduleDate) ||
      source.scheduleDate;
    const periodEnd =
      toIsoDate(splitLockedPeriod.releaseDate) ||
      toIsoDate(source.releaseDate) ||
      periodStart;
    if (!periodStart) {
      setStatus("원본 일정의 날짜 기간을 확인할 수 없습니다.");
      return;
    }
    const clonedIds = [];
    for (const draft of drafts) {
      const cloned = normalizeItem({
        scheduleDate: periodStart,
        releaseDate: periodEnd || periodStart,
        scheduleTitle: draft.scheduleTitle,
        sermonSpeaker: draft.sermonSpeaker,
        startTime: draft.startTime,
        place: draft.place,
        accessoryInfo: draft.accessoryInfo,
        preparation: draft.preparation,
        preaparationNote: draft.preaparationNote,
        color: "auto",
        colorMode: "auto",
      });
      state.items.push(cloned);
      clonedIds.push(cloned.id);
    }
    splitDialog.close();
    state.viewTab = "all";
    for (const id of clonedIds) state.openIds.add(id);
    refreshResultView({ openId: clonedIds[clonedIds.length - 1] });
    state.extractReady = true;
    syncButtons();
    setStatus(
      drafts.length === 1
        ? `일정 나누기: 「${drafts[0].scheduleTitle}」을(를) 같은 기간으로 추가했습니다.`
        : `일정 나누기: ${drafts.length}건을 같은 기간으로 추가했습니다.`,
    );
    resultWrap
      .querySelector(`.result-item[data-id="${clonedIds[clonedIds.length - 1]}"] .edit-title`)
      ?.focus();
  });

  btnWordFilters.addEventListener("click", () => openWordFilterDialog());
  $("wordFilterClose").addEventListener("click", () => wordFilterDialog.close());
  $("wordFilterDone").addEventListener("click", () => wordFilterDialog.close());
  $("filterTargetRows").addEventListener("change", (event) => {
    const enable = event.target.closest(".filter-target-enable");
    if (!enable) return;
    const row = enable.closest(".filter-target-row");
    if (row) syncFilterTargetRow(row);
  });
  $("filterAddBtn").addEventListener("click", () => {
    const pattern = ($("filterPattern").value || "").trim();
    if (!pattern) {
      setStatus("제목 조건을 입력해 주세요.");
      return;
    }
    const { added, errors } = collectCheckedFilterRules(pattern);
    if (errors.length) {
      setStatus(errors[0]);
      return;
    }
    if (!added.length) {
      setStatus("적용할 항목을 하나 이상 체크해 주세요.");
      return;
    }
    const rules = loadWordFilters();
    rules.push(...added);
    saveWordFilters(rules);
    $("filterPattern").value = "";
    renderFilterTargetRows();
    renderFilterRuleList();
    const labels = added
      .map((rule) => FILTER_TARGET_LABELS[rule.target] || rule.target)
      .join(", ");
    setStatus(
      added.length === 1
        ? `단어 필터 조건을 저장했습니다. 다음 추출부터 적용됩니다.`
        : `단어 필터 ${added.length}건(${labels})을 저장했습니다. 다음 추출부터 적용됩니다.`,
    );
  });
  $("filterRuleList").addEventListener("click", (event) => {
    const btn = event.target.closest("[data-remove-rule]");
    if (!btn) return;
    const id = btn.closest("[data-rule-id]")?.dataset.ruleId;
    if (!id) return;
    saveWordFilters(loadWordFilters().filter((rule) => rule.id !== id));
    renderFilterRuleList();
  });

  btnViewTabs.addEventListener("click", () => openViewTabDialog());
  $("viewTabClose").addEventListener("click", () => viewTabDialog.close());
  $("viewTabDone").addEventListener("click", () => viewTabDialog.close());
  $("viewTabAddBtn").addEventListener("click", () => {
    const word = ($("viewTabWord").value || "").trim();
    if (!word) {
      setStatus("탭에 넣을 제목 단어를 입력해 주세요.");
      return;
    }
    const tabs = loadViewTabs();
    if (tabs.some((tab) => tab.word === word)) {
      setStatus(`이미 「${word}」 탭이 있습니다.`);
      return;
    }
    tabs.push({ id: newId(), word });
    saveViewTabs(tabs);
    $("viewTabWord").value = "";
    renderViewTabList();
    syncVisibleRowsToState();
    refreshResultView();
    setStatus(`보기 탭 「${word}」을(를) 추가했습니다.`);
  });
  $("viewTabList").addEventListener("click", (event) => {
    const btn = event.target.closest("[data-remove-view-tab]");
    if (!btn) return;
    const id = btn.closest("[data-view-tab-id]")?.dataset.viewTabId;
    if (!id) return;
    const next = loadViewTabs().filter((tab) => tab.id !== id);
    saveViewTabs(next);
    const flags = loadExportExcludeTabs();
    if (flags[id]) {
      delete flags[id];
      saveExportExcludeTabs(flags);
    }
    if (state.viewTab === id) state.viewTab = "all";
    renderViewTabList();
    syncVisibleRowsToState();
    refreshResultView();
  });
  $("viewTabWord").addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      $("viewTabAddBtn").click();
    }
  });
  viewTabBar.addEventListener("click", (event) => {
    const btn = event.target.closest("[data-view-tab]");
    if (!btn) return;
    const next = btn.dataset.viewTab;
    if (!next || next === state.viewTab) return;
    syncVisibleRowsToState();
    state.viewTab = next;
    refreshResultView();
  });

  btnExport.addEventListener("click", async () => {
    if (!state.sessionId || !state.extractReady) return;
    syncVisibleRowsToState();
    const tabs = loadViewTabs();
    const flags = loadExportExcludeTabs();
    const exportItems = state.items.filter(
      (item) =>
        item.scheduleTitle.trim() &&
        item.scheduleDate &&
        !itemsExcludedFromExport(item, tabs, flags),
    );
    const skipped = state.items.filter(
      (item) =>
        item.scheduleTitle.trim() &&
        item.scheduleDate &&
        itemsExcludedFromExport(item, tabs, flags),
    ).length;
    if (!exportItems.length) {
      setStatus(
        skipped
          ? "내보낼 일정이 없습니다. 탭 내보내기 제외 설정을 확인해 주세요."
          : "내보낼 일정이 없습니다. 제목과 날짜를 확인해 주세요.",
      );
      syncButtons();
      return;
    }
    setStatus("일정 데이터 내보내는 중…");
    btnExport.disabled = true;
    try {
      const res = await fetch("/api/export", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ sessionId: state.sessionId, items: exportItems }),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        throw new Error(data.detail || "내보내기 실패");
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = exportDownloadName(exportItems);
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      setStatus(
        skipped
          ? `내보내기 완료: ${exportItems.length}건 (탭 제외 ${skipped}건).`
          : `내보내기 완료: ${exportItems.length}건.`,
      );
    } catch (err) {
      setStatus(err.message || String(err));
    } finally {
      syncButtons();
    }
  });

  renderFilterTargetRows();
  syncButtons();
})();
