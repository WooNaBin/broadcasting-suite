/**
 * FileChecker UI 데모 — 더미 목록만. NAS/브리지 없음.
 * index.html?demo=1 (로그인 없음 · 작업 목록)
 */
(function () {
  const params = new URLSearchParams(location.search);
  const isDemo =
    params.get("demo") === "1" || /\/demo\.html$/i.test(location.pathname);
  if (!isDemo) return;

  window.__UI_DEMO__ = { app: "files" };

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
      "<strong>FileChecker 데모</strong><span>더미 목록 · 메인 작업 목록부터</span>" +
      '<a href="../../demos/">데모 허브</a><span class="sep">·</span>' +
      '<a href="index.html">실사용</a>';
    const mount = () => document.body.insertBefore(el, document.body.firstChild);
    if (document.body) mount();
    else document.addEventListener("DOMContentLoaded", mount);
  }
  injectBanner();

  const today = new Date();
  const iso = (dt) => {
    const y = dt.getFullYear();
    const m = String(dt.getMonth() + 1).padStart(2, "0");
    const d = String(dt.getDate()).padStart(2, "0");
    return `${y}-${m}-${d}`;
  };
  const addDays = (n) => {
    const d = new Date(today);
    d.setDate(d.getDate() + n);
    return iso(d);
  };

  let schedules = [
    {
      id: "fc-demo-1",
      date: addDays(-5),
      description: "주일예배 2부",
      place: "Main_Large",
      memo: "",
      hasInstructor: true,
      instructor: "홍길동",
      specialSong: true,
      youTube: true,
      baseFileName: "20260906_주일예배2부_본당대강당",
      localAudioFound: true,
      localVideoFound: true,
      localHighQualityFound: false,
      nasAudioFound: true,
      nasVideoFound: false,
      nasHighQualityFound: false,
      localAudioPath: "D:\\Demo\\audio.wav",
      localVideoPath: "D:\\Demo\\video.mp4",
      nasAudioPath: "\\\\NAS\\demo\\audio.wav",
    },
    {
      id: "fc-demo-2",
      date: addDays(-2),
      description: "수요 찬양 집회",
      place: "Main_Medium",
      memo: "",
      hasInstructor: true,
      instructor: "김은혜",
      specialSong: true,
      youTube: false,
      baseFileName: "20260909_수요찬양집회_본당중강당",
      localAudioFound: true,
      localVideoFound: false,
      localHighQualityFound: false,
      nasAudioFound: false,
      nasVideoFound: false,
      nasHighQualityFound: false,
      localAudioPath: "D:\\Demo\\wed-a.wav",
    },
    {
      id: "fc-demo-3",
      date: addDays(0),
      description: "금요일 오전 집회",
      place: "Main_Large",
      memo: "데모",
      hasInstructor: true,
      instructor: "이준호",
      specialSong: true,
      youTube: true,
      baseFileName: "20260911_금요일오전형집회_본당대강당",
      localAudioFound: false,
      localVideoFound: false,
      localHighQualityFound: false,
      nasAudioFound: false,
      nasVideoFound: false,
      nasHighQualityFound: false,
    },
    {
      id: "fc-demo-4",
      date: addDays(-6),
      description: "청년 모임 녹화",
      place: "Edu_Large",
      memo: "완료 샘플",
      hasInstructor: false,
      instructor: null,
      specialSong: false,
      youTube: true,
      baseFileName: "20260905_청년모임_교육원대강당",
      localAudioFound: true,
      localVideoFound: true,
      localHighQualityFound: true,
      nasAudioFound: true,
      nasVideoFound: true,
      nasHighQualityFound: true,
      localAudioPath: "D:\\Demo\\youth-a.wav",
      localVideoPath: "D:\\Demo\\youth-v.mp4",
      localHighQualityPath: "D:\\Demo\\youth-hq.mp4",
      nasAudioPath: "\\\\NAS\\demo\\youth-a.wav",
      nasVideoPath: "\\\\NAS\\demo\\youth-v.mp4",
      nasHighQualityPath: "\\\\NAS\\demo\\youth-hq.mp4",
    },
  ];

  let settings = {
    localPath: "D:\\Demo\\Render",
    nasIp: "192.168.0.10",
    nasSearchPath: "Permanent DATA\\H264_mp4 DATA",
    nasUsername: "demo",
    nasPassword: "",
    scheduleJsonPath: "",
  };

  function json(data, status) {
    return new Response(JSON.stringify(data), {
      status: status || 200,
      headers: { "Content-Type": "application/json" },
    });
  }

  const realFetch = window.fetch.bind(window);
  window.fetch = async function (input, init) {
    const url = String(input);
    const method = String((init && init.method) || "GET").toUpperCase();
    if (!url.includes("/api/")) return realFetch(input, init);

    if (
      url.includes("/api/schedules") &&
      method === "GET" &&
      !url.includes("export") &&
      !url.includes("import") &&
      !url.includes("sync")
    ) {
      return json(schedules);
    }
    if (
      url.includes("/api/schedules") &&
      method === "POST" &&
      !url.includes("import") &&
      !url.includes("sync")
    ) {
      const body = init && init.body ? JSON.parse(String(init.body)) : {};
      const item = {
        id: "fc-demo-" + Date.now(),
        baseFileName: (body.date || "") + "_" + (body.description || "demo"),
        ...body,
      };
      schedules = [item, ...schedules];
      return json(item);
    }
    if (url.includes("/api/schedules/export")) return json(schedules);
    if (url.includes("/api/schedules/import") || url.includes("/api/schedules/sync")) {
      return json({ imported: 0, message: "데모: 가져오기 생략" });
    }
    if (url.includes("/api/settings")) {
      if (method === "GET") return json(settings);
      if (method === "PUT" || method === "POST") {
        try {
          settings = { ...settings, ...JSON.parse(String(init.body || "{}")) };
        } catch {
          /* keep */
        }
        return json(settings);
      }
    }
    if (url.includes("/api/nas") || url.includes("nas-test")) {
      return json({ ok: true, message: "데모: NAS 테스트 성공으로 표시" });
    }
    if (url.includes("/api/select-folder") || url.includes("folder")) {
      return json({ path: settings.localPath });
    }
    return json({ ok: true, demo: true });
  };
})();
