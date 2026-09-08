(() => {
  const badge = document.getElementById("badge");
  const detail = document.getElementById("status-detail");
  const setupBtn = document.getElementById("btn-setup");

  async function refresh() {
    try {
      const res = await fetch("/api/status", { cache: "no-store" });
      const data = await res.json();
      if (data.connected) {
        badge.textContent = "NAS 연결됨";
        badge.className = "badge ok";
        detail.textContent = [data.scheduleRoot, data.workLogRoot, data.mediaRoot]
          .filter(Boolean)
          .join(" · ");
      } else {
        badge.textContent = "미연결";
        badge.className = "badge bad";
        detail.textContent = data.lastError || "앱을 열려면 먼저 NAS 설정이 필요합니다.";
      }
      return data;
    } catch (err) {
      badge.textContent = "브리지 오류";
      badge.className = "badge warn";
      detail.textContent = String(err);
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
