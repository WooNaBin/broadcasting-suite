(() => {
  const hello = () => fetch("/api/session/hello", { method: "POST", keepalive: true }).catch(() => {});
  const goodbye = () => {
    try {
      navigator.sendBeacon("/api/goodbye", new Blob([], { type: "application/json" }));
    } catch {
      fetch("/api/goodbye", { method: "POST", keepalive: true }).catch(() => {});
    }
  };

  hello();
  setInterval(() => {
    fetch("/api/session/ping", { method: "POST", keepalive: true }).catch(() => {});
  }, 15000);

  window.addEventListener("pagehide", goodbye);
  window.addEventListener("beforeunload", goodbye);
})();
