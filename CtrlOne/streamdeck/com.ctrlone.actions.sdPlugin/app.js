(() => {
  const API = "http://127.0.0.1:5177";
  const ACTION_UUID = "com.ctrlone.actions.run";
  const POLL_MS = 2500;

  let websocket = null;
  let pluginUUID = null;
  /** @type {Map<string, { context: string, actionId: string, settings: object }>} */
  const buttons = new Map();
  let online = false;
  /** @type {Array<{id:string,name:string}>} */
  let actionsCache = [];

  // Stream Deck이 플러그인 로드 시 호출
  window.connectElgatoStreamDeckSocket = (port, uuid, registerEvent, info, actionInfo) => {
    pluginUUID = uuid;
    websocket = new WebSocket(`ws://127.0.0.1:${port}`);
    websocket.onopen = () => {
      websocket.send(JSON.stringify({ event: registerEvent, uuid }));
      startPolling();
    };
    websocket.onmessage = (evt) => {
      let msg;
      try {
        msg = JSON.parse(evt.data);
      } catch {
        return;
      }
      handleMessage(msg);
    };
  };

  function handleMessage(msg) {
    const { event, context, payload, action } = msg;
    if (event === "willAppear" && action === ACTION_UUID) {
      const settings = payload?.settings || {};
      buttons.set(context, {
        context,
        actionId: settings.actionId || "",
        settings
      });
      refreshButton(context);
      return;
    }
    if (event === "willDisappear") {
      buttons.delete(context);
      return;
    }
    if (event === "keyDown" && action === ACTION_UUID) {
      runAction(context);
      return;
    }
    if (event === "didReceiveSettings" && action === ACTION_UUID) {
      const settings = payload?.settings || {};
      const btn = buttons.get(context) || { context, actionId: "", settings: {} };
      btn.settings = settings;
      btn.actionId = settings.actionId || "";
      buttons.set(context, btn);
      refreshButton(context);
      return;
    }
    if (event === "sendToPlugin") {
      // PI에서 액션 목록 요청
      if (payload?.event === "getActions") {
        sendToPropertyInspector(context, {
          event: "actionsList",
          online,
          actions: actionsCache
        });
      }
    }
  }

  function startPolling() {
    pollOnce();
    setInterval(pollOnce, POLL_MS);
  }

  async function pollOnce() {
    const wasOnline = online;
    try {
      const health = await fetchJson(`${API}/api/health`, 1200);
      online = !!(health && health.status === "ok");
      if (online) {
        const list = await fetchJson(`${API}/api/actions`, 2000);
        actionsCache = Array.isArray(list)
          ? list.map((a) => ({ id: String(a.id), name: a.name || String(a.id).slice(0, 8) }))
          : [];
      } else {
        actionsCache = [];
      }
    } catch {
      online = false;
      actionsCache = [];
    }

    if (wasOnline !== online || online) {
      for (const ctx of buttons.keys()) refreshButton(ctx);
    }
  }

  function refreshButton(context) {
    const btn = buttons.get(context);
    if (!btn) return;
    if (!online) {
      setTitle(context, "오프라인");
      setState(context, 0);
      return;
    }
    const found = actionsCache.find((a) => a.id === btn.actionId);
    if (!btn.actionId) {
      setTitle(context, "액션\n선택");
      return;
    }
    if (!found) {
      setTitle(context, "없음");
      return;
    }
    setTitle(context, wrapTitle(found.name));
  }

  async function runAction(context) {
    const btn = buttons.get(context);
    if (!btn?.actionId) {
      showAlert(context);
      return;
    }
    if (!online) {
      showAlert(context);
      return;
    }
    try {
      const res = await fetch(`${API}/api/actions/${encodeURIComponent(btn.actionId)}/run`, {
        method: "POST"
      });
      if (!res.ok) {
        showAlert(context);
        return;
      }
      showOk(context);
    } catch {
      showAlert(context);
    }
  }

  function wrapTitle(name) {
    const text = String(name || "").trim();
    if (text.length <= 10) return text;
    const mid = Math.ceil(text.length / 2);
    return `${text.slice(0, mid)}\n${text.slice(mid, mid + 12)}`;
  }

  function setTitle(context, title) {
    send({ event: "setTitle", context, payload: { title, target: 0 } });
  }

  function setState(context, state) {
    send({ event: "setState", context, payload: { state } });
  }

  function showAlert(context) {
    send({ event: "showAlert", context });
  }

  function showOk(context) {
    send({ event: "showOk", context });
  }

  function sendToPropertyInspector(context, payload) {
    send({
      event: "sendToPropertyInspector",
      context,
      payload
    });
  }

  function send(obj) {
    if (websocket && websocket.readyState === WebSocket.OPEN) {
      websocket.send(JSON.stringify(obj));
    }
  }

  async function fetchJson(url, timeoutMs) {
    const ctrl = new AbortController();
    const t = setTimeout(() => ctrl.abort(), timeoutMs);
    try {
      const res = await fetch(url, { signal: ctrl.signal, cache: "no-store" });
      if (!res.ok) throw new Error(String(res.status));
      return await res.json();
    } finally {
      clearTimeout(t);
    }
  }
})();
