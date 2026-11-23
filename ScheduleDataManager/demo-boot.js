/**
 * SDM UI 데모 — 더미 JSON만. NAS/브리지 없음.
 * index.html?demo=1 (로그인 생략 → 일정 보드)
 */
(function () {
  const params = new URLSearchParams(location.search);
  const isDemo =
    params.get("demo") === "1" || /\/demo\.html$/i.test(location.pathname);
  if (!isDemo) return;

  window.__UI_DEMO__ = { app: "schedule" };

  try {
    localStorage.removeItem("schedule-data-manager-nas-dirty");
    localStorage.removeItem("schedule-data-manager-color-rules");
  } catch {
    /* ignore */
  }

  function ymdFromDate(d) {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    return `${y}-${m}-${day}`;
  }

  // 이번 주 일~토 (이어진 일정이 주 경계에서 끊기지 않게)
  function weekDay(dow) {
    const d = new Date();
    d.setHours(12, 0, 0, 0);
    d.setDate(d.getDate() - d.getDay() + dow);
    return ymdFromDate(d);
  }

  const isoNow = new Date().toISOString();
  const sun = weekDay(0);
  const mon = weekDay(1);
  const tue = weekDay(2);
  const wed = weekDay(3);
  const thu = weekDay(4);
  const fri = weekDay(5);
  const sat = weekDay(6);
  const nextSun = weekDay(7);

  const files = Object.create(null);
  const revisions = Object.create(null);

  function schedule(partial) {
    const date = partial.scheduleDate;
    return {
      accessoryInfo: "",
      place: "Main_Large",
      sermonSpeaker: "",
      preparation: [],
      preaparationNote: "",
      colorMode: "auto",
      color: "auto",
      ...partial,
      releaseDate: partial.releaseDate || date,
    };
  }

  files["demo-2026.json"] = [
    schedule({
      id: "demo-sun",
      scheduleDate: sun,
      startTime: 1030,
      scheduleTitle: "주일 예배",
      accessoryInfo: "2부",
      sermonSpeaker: "홍길동",
      preparation: ["Recording", "Streaming"],
      preaparationNote: "송출·녹화 상태 확인",
    }),
    schedule({
      id: "demo-camp-mon",
      scheduleDate: mon,
      startTime: 1000,
      scheduleTitle: "하계기도회",
      place: "Edu_Large",
      sermonSpeaker: "김은혜",
      preparation: ["Recording"],
      preaparationNote: "월~수 이어진 일정 · 칸 클릭·구분선 확인용",
    }),
    schedule({
      id: "demo-camp-tue",
      scheduleDate: tue,
      startTime: 1000,
      scheduleTitle: "하계기도회",
      place: "Edu_Large",
      sermonSpeaker: "김은혜",
      preparation: ["Recording"],
    }),
    schedule({
      id: "demo-camp-wed",
      scheduleDate: wed,
      startTime: 1000,
      scheduleTitle: "하계기도회",
      place: "Edu_Large",
      sermonSpeaker: "김은혜",
      preparation: ["Recording"],
    }),
    schedule({
      id: "demo-wed-evening",
      scheduleDate: wed,
      startTime: 1930,
      scheduleTitle: "수요 찬양 집회",
      place: "Main_Medium",
      sermonSpeaker: "김은혜",
      preparation: ["Praise"],
    }),
    schedule({
      id: "demo-thu-none",
      scheduleDate: thu,
      startTime: 1400,
      scheduleTitle: "방송실 점검",
      place: "Etc",
      preparation: [],
      colorMode: "none",
      color: "none",
      preaparationNote: "색상 없음 예시",
    }),
    schedule({
      id: "demo-fri",
      scheduleDate: fri,
      startTime: 1030,
      scheduleTitle: "금요일 오전 집회",
      accessoryInfo: "데모",
      sermonSpeaker: "이준호",
      preparation: ["Recording", "Praise", "Streaming"],
      preaparationNote: "본당 대강당 방송 준비",
      colorMode: "custom",
      color: "green",
    }),
    schedule({
      id: "demo-workshop-fri",
      scheduleDate: fri,
      startTime: 1400,
      scheduleTitle: "청년 리더십 워크숍",
      accessoryInfo: "2일 과정",
      place: "Edu_Large",
      preparation: ["OtherDisplay"],
      preaparationNote: "프로젝터·안내 배너",
    }),
    schedule({
      id: "demo-workshop-sat",
      scheduleDate: sat,
      startTime: 1400,
      scheduleTitle: "청년 리더십 워크숍",
      accessoryInfo: "2일 과정",
      place: "Edu_Large",
      preparation: ["OtherDisplay"],
    }),
    schedule({
      id: "demo-sun2",
      scheduleDate: nextSun,
      startTime: 1030,
      scheduleTitle: "주일 예배",
      accessoryInfo: "1부",
      sermonSpeaker: "박성민",
      preparation: ["Recording", "Streaming", "Praise"],
    }),
  ];

  files["_schedule-colors.json"] = {
    version: 1,
    updatedAt: isoNow,
    rules: [
      { id: "rule-sunday", pattern: "주일", color: "orange" },
      { id: "rule-pray", pattern: "기도", color: "teal" },
      { id: "rule-workshop", pattern: "워크숍 or 리더십", color: "blue" },
      { id: "rule-wed", pattern: "수요 and 찬양", color: "coral" },
    ],
  };

  files["_schedule-presets.json"] = {
    version: 1,
    updatedAt: isoNow,
    presets: [
      {
        id: "preset-sunday",
        name: "주일 예배",
        fields: ["startTime", "scheduleTitle", "place", "preparation", "color"],
        values: {
          startTime: 1030,
          scheduleTitle: "주일 예배",
          place: "Main_Large",
          preparation: ["Recording", "Streaming"],
          colorMode: "auto",
          color: "auto",
        },
      },
      {
        id: "preset-wed",
        name: "수요 집회",
        fields: ["startTime", "scheduleTitle", "place", "color"],
        values: {
          startTime: 1930,
          scheduleTitle: "수요 찬양 집회",
          place: "Main_Medium",
          colorMode: "auto",
          color: "auto",
        },
      },
      {
        id: "preset-none",
        name: "색 없음",
        fields: ["color"],
        values: {
          colorMode: "none",
          color: "none",
        },
      },
    ],
  };

  files["_speaker-roster.json"] = {
    version: 1,
    updatedAt: isoNow,
    gwangju: ["홍길동", "김은혜", "이준호", "박성민"],
  };

  files["_shared_notice.json"] = {
    text: "데모 공유사항입니다. NAS 없이 일정 보드 UI만 확인합니다.",
    updatedAt: isoNow,
    lock: null,
    history: [
      {
        text: "데모 공유사항입니다. NAS 없이 일정 보드 UI만 확인합니다.",
        updatedAt: isoNow,
      },
    ],
  };

  Object.keys(files).forEach((k) => {
    revisions[k] = "1";
  });

  function injectBanner() {
    if (document.getElementById("ui-demo-banner")) return;
    const style = document.createElement("style");
    style.textContent =
      ".ui-demo-banner{position:sticky;top:0;z-index:100000;display:flex;flex-wrap:wrap;align-items:center;gap:.55rem 1rem;padding:.55rem 1rem;background:#14181f;color:#f3f5f8;font:600 .84rem/1.35 Segoe UI,system-ui,sans-serif;border-bottom:1px solid #2a3340}" +
      ".ui-demo-banner strong{color:#ffd166}.ui-demo-banner span{opacity:.88;font-weight:500}" +
      ".ui-demo-banner a{color:#9ecbff;text-decoration:none}.ui-demo-banner a:hover{text-decoration:underline}" +
      ".ui-demo-banner .sep{opacity:.35}";
    document.head.appendChild(style);
    const el = document.createElement("div");
    el.id = "ui-demo-banner";
    el.className = "ui-demo-banner";
    el.setAttribute("role", "status");
    el.innerHTML =
      "<strong>스케줄 데모</strong><span>더미 데이터 · 로그인 생략 · 일정 보드</span>" +
      '<a href="../demos/">데모 허브</a><span class="sep">·</span>' +
      '<a href="index.html">실사용</a>';
    const mount = () => document.body.insertBefore(el, document.body.firstChild);
    if (document.body) mount();
    else document.addEventListener("DOMContentLoaded", mount);
  }
  injectBanner();

  function json(data, status, extraHeaders) {
    const headers = { "Content-Type": "application/json" };
    if (extraHeaders) Object.assign(headers, extraHeaders);
    return new Response(JSON.stringify(data), { status: status || 200, headers });
  }

  function fileNameFromUrl(url) {
    try {
      const u = new URL(url, location.origin);
      return u.searchParams.get("name") || u.searchParams.get("path") || "";
    } catch {
      return "";
    }
  }

  const realFetch = window.fetch.bind(window);
  window.fetch = async function (input, init) {
    const url = String(input);
    const method = String((init && init.method) || "GET").toUpperCase();
    if (!url.includes("/api/")) return realFetch(input, init);

    if (url.includes("/api/status")) {
      return json({
        connected: true,
        root: "demo://schedule",
        via: "UI-Demo",
        nasConnected: true,
      });
    }
    if (url.includes("/api/connect") || url.includes("/api/disconnect")) {
      return json({ ok: true, root: "demo://schedule" });
    }
    if (url.includes("/api/files")) {
      return json(["demo-2026.json"]);
    }
    if (url.includes("/api/file")) {
      const name = fileNameFromUrl(url) || "demo-2026.json";
      if (method === "GET") {
        if (!(name in files)) return json({ message: "없음" }, 404);
        return json(files[name], 200, { "X-File-Revision": revisions[name] || "1" });
      }
      if (method === "PUT" || method === "POST") {
        try {
          files[name] = JSON.parse(String(init.body || "null"));
        } catch {
          /* keep */
        }
        revisions[name] = String(Number(revisions[name] || 0) + 1);
        return json({ ok: true, revision: revisions[name] });
      }
    }
    if (url.includes("/api/lan-devices")) return json([]);
    if (url.includes("/api/document") || url.includes("/api/documents")) return json([]);
    if (url.includes("/api/backup") || url.includes("/api/backups") || url.includes("/api/restore")) {
      return json({ ok: true });
    }
    if (url.includes("/api/active-schedule-file") || url.includes("/api/session/")) {
      return json({ ok: true });
    }
    return json({ ok: true, demo: true });
  };

  if ("serviceWorker" in navigator) {
    navigator.serviceWorker.register = function () {
      return Promise.reject(new Error("demo: sw disabled"));
    };
  }
})();
