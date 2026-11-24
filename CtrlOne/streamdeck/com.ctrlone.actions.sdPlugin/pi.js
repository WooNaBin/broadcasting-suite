(() => {
  let websocket = null;
  let uuid = null;
  let actionInfo = null;
  let settings = {};

  const statusEl = () => document.getElementById("status");
  const selectEl = () => document.getElementById("actionSelect");

  window.connectElgatoStreamDeckSocket = (port, propertyInspectorUUID, registerEvent, info, actionInfoJson) => {
    uuid = propertyInspectorUUID;
    try {
      actionInfo = JSON.parse(actionInfoJson);
    } catch {
      actionInfo = null;
    }
    settings = actionInfo?.payload?.settings || {};

    websocket = new WebSocket(`ws://127.0.0.1:${port}`);
    websocket.onopen = () => {
      websocket.send(JSON.stringify({ event: registerEvent, uuid }));
      requestActions();
      setInterval(requestActions, 2500);
    };
    websocket.onmessage = (evt) => {
      let msg;
      try {
        msg = JSON.parse(evt.data);
      } catch {
        return;
      }
      if (msg.event === "sendToPropertyInspector" && msg.payload?.event === "actionsList") {
        applyList(msg.payload);
      }
      if (msg.event === "didReceiveSettings") {
        settings = msg.payload?.settings || {};
        syncSelectValue();
      }
    };

    selectEl().addEventListener("change", () => {
      settings = { ...settings, actionId: selectEl().value || "" };
      saveSettings();
    });
  };

  function requestActions() {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) return;
    const context = actionInfo?.context;
    if (!context) return;
    websocket.send(
      JSON.stringify({
        event: "sendToPlugin",
        action: actionInfo?.action,
        context,
        payload: { event: "getActions" }
      })
    );
  }

  function applyList(payload) {
    const online = !!payload.online;
    const actions = Array.isArray(payload.actions) ? payload.actions : [];
    const st = statusEl();
    st.textContent = online ? `CtrlOne 연결됨 (${actions.length}개)` : "CtrlOne 오프라인 — 앱을 실행하세요";
    st.className = `status ${online ? "online" : "offline"}`;

    const sel = selectEl();
    const current = settings.actionId || "";
    sel.innerHTML = `<option value="">— 선택 —</option>`;
    for (const a of actions) {
      const opt = document.createElement("option");
      opt.value = a.id;
      opt.textContent = a.name || a.id;
      sel.appendChild(opt);
    }
    if (current && ![...sel.options].some((o) => o.value === current)) {
      const missing = document.createElement("option");
      missing.value = current;
      missing.textContent = `(삭제됨) ${current.slice(0, 8)}…`;
      sel.appendChild(missing);
    }
    sel.value = current;
    sel.disabled = !online;
  }

  function syncSelectValue() {
    const sel = selectEl();
    if (settings.actionId) sel.value = settings.actionId;
  }

  function saveSettings() {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) return;
    const context = actionInfo?.context;
    if (!context) return;
    websocket.send(
      JSON.stringify({
        event: "setSettings",
        context,
        payload: settings
      })
    );
  }
})();
