/**
 * WorkLog UI 데모 — 더미 JSON만. NAS/브리지 없음.
 * index.html?demo=1 (상태창·프로필·PIN 생략 → 메인 에디터)
 */
(function () {
  const params = new URLSearchParams(location.search);
  const isDemo =
    params.get("demo") === "1" || /\/demo\.html$/i.test(location.pathname);
  if (!isDemo) return;

  window.__UI_DEMO__ = { app: "worklog" };

  function injectBanner() {
    if (document.getElementById("ui-demo-banner")) return;
    const style = document.createElement("style");
    style.textContent =
      ".ui-demo-banner{position:sticky;top:0;z-index:100000;display:flex;flex-wrap:wrap;align-items:center;gap:.55rem 1rem;padding:.55rem 1rem;background:#14181f;color:#f3f5f8;font:600 .84rem/1.35 Segoe UI,system-ui,sans-serif;border-bottom:1px solid #2a3340}" +
      ".ui-demo-banner strong{color:#ffd166}.ui-demo-banner span{opacity:.88;font-weight:500}" +
      ".ui-demo-banner a{color:#9ecbff;text-decoration:none}.ui-demo-banner a:hover{text-decoration:underline}" +
      ".ui-demo-banner .sep{opacity:.35}";
    document.head.appendChild(style);
    document.documentElement.classList.add("ui-demo-mode");
    const el = document.createElement("div");
    el.id = "ui-demo-banner";
    el.className = "ui-demo-banner";
    el.setAttribute("role", "status");
    el.innerHTML =
      "<strong>WorkLog 데모</strong><span>더미 데이터 · 로그인·PIN 생략 · 메인 에디터</span>" +
      '<a href="../../demos/">데모 허브</a><span class="sep">·</span>' +
      '<a href="index.html">실사용</a>';
    const mount = () => document.body.insertBefore(el, document.body.firstChild);
    if (document.body) mount();
    else document.addEventListener("DOMContentLoaded", mount);
  }
  injectBanner();

  const today = new Date();
  const y = today.getFullYear();
  const m = String(today.getMonth() + 1).padStart(2, "0");
  const d = String(today.getDate()).padStart(2, "0");
  const todayYmd = `${y}-${m}-${d}`;
  const monthKey = `${y}-${m}`;
  const isoNow = today.toISOString();

  function ymd(offsetDays) {
    const dt = new Date(today);
    dt.setDate(dt.getDate() + offsetDays);
    return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, "0")}-${String(dt.getDate()).padStart(2, "0")}`;
  }

  const store = { revisions: Object.create(null), files: Object.create(null) };
  function rev(path) {
    store.revisions[path] = String(Number(store.revisions[path] || 0) + 1);
    return store.revisions[path];
  }
  function getRev(path) {
    return store.revisions[path] || "0";
  }

  store.files["profiles.json"] = {
    version: 1,
    profiles: [
      { id: "ko_yunseok", displayName: "고윤석" },
      { id: "kim_jonghee", displayName: "김종희" },
      { id: "lim_wonbin", displayName: "임원빈" },
    ],
  };
  store.files["defaults.json"] = {
    version: 2,
    customTemplates: [],
    specialNotesMax: 10,
    titleRules: [
      {
        id: "rule-demo",
        conditionType: "schedule_title",
        value: "주일",
        record: "{schedule_title} {place_label} 방송 준비.",
        appendTarget: "A",
        templates: ["Sermon_Sunday_Primary"],
      },
    ],
    templateButtons: {
      Sermon_Sunday_Primary: { appendLog: "주일 말씀 방송 준비", appendTarget: "A" },
      Sermon_Wednesday: { appendLog: "수요 말씀 방송 준비", appendTarget: "A" },
    },
  };
  store.files[`work-logs/${monthKey}.json`] = {
    version: 1,
    entries: [
      {
        id: "demo-entry-wed",
        date: ymd(-2),
        log: "수요 집회 방송 준비. 중강당 믹서 점검.",
        templates: ["Sermon_Wednesday"],
        isPrinted: false,
        updatedBy: "ko_yunseok",
        updatedAt: isoNow,
      },
      {
        id: "demo-entry-1",
        date: todayYmd,
        log: "데모 일지입니다. NAS 없이 메인 에디터 UI만 확인합니다.\n금요일 오전 집회 본당 대강당 방송 준비.",
        templates: ["Sermon_Sunday_Primary"],
        isPrinted: false,
        updatedBy: "ko_yunseok",
        updatedAt: isoNow,
      },
    ],
  };
  store.files["special-notes.json"] = {
    version: 1,
    notes: [
      {
        id: "demo-note-1",
        text: "데모 특이사항: 본당 대강당 무선마이크 배터리 교체.",
        updatedAt: isoNow,
        updatedBy: "ko_yunseok",
      },
    ],
  };
  store.files["print-signs.json"] = {
    version: 4,
    names: ["김종희", "임원빈", "방송실장", "총무", "봉사회장"],
    notesCardTitle: "기타 특이사항 (장소별 방송장비, 차량, 기타 등등)",
    printCardStyle: "circle",
    emptyJournalMinLines: 4,
    emptyNotesMinLines: 4,
  };
  store.files[`audit/${monthKey}.json`] = {
    version: 1,
    items: [
      {
        ts: isoNow,
        actor: "ko_yunseok",
        action: "update",
        entryId: "demo-entry-1",
        date: todayYmd,
        summary: "데모 감사 로그",
      },
    ],
  };
  Object.keys(store.files).forEach((k) => {
    store.revisions[k] = "1";
  });

  const locks = Object.create(null);
  const scheduleList = [
    {
      scheduleDate: ymd(-2),
      startTime: 1930,
      scheduleTitle: "수요 찬양 집회",
      place: "Main_Medium",
      sermonSpeaker: "김은혜",
      preparation: ["Praise"],
      accessoryInfo: "",
    },
    {
      scheduleDate: todayYmd,
      startTime: 1030,
      scheduleTitle: "금요일 오전 집회",
      place: "Main_Large",
      sermonSpeaker: "이준호",
      preparation: ["Recording", "Praise", "Streaming"],
      accessoryInfo: "데모",
    },
    {
      scheduleDate: ymd(2),
      startTime: 1030,
      scheduleTitle: "주일 예배",
      place: "Main_Large",
      sermonSpeaker: "박성민",
      preparation: ["Recording", "Streaming", "Praise"],
      accessoryInfo: "1부",
    },
  ];

  function jsonResponse(data, status, headers) {
    const h = new Headers(headers || {});
    if (!h.has("Content-Type")) h.set("Content-Type", "application/json");
    return new Response(JSON.stringify(data), { status: status || 200, headers: h });
  }

  function textResponse(text, status, headers) {
    const h = new Headers(headers || {});
    if (!h.has("Content-Type")) h.set("Content-Type", "application/json");
    return new Response(text, { status: status || 200, headers: h });
  }

  function parseUrl(input) {
    try {
      return new URL(String(input), location.origin);
    } catch {
      return null;
    }
  }

  function apiPath(u) {
    if (!u) return "";
    const p = u.pathname;
    const idx = p.indexOf("/api/");
    if (idx >= 0) return p.slice(idx);
    if (p.startsWith("/api/")) return p;
    return "";
  }

  const realFetch = window.fetch.bind(window);
  window.fetch = async function demoFetch(input, init) {
    const u = parseUrl(input);
    const path = apiPath(u);
    if (!path) return realFetch(input, init);

    const method = String((init && init.method) || "GET").toUpperCase();

    if (path === "/api/status") {
      return jsonResponse({
        connected: true,
        via: "UI-Demo",
        nasConnected: true,
        root: "demo://work-log",
        workLogRoot: "demo://work-log",
        scheduleRoot: "demo://schedule",
      });
    }
    if (path === "/api/connect" || path === "/api/ensure-layout" || path === "/api/disconnect") {
      return jsonResponse({ ok: true });
    }
    if (path === "/api/session/ping" || path === "/api/session/bye") {
      return jsonResponse({ ok: true });
    }

    if (path === "/api/file") {
      const rel = (u && u.searchParams.get("path")) || "";
      if (method === "GET") {
        if (!(rel in store.files)) return jsonResponse({ message: "없음" }, 404);
        return textResponse(JSON.stringify(store.files[rel]), 200, {
          "X-File-Revision": getRev(rel),
        });
      }
      if (method === "PUT") {
        const expected =
          (init &&
            init.headers &&
            (init.headers["X-Expected-Revision"] || init.headers.get?.("X-Expected-Revision"))) ||
          null;
        if (expected != null && String(expected) !== getRev(rel) && getRev(rel) !== "0") {
          return jsonResponse(
            { message: "충돌", revision: getRev(rel), content: store.files[rel] },
            409,
          );
        }
        const raw = init && init.body != null ? String(init.body) : "null";
        store.files[rel] = raw === "null" || raw.trim() === "" ? null : JSON.parse(raw);
        return jsonResponse({ ok: true, revision: rev(rel) });
      }
    }

    if (path.startsWith("/api/audit/append")) {
      const month = (u && u.searchParams.get("month")) || monthKey;
      const key = `audit/${month}.json`;
      if (!store.files[key]) store.files[key] = { version: 1, items: [] };
      const payload = init && init.body ? JSON.parse(String(init.body)) : {};
      store.files[key].items = store.files[key].items || [];
      store.files[key].items.push(payload);
      rev(key);
      return jsonResponse({ ok: true });
    }
    if (path === "/api/audit" || path.startsWith("/api/audit")) {
      const month = (u && u.searchParams.get("month")) || monthKey;
      const key = `audit/${month}.json`;
      const data = store.files[key] || { version: 1, items: [] };
      const items = Array.isArray(data.items) ? data.items : [];
      return new Response(items.map((row) => JSON.stringify(row)).join("\n"), {
        status: 200,
        headers: { "Content-Type": "text/plain; charset=utf-8" },
      });
    }

    if (path.startsWith("/api/edit-lock")) {
      if (path.includes("acquire")) {
        const body = init && init.body ? JSON.parse(String(init.body)) : {};
        const date = body.date || (u && u.searchParams.get("date")) || "";
        locks[date] = {
          actorId: body.actorId || "demo",
          actorName: body.actorName || "데모",
          sessionId: body.sessionId || "demo-session",
        };
        return jsonResponse({ ok: true, holder: locks[date] });
      }
      if (path.includes("heartbeat")) return jsonResponse({ ok: true });
      if (path.includes("release")) {
        const body = init && init.body ? JSON.parse(String(init.body || "{}")) : {};
        if (body.date) delete locks[body.date];
        return jsonResponse({ ok: true });
      }
      const date = (u && u.searchParams.get("date")) || "";
      return jsonResponse({ holder: locks[date] || null });
    }

    if (path === "/api/schedule/files") return jsonResponse(["demo-2026.json"]);
    if (path.startsWith("/api/schedule/file")) {
      return jsonResponse({ version: 1, schedules: scheduleList });
    }

    return jsonResponse({ ok: true, demo: true });
  };
})();
