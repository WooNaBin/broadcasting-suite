(() => {
  const badge = document.getElementById("badge");
  const nasInfo = document.getElementById("nas-info");
  const setupBtn = document.getElementById("btn-setup");

  function hideInfo() {
    nasInfo.hidden = true;
    nasInfo.innerHTML = "";
  }

  function showInfo(data) {
    const rows = [
      ["NAS", data.host || ""],
      ["스케줄", data.scheduleRoot || ""],
      ["일지", data.workLogRoot || ""],
      ["미디어", data.mediaRoot || ""],
    ].filter(([, v]) => v);
    if (rows.length === 0) {
      hideInfo();
      return;
    }
    nasInfo.hidden = false;
    nasInfo.innerHTML = rows
      .map(([k, v]) => `<div><span class="k">${k}</span><span class="v">${v}</span></div>`)
      .join("");
  }

  async function refresh() {
    try {
      const res = await fetch("/api/status", { cache: "no-store" });
      const data = await res.json();
      if (data.connected) {
        badge.textContent = "NAS 연결됨";
        badge.className = "badge ok";
        showInfo(data);
      } else {
        badge.textContent = "미연결";
        badge.className = "badge bad";
        hideInfo();
      }
      return data;
    } catch (err) {
      badge.textContent = "브리지 오류";
      badge.className = "badge warn";
      hideInfo();
      return null;
    }
  }

  setupBtn.addEventListener("click", () => {
    location.href = "/setup.html";
  });

  document.querySelectorAll("[data-app]").forEach((el) => {
    el.addEventListener("click", async (event) => {
      event.preventDefault();
      const target = el.getAttribute("data-app");
      const status = await refresh();
      if (!status?.connected) {
        location.href = `/setup.html?next=${encodeURIComponent(target)}`;
        return;
      }
      window.open(target, "_blank");
    });
  });

  refresh();
  setInterval(refresh, 10000);
})();
