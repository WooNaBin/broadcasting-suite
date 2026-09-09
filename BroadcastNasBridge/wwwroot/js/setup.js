(() => {
  const form = document.getElementById("setup-form");
  const msg = document.getElementById("setup-msg");
  const hostInput = document.getElementById("host");
  const scanBtn = document.getElementById("btn-scan");
  const deviceList = document.getElementById("device-list");
  const params = new URLSearchParams(location.search);
  const next = params.get("next") || "/";

  function show(text, ok) {
    msg.hidden = false;
    msg.textContent = text;
    msg.className = ok ? "msg ok" : "msg";
  }

  function hostMode() {
    const checked = form.querySelector('input[name="hostMode"]:checked');
    return checked ? checked.value : "manual";
  }

  function syncModeUi() {
    const scan = hostMode() === "scan";
    scanBtn.hidden = !scan;
    if (!scan) {
      deviceList.hidden = true;
      deviceList.innerHTML = "";
    }
  }

  async function scanDevices() {
    deviceList.hidden = false;
    deviceList.innerHTML = "<li class=\"muted\">검색 중… (잠시 걸릴 수 있습니다)</li>";
    scanBtn.disabled = true;
    try {
      const res = await fetch("/api/lan-devices", { cache: "no-store" });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "검색 실패");
      const list = Array.isArray(data) ? data : [];
      if (list.length === 0) {
        deviceList.innerHTML = "<li class=\"muted\">발견된 기기가 없습니다. 직접 입력해 보세요.</li>";
        return;
      }
      deviceList.innerHTML = "";
      for (const d of list) {
        const li = document.createElement("li");
        const btn = document.createElement("button");
        btn.type = "button";
        const name = d.displayName || d.hostname || d.ip;
        btn.innerHTML = `<span>${name}</span><span class="meta">${d.ip}${d.smbOpen ? " · SMB" : ""}</span>`;
        btn.addEventListener("click", () => {
          hostInput.value = d.ip;
          show(`${name} (${d.ip}) 선택됨`, true);
        });
        li.appendChild(btn);
        deviceList.appendChild(li);
      }
    } catch (err) {
      deviceList.innerHTML = "";
      show(err.message || String(err), false);
    } finally {
      scanBtn.disabled = false;
    }
  }

  async function load() {
    const res = await fetch("/api/setup", { cache: "no-store" });
    const data = await res.json();
    const cfg = data.config || {};
    for (const [key, value] of Object.entries(cfg)) {
      const input = form.elements.namedItem(key);
      if (!input || key === "host") continue;
      if (input.type === "checkbox") input.checked = !!value;
      else input.value = value ?? "";
    }
    if (cfg.host) hostInput.value = cfg.host;
  }

  form.querySelectorAll('input[name="hostMode"]').forEach((el) => {
    el.addEventListener("change", () => {
      syncModeUi();
      if (hostMode() === "scan") scanDevices();
    });
  });
  scanBtn.addEventListener("click", () => scanDevices());

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const fd = new FormData(form);
    const body = {
      host: fd.get("host"),
      username: fd.get("username"),
      password: fd.get("password"),
      rememberPassword: fd.get("rememberPassword") === "on",
      tempShare: fd.get("tempShare"),
      permanentShare: fd.get("permanentShare"),
      scheduleRelativePath: fd.get("scheduleRelativePath"),
      workLogRelativePath: fd.get("workLogRelativePath"),
      mediaRelativePath: fd.get("mediaRelativePath"),
      localPath: fd.get("localPath") || "",
      scheduleJsonFile: fd.get("scheduleJsonFile") || null,
    };
    show("연결 중…", true);
    try {
      const res = await fetch("/api/setup", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "연결 실패");
      show("연결되었습니다. 이동합니다…", true);
      setTimeout(() => { location.href = next; }, 500);
    } catch (err) {
      show(err.message || String(err), false);
    }
  });

  syncModeUi();
  load().catch((err) => show(String(err), false));
})();
