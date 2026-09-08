(() => {
  const form = document.getElementById("setup-form");
  const msg = document.getElementById("setup-msg");
  const params = new URLSearchParams(location.search);
  const next = params.get("next") || "/";

  function show(text, ok) {
    msg.hidden = false;
    msg.textContent = text;
    msg.className = ok ? "msg ok" : "msg";
  }

  async function load() {
    const res = await fetch("/api/setup", { cache: "no-store" });
    const data = await res.json();
    const cfg = data.config || {};
    for (const [key, value] of Object.entries(cfg)) {
      const input = form.elements.namedItem(key);
      if (!input) continue;
      if (input.type === "checkbox") input.checked = !!value;
      else input.value = value ?? "";
    }
  }

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

  load().catch((err) => show(String(err), false));
})();
