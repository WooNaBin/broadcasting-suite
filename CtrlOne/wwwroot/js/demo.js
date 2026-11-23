(() => {
  const PRESET_KEY = "ctrlone-demo-presets";
  const DEMO_POOL = [
    {
      address: "192.168.10.51",
      model: "HyperDeck Studio Mini",
      suggestedName: "Cam_A_Mini",
      protocolVersion: "1.12",
    },
    {
      address: "192.168.10.52",
      model: "HyperDeck Studio HD Mini",
      suggestedName: "Cam_B_HD",
      protocolVersion: "1.12",
    },
    {
      address: "192.168.10.53",
      model: "HyperDeck Extreme 8K",
      suggestedName: "Replay_01",
      protocolVersion: "1.13",
    },
  ];

  const state = {
    devices: createSeedDevices(),
    discovered: [],
    presets: loadPresets(),
  };

  const els = {
    grid: document.getElementById("device-grid"),
    selectionList: document.getElementById("selection-list"),
    presetList: document.getElementById("preset-list"),
    logView: document.getElementById("log-view"),
    addDialog: document.getElementById("add-dialog"),
    renameDialog: document.getElementById("rename-dialog"),
    presetDialog: document.getElementById("preset-dialog"),
    renameForm: document.getElementById("rename-form"),
    presetForm: document.getElementById("preset-form"),
    renameId: document.getElementById("rename-id"),
    renameName: document.getElementById("rename-name"),
    presetName: document.getElementById("preset-name"),
    scanBtn: document.getElementById("btn-scan"),
    scanStatus: document.getElementById("scan-status"),
    scanSubnets: document.getElementById("scan-subnets"),
    discoverList: document.getElementById("discover-list"),
    addSelectedBtn: document.getElementById("btn-add-selected"),
  };

  document.getElementById("btn-add-device").addEventListener("click", () => {
    resetDiscoverUi();
    els.addDialog.showModal();
    runFakeScan();
  });
  document.getElementById("btn-batch-record").addEventListener("click", batchRecord);
  document.getElementById("btn-batch-stop").addEventListener("click", batchStop);
  document.getElementById("btn-save-preset").addEventListener("click", () => {
    els.presetName.value = "";
    els.presetDialog.showModal();
    els.presetName.focus();
  });
  document.getElementById("add-cancel").addEventListener("click", () => els.addDialog.close());
  document.getElementById("rename-cancel").addEventListener("click", () =>
    els.renameDialog.close("cancel")
  );
  document.getElementById("preset-cancel").addEventListener("click", () =>
    els.presetDialog.close("cancel")
  );
  els.scanBtn.addEventListener("click", () => runFakeScan());
  els.addSelectedBtn.addEventListener("click", () => addSelectedDevices());

  els.renameDialog.addEventListener("close", () => {
    if (els.renameDialog.returnValue !== "ok") return;
    const id = els.renameId.value;
    const name = els.renameName.value.trim();
    const device = state.devices.find((d) => d.id === id);
    if (!device || !name) return;
    device.name = name;
    appendLog(`[데모] 이름 변경 → ${name}`);
    render();
  });

  els.presetDialog.addEventListener("close", () => {
    if (els.presetDialog.returnValue !== "ok") return;
    const name = els.presetName.value.trim();
    if (!name) return;
    const ids = state.devices.filter((d) => d.isSelected).map((d) => d.id);
    if (!ids.length) {
      appendLog("[데모] 선택된 장비가 없어 프리셋을 저장하지 않았습니다.");
      return;
    }
    const existing = state.presets.find(
      (p) => p.name.toLowerCase() === name.toLowerCase()
    );
    if (existing) {
      existing.deviceIds = ids;
    } else {
      state.presets.push({ id: `preset-${Date.now()}`, name, deviceIds: ids });
    }
    savePresets();
    appendLog(`[데모] 프리셋 저장: ${name} (${ids.length}대)`);
    renderPresets();
  });

  appendLog("[데모] 레이아웃 미리보기 모드입니다. HyperDeck TCP 연결은 사용하지 않습니다.");
  appendLog("[데모] 가상 장비 4대가 Connected 상태로 표시됩니다.");
  render();
  startDemoTick();

  function createSeedDevices() {
    const gb = (n) => Math.round(n * 1024 * 1024 * 1024);
    const caps = (partial) => ({
      model: "",
      slotCount: 2,
      canPlay: true,
      canStop: true,
      canRecord: true,
      canPreview: false,
      canSelectSlot: true,
      canRecordSpill: false,
      canStream: false,
      capabilitiesResolved: true,
      ...partial,
    });
    return [
      {
        id: "demo-1",
        name: "Mini_Camera_01",
        address: "192.168.10.21",
        order: 0,
        isSelected: true,
        isConnected: true,
        transportStatus: "preview",
        isPreviewMode: true,
        isStreaming: false,
        spillStatus: "idle",
        spillMessage: "",
        activeSlotId: 1,
        supportsSlotSelect: true,
        capabilities: caps({
          model: "HyperDeck Studio Mini",
          canPreview: true,
          canRecordSpill: true,
        }),
        slots: [
          slot(1, "mounted", "02:45:12", true, gb(180), gb(512)),
          slot(2, "mounted", "01:10:08", false, gb(90), gb(512)),
        ],
        slot1: slot(1, "mounted", "02:45:12", true, gb(180), gb(512)),
        slot2: slot(2, "mounted", "01:10:08", false, gb(90), gb(512)),
      },
      {
        id: "demo-2",
        name: "Mini_Camera_02",
        address: "192.168.10.22",
        order: 1,
        isSelected: true,
        isConnected: true,
        transportStatus: "record",
        isPreviewMode: false,
        isStreaming: false,
        activeSlotId: 1,
        supportsSlotSelect: true,
        capabilities: caps({ model: "HyperDeck Studio HD Mini" }),
        slots: [
          slot(1, "mounted", "01:58:40", true, gb(95), gb(256)),
          slot(2, "empty", "--:--:--", false, 0, 0),
        ],
        slot1: slot(1, "mounted", "01:58:40", true, gb(95), gb(256)),
        slot2: slot(2, "empty", "--:--:--", false, 0, 0),
      },
      {
        id: "demo-3",
        name: "Studio_Rack_A",
        address: "192.168.10.30",
        order: 2,
        isSelected: true,
        isConnected: true,
        transportStatus: "play",
        isPreviewMode: false,
        isStreaming: true,
        activeSlotId: 1,
        supportsSlotSelect: true,
        capabilities: caps({
          model: "HyperDeck Extreme 8K + Stream",
          slotCount: 3,
          canStream: true,
          canPreview: true,
          canRecordSpill: true,
        }),
        slots: [
          slot(1, "mounted", "04:12:55", true, gb(40), gb(1024)),
          slot(2, "mounted", "04:12:55", false, gb(800), gb(1024)),
          slot(3, "mounted", "00:45:10", false, gb(200), gb(512)),
        ],
        slot1: slot(1, "mounted", "04:12:55", true, gb(40), gb(1024)),
        slot2: slot(2, "mounted", "04:12:55", false, gb(800), gb(1024)),
      },
      {
        id: "demo-4",
        name: "Probing_Deck",
        address: "192.168.10.41",
        order: 3,
        isSelected: true,
        isConnected: true,
        transportStatus: "unknown",
        isPreviewMode: false,
        isStreaming: false,
        activeSlotId: 1,
        supportsSlotSelect: false,
        capabilities: caps({
          model: "",
          canPlay: false,
          canStop: false,
          canRecord: false,
          canSelectSlot: false,
          capabilitiesResolved: false,
        }),
        slots: [
          slot(1, "mounted", "--:--:--", true, gb(10), gb(128)),
          slot(2, "empty", "--:--:--", false, 0, 0),
        ],
        slot1: slot(1, "mounted", "--:--:--", true, gb(10), gb(128)),
        slot2: slot(2, "empty", "--:--:--", false, 0, 0),
      },
    ];
  }

  function slot(slotId, status, recordingTime, isActive, remainingBytes, totalBytes) {
    const freePercent = totalBytes > 0 ? (remainingBytes * 100) / totalBytes : 0;
    return {
      slotId,
      status,
      recordingTime,
      isActive,
      remainingBytes,
      totalBytes,
      freePercent,
      capacityLabel:
        totalBytes > 0
          ? `${formatBytes(remainingBytes)} / ${formatBytes(totalBytes)}`
          : remainingBytes > 0
            ? `${formatBytes(remainingBytes)}`
            : "용량 정보 없음",
    };
  }

  function formatBytes(bytes) {
    const gb = 1024 ** 3;
    const mb = 1024 ** 2;
    if (bytes >= gb) return `${(bytes / gb).toFixed(1)} GB`;
    if (bytes >= mb) return `${(bytes / mb).toFixed(0)} MB`;
    return `${bytes} B`;
  }

  function loadPresets() {
    try {
      return JSON.parse(localStorage.getItem(PRESET_KEY) || "[]");
    } catch {
      return [];
    }
  }

  function savePresets() {
    localStorage.setItem(PRESET_KEY, JSON.stringify(state.presets));
  }

  function isRecording(status) {
    const value = String(status || "").trim().toLowerCase();
    return value === "record" || value === "recording";
  }

  function render() {
    renderSelection();
    renderPresets();
    renderGrid();
  }

  function renderSelection() {
    els.selectionList.innerHTML = state.devices
      .map((d) => {
        const probing = d.isConnected && !d.capabilities?.capabilitiesResolved;
        let tag = "";
        let extraClass = "";
        if (!d.isConnected) {
          tag = `<span class="offline-tag">OFF</span>`;
          extraClass = "offline";
        } else if (probing) {
          tag = `<span class="probe-tag">확인중</span>`;
          extraClass = "probing";
        }
        return `
      <label class="selection-item ${extraClass}">
        <input type="checkbox" data-select="${d.id}" ${d.isSelected ? "checked" : ""} />
        <span>${escapeHtml(d.name)}</span>
        ${tag}
      </label>`;
      })
      .join("");

    els.selectionList.querySelectorAll(".selection-item").forEach((label) => {
      label.addEventListener("click", (e) => {
        e.preventDefault();
        const input = label.querySelector("[data-select]");
        if (!input) return;
        const id = input.getAttribute("data-select");
        const device = state.devices.find((d) => d.id === id);
        if (!device) return;
        device.isSelected = !device.isSelected;
        input.checked = device.isSelected;
        appendLog(`[데모] 그룹 선택 ${device.name}: ${device.isSelected ? "ON" : "OFF"}`);
      });
    });
  }

  function renderPresets() {
    if (!state.presets.length) {
      els.presetList.innerHTML = `<span class="preset-empty">저장된 프리셋 없음</span>`;
      return;
    }

    els.presetList.innerHTML = state.presets
      .map(
        (p) => `
      <div class="preset-chip">
        <button type="button" class="preset-apply" data-preset-apply="${p.id}">${escapeHtml(p.name)}</button>
        <button type="button" class="preset-delete" data-preset-delete="${p.id}" title="삭제">✕</button>
      </div>`
      )
      .join("");

    els.presetList.querySelectorAll("[data-preset-apply]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const preset = state.presets.find((p) => p.id === btn.getAttribute("data-preset-apply"));
        if (!preset) return;
        const ids = new Set(preset.deviceIds);
        state.devices.forEach((d) => {
          d.isSelected = ids.has(d.id);
        });
        const offline = state.devices.filter((d) => d.isSelected && !d.isConnected);
        appendLog(`[데모] 프리셋 적용: ${preset.name}`);
        if (offline.length) {
          appendLog(
            `[데모] 프리셋 경고: 미연결 장비 — ${offline.map((d) => d.name).join(", ")}`
          );
        }
        renderSelection();
        state.devices.forEach(patchDemoCard);
      });
    });

    els.presetList.querySelectorAll("[data-preset-delete]").forEach((btn) => {
      btn.addEventListener("click", () => {
        if (!confirm("이 프리셋을 삭제할까요?")) return;
        const id = btn.getAttribute("data-preset-delete");
        state.presets = state.presets.filter((p) => p.id !== id);
        savePresets();
        appendLog("[데모] 프리셋 삭제");
        renderPresets();
      });
    });
  }

  function renderGrid() {
    els.grid.innerHTML = state.devices.map(cardHtml).join("");
  }

  function patchDemoCard(d) {
    const card = els.grid.querySelector(`.device-card[data-id="${d.id}"]`);
    if (!card) {
      renderGrid();
      return;
    }
    card.classList.toggle("is-recording", d.isConnected && isRecording(d.transportStatus));
    card.classList.toggle("is-offline", !d.isConnected);
    card.classList.toggle(
      "is-probing",
      d.isConnected && !d.capabilities?.capabilitiesResolved
    );
    card.classList.toggle(
      "is-no-remote",
      d.isConnected &&
        !!d.capabilities?.capabilitiesResolved &&
        !d.capabilities.canPlay &&
        !d.capabilities.canRecord &&
        !d.capabilities.canStop
    );
    const title = card.querySelector(".card-title");
    if (title) title.textContent = d.name;
    const model = card.querySelector(".card-model");
    if (model) {
      const text = d.capabilities?.model || "";
      model.textContent = text;
      model.hidden = !text;
    }
    const conn = card.querySelector(".conn");
    if (conn) {
      conn.classList.toggle("is-off", !d.isConnected);
      conn.innerHTML = `<span class="dot ${d.isConnected ? "on" : ""}"></span>${
        d.isConnected ? "Connected" : "Disconnected"
      }`;
    }
    const mode = card.querySelector(".mode-status");
    if (mode) mode.outerHTML = modeStatusHtml(d);
    const slotSelect = card.querySelector(".slot-select");
    const nextSelect = slotSelectHtml(d).trim();
    if (slotSelect && nextSelect) {
      slotSelect.outerHTML = nextSelect;
    } else if (!slotSelect && nextSelect) {
      card.querySelector(".card-col-c")?.insertAdjacentHTML("beforeend", nextSelect);
    } else if (slotSelect && !nextSelect) {
      slotSelect.remove();
    }
    const slots = card.querySelector(".slots");
    if (slots) slots.innerHTML = (d.slots || [d.slot1, d.slot2]).map(slotHtml).join("");
    const transportBtns = card.querySelector(".transport-btns");
    if (transportBtns) transportBtns.innerHTML = transportButtonsInner(d);
  }

  els.grid.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-action]");
    if (!btn || !els.grid.contains(btn)) return;
    onCardAction({ currentTarget: btn });
  });

  function cardHtml(d) {
    const offlineClass = d.isConnected ? "" : "is-offline";
    const recordingClass = d.isConnected && isRecording(d.transportStatus) ? "is-recording" : "";
    const probingClass =
      d.isConnected && !d.capabilities?.capabilitiesResolved ? "is-probing" : "";
    const noRemoteClass =
      d.isConnected &&
      d.capabilities?.capabilitiesResolved &&
      !d.capabilities.canPlay &&
      !d.capabilities.canRecord &&
      !d.capabilities.canStop
        ? "is-no-remote"
        : "";
    const model = d.capabilities?.model || "";
    return `
      <article class="device-card ${offlineClass} ${recordingClass} ${probingClass} ${noRemoteClass}" data-id="${d.id}">
        <div class="card-col card-col-a">
          <span class="conn ${d.isConnected ? "" : "is-off"}"><span class="dot ${d.isConnected ? "on" : ""}"></span>${
            d.isConnected ? "Connected" : "Disconnected"
          }</span>
          <h3 class="card-title">${escapeHtml(d.name)}</h3>
          <p class="card-ip">${escapeHtml(d.address)}</p>
          <p class="card-model" ${model ? "" : "hidden"}>${escapeHtml(model)}</p>
          <div class="card-actions-mini">
            <button type="button" class="icon-btn" data-action="rename" data-id="${d.id}" title="이름 변경">✎</button>
            <button type="button" class="icon-btn" data-action="remove" data-id="${d.id}" title="삭제">✕</button>
          </div>
        </div>
        <div class="card-col card-col-b">
          ${modeStatusHtml(d)}
        </div>
        <div class="card-col card-col-c">
          <div class="slots">
            ${(d.slots || [d.slot1, d.slot2]).map(slotHtml).join("")}
          </div>
          ${slotSelectHtml(d)}
        </div>
        <div class="card-col card-col-d">
          <div class="transport-btns">
            ${transportButtonsInner(d)}
          </div>
        </div>
        <div class="card-col card-col-e">
          <div class="order-btns">
            <button type="button" class="btn btn-ghost" data-action="up" data-id="${d.id}" title="위로">▲</button>
            <button type="button" class="btn btn-ghost" data-action="down" data-id="${d.id}" title="아래로">▼</button>
          </div>
        </div>
      </article>`;
  }

  function modeStatusHtml(d) {
    if (!d.isConnected) {
      return `<span class="mode-status mode-offline">연결 끊김</span>`;
    }
    if (!d.capabilities?.capabilitiesResolved) {
      return `<span class="mode-status mode-probing">기능 확인 중</span>`;
    }

    const value = String(d.transportStatus || "").toLowerCase();
    let label = "중지";
    let cls = "mode-stopped";
    if (isRecording(value)) {
      label = "녹화중";
      cls = "mode-record";
    } else if (value === "play" || value === "playing") {
      label = "재생중";
      cls = "mode-play";
    } else if (value === "preview" || d.isPreviewMode) {
      label = "미리보기";
      cls = "mode-preview";
    } else if (value === "stopped" || value === "stop") {
      label = "중지";
      cls = "mode-stopped";
    } else if (value && value !== "unknown") {
      label = d.transportStatus;
      cls = "mode-unknown";
    }

    const onAir = d.isStreaming
      ? `<span class="mode-onair-tag" title="스트리밍 중">ON AIR</span>`
      : "";
    const spill =
      d.spillStatus === "pending"
        ? `<span class="mode-spill-tag is-pending" title="${escapeHtml(d.spillMessage || "Spill 중")}">SPILL…</span>`
        : d.spillStatus === "done"
          ? `<span class="mode-spill-tag is-done" title="${escapeHtml(d.spillMessage || "Spill 완료")}">SPILL OK</span>`
          : d.spillStatus === "error"
            ? `<span class="mode-spill-tag is-error" title="${escapeHtml(d.spillMessage || "Spill 실패")}">SPILL!</span>`
            : "";
    return `<span class="mode-status ${cls}">${escapeHtml(label)}${onAir}${spill}</span>`;
  }

  function transportButtonsInner(d) {
    const caps = d.capabilities || {};
    if (d.isConnected && !caps.capabilitiesResolved) {
      return `<span class="cap-empty cap-probing">지원 기능 확인 중…<br/><small>확인 전 일괄/개별 제어 제외</small></span>`;
    }
    if (
      caps.capabilitiesResolved &&
      !caps.canPlay &&
      !caps.canRecord &&
      !caps.canStop &&
      !caps.canPreview &&
      !caps.canRecordSpill &&
      !caps.canStream
    ) {
      return `<span class="cap-empty cap-none">원격 제어 기능 없음<br/><small>일괄 녹화/중지 대상에서 제외</small></span>`;
    }
    const off = !d.isConnected ? "disabled" : "";
    const parts = [];
    if (caps.canPlay) {
      parts.push(
        `<button type="button" class="btn btn-soft" data-action="play" data-id="${d.id}" ${off}>▶ 재생</button>`
      );
    }
    if (caps.canRecord) {
      parts.push(
        `<button type="button" class="btn btn-record" data-action="record" data-id="${d.id}" ${off}>● 녹화</button>`
      );
    }
    if (caps.canStop) {
      parts.push(
        `<button type="button" class="btn btn-stop" data-action="stop" data-id="${d.id}" ${off}>■ 중지</button>`
      );
    }
    if (caps.canPreview) {
      const label = d.isPreviewMode ? "출력으로" : "미리보기";
      const next = d.isPreviewMode ? "false" : "true";
      parts.push(
        `<button type="button" class="btn btn-soft" data-action="preview" data-id="${d.id}" data-enable="${next}" title="미리보기/프로그램 출력 전환" ${off}>${label}</button>`
      );
    }
    if (caps.canRecordSpill) {
      parts.push(spillButtonHtml(d, off));
    }
    if (caps.canStream) {
      const live = !!d.isStreaming;
      const label = live ? "OnAir 중지" : "OnAir 시작";
      const next = live ? "false" : "true";
      parts.push(
        `<button type="button" class="btn btn-onair ${live ? "is-live" : ""}" data-action="stream" data-id="${d.id}" data-enable="${next}" title="${live ? "현재 OnAir 중 — 클릭하면 중지" : "스트리밍(OnAir) 시작"}" ${off}>${label}</button>`
      );
    }
    return parts.join("") || `<span class="cap-empty">지원 기능 없음</span>`;
  }

  function spillButtonHtml(d, offlineDisabled) {
    const status = d.spillStatus || "idle";
    const recording = isRecording(d.transportStatus);
    const msg = d.spillMessage || "";
    let label = "Spill 넘김";
    let cls = "btn btn-spill";
    let disabled = offlineDisabled || !d.isConnected;
    let title =
      "녹화 중 다른 슬롯으로 끊김 없이 이어쓰기 (설정이 아니라 한 번 실행하는 동작)";

    if (status === "pending") {
      label = "Spill…";
      cls += " is-pending";
      disabled = "disabled";
      title = msg || "Spill 요청 중…";
    } else if (status === "done") {
      label = "Spill 완료";
      cls += " is-done";
      title = msg || "Spill 완료";
    } else if (status === "error") {
      label = "Spill 실패";
      cls += " is-error";
      title = msg || "Spill 실패";
    } else if (!recording) {
      label = "Spill";
      cls += " is-blocked";
      disabled = "disabled";
      title = "녹화 중에만 사용 가능합니다";
    } else {
      cls += " is-ready";
      title = msg || title;
    }

    const disAttr = disabled === "disabled" || disabled === true ? "disabled" : disabled || "";
    return `<button type="button" class="${cls}" data-action="spill" data-id="${d.id}" title="${escapeHtml(title)}" ${disAttr}>${label}</button>`;
  }

  function slotSelectHtml(_d) {
    // 슬롯 전환 원격 명령은 위험·불안정하여 UI에서 제거. 모니터링만 유지.
    return "";
  }

  function slotHtml(slot) {
    const pct = Math.max(0, Math.min(100, Number(slot.freePercent) || 0));
    const usedPct = slot.totalBytes > 0 ? Math.max(0, 100 - pct) : 0;
    const barClass = pct < 15 ? "low" : pct < 35 ? "warn" : "ok";
    return `
      <div class="slot ${slot.isActive ? "active" : ""}" data-slot="${slot.slotId}">
        <div class="slot-head">
          <span class="slot-id">SLOT ${slot.slotId}${slot.isActive ? " · ACTIVE" : ""}</span>
          <span class="slot-status">${escapeHtml(slot.status)}</span>
          <span class="slot-time">${escapeHtml(slot.recordingTime)}</span>
        </div>
        <div class="slot-capacity">
          <div class="capacity-bar"><span class="capacity-fill ${barClass}" style="width:${usedPct}%"></span></div>
          <div class="capacity-meta">
            <span>${escapeHtml(slot.capacityLabel)}</span>
            <span>${slot.totalBytes > 0 ? `${pct.toFixed(0)}% 여유` : ""}</span>
          </div>
        </div>
      </div>`;
  }

  function onCardAction(e) {
    const action = e.currentTarget.getAttribute("data-action");
    const id = e.currentTarget.getAttribute("data-id");
    const device = state.devices.find((d) => d.id === id);
    if (!device) return;

    switch (action) {
      case "play":
        device.transportStatus = "play";
        device.isPreviewMode = false;
        appendLog(`[데모] Tx: play → ${device.name}`);
        break;
      case "record":
        device.transportStatus = "record";
        device.isPreviewMode = false;
        appendLog(`[데모] Tx: record → ${device.name}`);
        break;
      case "stop":
        device.transportStatus = "stopped";
        device.isPreviewMode = false;
        appendLog(`[데모] Tx: stop → ${device.name}`);
        break;
      case "preview": {
        const enable = e.currentTarget.getAttribute("data-enable") === "true";
        device.isPreviewMode = enable;
        device.transportStatus = enable ? "preview" : "stopped";
        appendLog(`[데모] Tx: preview: enable: ${enable} → ${device.name}`);
        break;
      }
      case "spill": {
        if (!isRecording(device.transportStatus)) {
          device.spillStatus = "error";
          device.spillMessage = "녹화 중에만 Spill 가능";
          appendLog(`[데모] Spill 거부: 녹화 중이 아님 → ${device.name}`);
          break;
        }
        const from = device.activeSlotId || 1;
        const to = from === 1 ? 2 : 1;
        const target = to === 1 ? device.slot1 : device.slot2;
        if (target.status !== "mounted") {
          device.spillStatus = "error";
          device.spillMessage = `슬롯 ${to}가 mounted 아님`;
          appendLog(`[데모] Spill 실패: ${device.spillMessage} → ${device.name}`);
          break;
        }
        device.spillStatus = "pending";
        device.spillMessage = "슬롯 넘김 요청 중…";
        appendLog(`[데모] Tx: record spill → ${device.name}`);
        patchDemoCard(device);
        setTimeout(() => {
          device.activeSlotId = to;
          device.slot1.isActive = to === 1;
          device.slot2.isActive = to === 2;
          device.spillStatus = "done";
          device.spillMessage = `Spill 완료: 슬롯 ${from} → ${to}`;
          appendLog(`[데모] ${device.spillMessage}`);
          patchDemoCard(device);
          setTimeout(() => {
            if (device.spillStatus === "done") {
              device.spillStatus = "idle";
              device.spillMessage = "";
              patchDemoCard(device);
            }
          }, 3500);
        }, 450);
        return;
      }
      case "stream": {
        const enable = e.currentTarget.getAttribute("data-enable") === "true";
        device.isStreaming = enable;
        appendLog(`[데모] Tx: stream: enable: ${enable} → ${device.name}`);
        break;
      }
      case "slot": {
        const slotId = Number(e.currentTarget.getAttribute("data-slot"));
        device.activeSlotId = slotId;
        device.slot1.isActive = slotId === 1;
        device.slot2.isActive = slotId === 2;
        appendLog(`[데모] Tx: slot select: slot id: ${slotId} → ${device.name}`);
        break;
      }
      case "up":
        moveDevice(id, -1);
        return;
      case "down":
        moveDevice(id, 1);
        return;
      case "remove":
        if (!confirm("이 가상 장비를 목록에서 제거할까요?")) return;
        state.devices = state.devices.filter((d) => d.id !== id).map((d, i) => ({ ...d, order: i }));
        appendLog(`[데모] 장비 삭제: ${device.name}`);
        render();
        return;
      case "rename":
        els.renameId.value = id;
        els.renameName.value = device.name;
        els.renameDialog.showModal();
        els.renameName.focus();
        return;
    }
    patchDemoCard(device);
  }

  function moveDevice(id, direction) {
    const ordered = [...state.devices].sort((a, b) => a.order - b.order);
    const index = ordered.findIndex((d) => d.id === id);
    const target = index + direction;
    if (index < 0 || target < 0 || target >= ordered.length) return;
    [ordered[index], ordered[target]] = [ordered[target], ordered[index]];
    state.devices = ordered.map((d, i) => ({ ...d, order: i }));
    appendLog(`[데모] 순서 변경: ${ordered[target].name}`);
    render();
  }

  function batchRecord() {
    const selected = state.devices.filter((d) => d.isSelected);
    const probing = selected.filter((d) => d.isConnected && !d.capabilities?.capabilitiesResolved);
    const unsupported = selected.filter(
      (d) => d.isConnected && d.capabilities?.capabilitiesResolved && !d.capabilities.canRecord
    );
    const targets = selected.filter(
      (d) =>
        d.isConnected &&
        d.capabilities?.capabilitiesResolved &&
        d.capabilities.canRecord &&
        !isRecording(d.transportStatus)
    );
    if (probing.length)
      appendLog(`[데모] 일괄 녹화 건너뜀(기능 확인 중): ${probing.map((d) => d.name).join(", ")}`);
    if (unsupported.length)
      appendLog(`[데모] 일괄 녹화 건너뜀(녹화 미지원): ${unsupported.map((d) => d.name).join(", ")}`);
    targets.forEach((d) => {
      d.transportStatus = "record";
      patchDemoCard(d);
    });
    appendLog(`[데모] 일괄 녹화 (${targets.length}대)`);
  }

  function batchStop() {
    const selected = state.devices.filter((d) => d.isSelected);
    const probing = selected.filter((d) => d.isConnected && !d.capabilities?.capabilitiesResolved);
    const targets = selected.filter(
      (d) =>
        d.isConnected &&
        d.capabilities?.capabilitiesResolved &&
        d.capabilities.canStop &&
        isRecording(d.transportStatus)
    );
    if (probing.length)
      appendLog(`[데모] 일괄 중지 건너뜀(기능 확인 중): ${probing.map((d) => d.name).join(", ")}`);
    targets.forEach((d) => {
      d.transportStatus = "stopped";
      patchDemoCard(d);
    });
    appendLog(`[데모] 일괄 중지 (${targets.length}대)`);
  }

  function resetDiscoverUi() {
    state.discovered = [];
    els.scanStatus.textContent = "검색 대기";
    els.scanSubnets.textContent = "";
    els.discoverList.innerHTML =
      `<div class="discover-empty">[네트워크 검색]을 누르면 가상 장비가 표시됩니다.</div>`;
    els.addSelectedBtn.disabled = true;
  }

  function runFakeScan() {
    els.scanBtn.disabled = true;
    els.scanStatus.textContent = "가상 네트워크 검색 중…";
    els.scanStatus.classList.add("busy");
    els.discoverList.innerHTML = `<div class="discover-empty">데모 스캔 중…</div>`;

    setTimeout(() => {
      const registered = new Set(state.devices.map((d) => d.address));
      state.discovered = DEMO_POOL.map((item) => ({
        ...item,
        alreadyRegistered: registered.has(item.address),
      }));
      state.discovered.unshift({
        address: "192.168.10.21",
        model: "HyperDeck Studio Mini",
        suggestedName: "Mini_Camera_01",
        protocolVersion: "1.12",
        alreadyRegistered: true,
      });

      els.scanSubnets.textContent = "검색 대역: 192.168.10.0/24 · 254호스트 (데모)";
      els.scanStatus.textContent = `검색 완료: HyperDeck ${state.discovered.length}대 발견 (가상)`;
      els.scanStatus.classList.remove("busy");
      els.scanBtn.disabled = false;
      renderDiscoverList();
      appendLog("[데모] 가상 네트워크 검색 완료");
    }, 700);
  }

  function renderDiscoverList() {
    els.discoverList.innerHTML = state.discovered
      .map((d, index) => {
        const disabled = d.alreadyRegistered ? "disabled" : "";
        const checked = !d.alreadyRegistered ? "checked" : "";
        const registeredClass = d.alreadyRegistered ? "registered" : "";
        const badge = d.alreadyRegistered
          ? `<span class="discover-badge">이미 등록됨</span>`
          : "";
        return `
          <label class="discover-item ${registeredClass}">
            <input type="checkbox" data-discover-index="${index}" ${checked} ${disabled} />
            <div class="discover-meta">
              <p class="discover-model">${escapeHtml(d.model)}</p>
              <p class="discover-ip">${escapeHtml(d.address)} · v${escapeHtml(d.protocolVersion)}</p>
              ${badge}
              <div class="discover-name-row">
                <span>표시 이름</span>
                <input type="text" data-name-index="${index}" value="${escapeHtml(d.suggestedName)}" maxlength="64" ${disabled} />
              </div>
            </div>
          </label>`;
      })
      .join("");

    els.discoverList.querySelectorAll("[data-discover-index]").forEach((input) => {
      input.addEventListener("change", updateAddSelectedState);
    });
    updateAddSelectedState();
  }

  function updateAddSelectedState() {
    const selected = els.discoverList.querySelectorAll(
      "[data-discover-index]:checked:not(:disabled)"
    );
    els.addSelectedBtn.disabled = selected.length === 0;
  }

  function addSelectedDevices() {
    const added = [];
    els.discoverList.querySelectorAll("[data-discover-index]:checked:not(:disabled)").forEach((input) => {
      const index = Number(input.getAttribute("data-discover-index"));
      const item = state.discovered[index];
      if (!item) return;
      const nameInput = els.discoverList.querySelector(`[data-name-index="${index}"]`);
      const name = (nameInput?.value || item.suggestedName).trim();
      state.devices.push({
        id: `demo-${Date.now()}-${index}`,
        name,
        address: item.address,
        order: state.devices.length,
        isSelected: true,
        isConnected: true,
        transportStatus: "preview",
        slot1: { slotId: 1, status: "mounted", recordingTime: "03:00:00", isActive: true },
        slot2: { slotId: 2, status: "mounted", recordingTime: "03:00:00", isActive: false },
      });
      added.push(name);
    });

    appendLog(`[데모] 가상 장비 ${added.length}대 추가: ${added.join(", ")}`);
    els.addDialog.close();
    render();
  }

  function startDemoTick() {
    setInterval(() => {
      const recording = state.devices.filter(
        (d) => d.isConnected && isRecording(d.transportStatus) && d.slot1.status === "mounted"
      );
      if (!recording.length) return;

      recording.forEach((d) => {
        d.slot1.recordingTime = decrementTimecode(d.slot1.recordingTime);
        const timeEl = els.grid.querySelector(
          `[data-id="${CSS.escape(d.id)}"] [data-slot="1"] .slot-time`
        );
        if (timeEl) timeEl.textContent = d.slot1.recordingTime;
      });
    }, 1000);
  }

  function decrementTimecode(value) {
    const parts = String(value).split(":").map(Number);
    if (parts.length !== 3 || parts.some((n) => Number.isNaN(n))) return value;
    let total = parts[0] * 3600 + parts[1] * 60 + parts[2];
    if (total <= 0) return "00:00:00";
    total -= 1;
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = total % 60;
    return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  }

  function appendLog(line) {
    const stamp = new Date().toLocaleTimeString("ko-KR", { hour12: false });
    const text = line.startsWith("[") ? line : `[${stamp}] ${line}`;
    const atBottom =
      els.logView.scrollTop + els.logView.clientHeight >= els.logView.scrollHeight - 24;
    els.logView.textContent += (els.logView.textContent ? "\n" : "") + text;
    if (atBottom) els.logView.scrollTop = els.logView.scrollHeight;
  }

  function escapeHtml(value) {
    return String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  if (location.protocol === "file:") {
    document.querySelector(".demo-banner-link")?.remove();
  }
})();
