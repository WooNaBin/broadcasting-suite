(() => {
  const state = {
    devices: [],
    discovered: [],
    presets: [],
    actions: [],
  };

  const ACTION_COMMAND_LABELS = {
    deviceStatus: "상태 확인",
    play: "재생",
    record: "녹화",
    stop: "중지",
    preview: "미리보기 ON",
    previewOff: "미리보기 OFF",
    stream: "OnAir ON",
    streamOff: "OnAir OFF",
    recordSpill: "Spill",
  };

  function isDeviceStatusCommand(command) {
    return String(command || "").toLowerCase() === "devicestatus";
  }

  const els = {
    grid: document.getElementById("device-grid"),
    selectionList: document.getElementById("selection-list"),
    presetList: document.getElementById("preset-list"),
    actionList: document.getElementById("action-list"),
    logView: document.getElementById("log-view"),
    connBadge: document.getElementById("conn-badge"),
    addDialog: document.getElementById("add-dialog"),
    renameDialog: document.getElementById("rename-dialog"),
    addressDialog: document.getElementById("address-dialog"),
    presetDialog: document.getElementById("preset-dialog"),
    actionDialog: document.getElementById("action-dialog"),
    renameForm: document.getElementById("rename-form"),
    addressForm: document.getElementById("address-form"),
    presetForm: document.getElementById("preset-form"),
    actionForm: document.getElementById("action-form"),
    renameId: document.getElementById("rename-id"),
    renameName: document.getElementById("rename-name"),
    addressId: document.getElementById("address-id"),
    addressValue: document.getElementById("address-value"),
    presetName: document.getElementById("preset-name"),
    actionId: document.getElementById("action-id"),
    actionName: document.getElementById("action-name"),
    actionCommand: document.getElementById("action-command"),
    actionDeviceList: document.getElementById("action-device-list"),
    actionDialogTitle: document.getElementById("action-dialog-title"),
    scanBtn: document.getElementById("btn-scan"),
    scanStatus: document.getElementById("scan-status"),
    scanSubnets: document.getElementById("scan-subnets"),
    discoverList: document.getElementById("discover-list"),
    addSelectedBtn: document.getElementById("btn-add-selected"),
  };

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/dashboard")
    .withAutomaticReconnect()
    .build();

  connection.on("DevicesUpdated", (devices) => {
    const next = normalizeDevices(devices);
    const structural = !sameDeviceStructure(state.devices, next);
    state.devices = next;
    // 프리셋/선택만 바뀐 경우 전체 재생성 없이 패치 → 깜빡임 방지
    syncUi({ full: structural });
  });

  connection.on("DeviceUpdated", (device) => {
    const next = normalizeDevice(device);
    const idx = state.devices.findIndex((d) => d.id === next.id);
    if (idx >= 0) {
      const prevName = state.devices[idx].name;
      state.devices[idx] = { ...state.devices[idx], ...next };
      patchCard(state.devices[idx]);
      if (prevName !== next.name) renderSelection();
    } else {
      state.devices.push(next);
      state.devices.sort((a, b) => a.order - b.order);
      syncUi({ full: true });
    }
  });

  connection.on("PresetsUpdated", (presets) => {
    state.presets = normalizePresets(presets);
    renderPresets();
  });

  connection.on("ActionsUpdated", (actions) => {
    state.actions = normalizeActions(actions);
    renderActions();
  });

  connection.on("Log", appendLog);
  connection.on("LogsSnapshot", (lines) => {
    els.logView.textContent = "";
    (lines || []).forEach(appendLog);
  });

  connection.onreconnecting(() => setConnBadge(false, "재연결 중…"));
  connection.onreconnected(() => setConnBadge(true, "연결됨"));
  connection.onclose(() => setConnBadge(false, "연결 끊김"));

  // 탭/브라우저 종료 시 서버에 알려 프로세스 종료 (새로고침은 재연결로 취소)
  function notifyAppClosing() {
    try {
      if (navigator.sendBeacon) {
        navigator.sendBeacon("/api/goodbye", "");
      } else {
        fetch("/api/goodbye", { method: "POST", keepalive: true }).catch(() => {});
      }
    } catch {
      // ignore
    }
  }
  window.addEventListener("pagehide", notifyAppClosing);
  window.addEventListener("beforeunload", notifyAppClosing);

  document.getElementById("btn-add-device").addEventListener("click", () => {
    resetDiscoverUi();
    els.addDialog.showModal();
    runScan();
  });

  document.getElementById("btn-batch-record").addEventListener("click", () =>
    connection.invoke("BatchRecord").catch(showError)
  );
  document.getElementById("btn-batch-stop").addEventListener("click", () =>
    connection.invoke("BatchStop").catch(showError)
  );

  document.getElementById("btn-save-preset").addEventListener("click", () => {
    els.presetName.value = "";
    els.presetDialog.showModal();
    els.presetName.focus();
  });

  document.getElementById("btn-new-action").addEventListener("click", () => {
    openActionEditor(null);
  });

  document.getElementById("add-cancel").addEventListener("click", () => {
    els.addDialog.close("cancel");
  });
  document.getElementById("rename-cancel").addEventListener("click", () => {
    els.renameDialog.close("cancel");
  });
  document.getElementById("address-cancel").addEventListener("click", () => {
    els.addressDialog.close("cancel");
  });
  document.getElementById("preset-cancel").addEventListener("click", () => {
    els.presetDialog.close("cancel");
  });
  document.getElementById("action-cancel").addEventListener("click", () => {
    els.actionDialog.close("cancel");
  });

  els.scanBtn.addEventListener("click", () => runScan());
  els.addSelectedBtn.addEventListener("click", () => addSelectedDevices());

  els.renameDialog.addEventListener("close", async () => {
    if (els.renameDialog.returnValue !== "ok") return;
    const id = els.renameId.value;
    const name = els.renameName.value.trim();
    if (!id || !name) return;
    try {
      await connection.invoke("RenameDevice", id, name);
    } catch (err) {
      showError(err);
    }
  });

  els.addressDialog.addEventListener("close", async () => {
    if (els.addressDialog.returnValue !== "ok") return;
    const id = els.addressId.value;
    const address = els.addressValue.value.trim();
    if (!id || !address) return;
    try {
      await connection.invoke("UpdateDeviceAddress", id, address);
    } catch (err) {
      showError(err);
    }
  });
  els.presetDialog.addEventListener("close", async () => {
    if (els.presetDialog.returnValue !== "ok") return;
    const name = els.presetName.value.trim();
    if (!name) return;
    try {
      await connection.invoke("SavePreset", name);
    } catch (err) {
      showError(err);
    }
  });

  els.actionDialog.addEventListener("close", async () => {
    if (els.actionDialog.returnValue !== "ok") return;
    const name = els.actionName.value.trim();
    const command = els.actionCommand.value;
    const idRaw = els.actionId.value.trim();
    const id = idRaw || null;
    const deviceIds = isDeviceStatusCommand(command)
      ? []
      : [...els.actionDeviceList.querySelectorAll("input[type=checkbox]:checked")].map((el) => el.value);
    if (!name || !command) {
      showError(new Error("이름과 명령을 지정하세요."));
      return;
    }
    if (!isDeviceStatusCommand(command) && deviceIds.length === 0) {
      showError(new Error("이름·명령·장비를 모두 지정하세요."));
      return;
    }
    try {
      await connection.invoke("SaveAction", id, name, command, deviceIds);
    } catch (err) {
      showError(err);
    }
  });

  els.actionCommand?.addEventListener("change", () => {
    refreshActionDevicePicker();
  });

  function resetDiscoverUi() {
    state.discovered = [];
    els.scanStatus.textContent = "검색 대기";
    els.scanStatus.classList.remove("busy");
    els.scanSubnets.textContent = "";
    els.discoverList.innerHTML =
      `<div class="discover-empty">[네트워크 검색]을 누르면 장비가 여기에 표시됩니다.</div>`;
    els.addSelectedBtn.disabled = true;
  }

  async function runScan() {
    els.scanBtn.disabled = true;
    els.addSelectedBtn.disabled = true;
    els.scanStatus.textContent = "네트워크 검색 중… (최대 약 1분)";
    els.scanStatus.classList.add("busy");
    els.discoverList.innerHTML =
      `<div class="discover-empty">HyperDeck 포트(9993)를 스캔하고 있습니다…</div>`;

    try {
      const result = await connection.invoke("ScanNetwork");
      const devices = (result.devices || result.Devices || []).map(normalizeDiscovered);
      const subnets = result.scannedSubnets || result.ScannedSubnets || [];
      const message = result.message || result.Message || "";
      const hosts = result.hostsChecked ?? result.HostsChecked ?? 0;

      state.discovered = devices;
      els.scanSubnets.textContent = subnets.length
        ? `검색 대역: ${subnets.join(", ")} · ${hosts}호스트`
        : "";
      els.scanStatus.textContent = message;
      renderDiscoverList();
    } catch (err) {
      els.scanStatus.textContent = "검색 실패";
      showError(err);
      els.discoverList.innerHTML =
        `<div class="discover-empty">검색에 실패했습니다. 잠시 후 다시 시도하세요.</div>`;
    } finally {
      els.scanBtn.disabled = false;
      els.scanStatus.classList.remove("busy");
      updateAddSelectedState();
    }
  }

  function normalizeDiscovered(d) {
    return {
      address: d.address ?? d.Address ?? "",
      model: d.model ?? d.Model ?? "HyperDeck",
      protocolVersion: d.protocolVersion ?? d.ProtocolVersion ?? "",
      uniqueId: d.uniqueId ?? d.UniqueId ?? "",
      suggestedName: d.suggestedName ?? d.SuggestedName ?? "",
      alreadyRegistered: d.alreadyRegistered ?? d.AlreadyRegistered ?? false,
    };
  }

  function normalizePresets(list) {
    return (list || []).map((p) => ({
      id: p.id ?? p.Id,
      name: p.name ?? p.Name ?? "",
      deviceIds: (p.deviceIds ?? p.DeviceIds ?? []).map(String),
    }));
  }

  function normalizeActions(list) {
    return (list || []).map((a) => ({
      id: a.id ?? a.Id,
      name: a.name ?? a.Name ?? "",
      command: a.command ?? a.Command ?? "record",
      deviceIds: (a.deviceIds ?? a.DeviceIds ?? []).map(String),
    }));
  }

  function renderDiscoverList() {
    if (!state.discovered.length) {
      els.discoverList.innerHTML =
        `<div class="discover-empty">발견된 HyperDeck이 없습니다. 동일 네트워크와 Remote 설정을 확인하세요.</div>`;
      updateAddSelectedState();
      return;
    }

    els.discoverList.innerHTML = state.discovered
      .map((d, index) => {
        const disabled = d.alreadyRegistered ? "disabled" : "";
        const checked = !d.alreadyRegistered ? "checked" : "";
        const registeredClass = d.alreadyRegistered ? "registered" : "";
        const badge = d.alreadyRegistered
          ? `<span class="discover-badge">이미 등록됨</span>`
          : "";
        const name = escapeHtml(d.suggestedName || `HyperDeck_${index + 1}`);
        return `
          <label class="discover-item ${registeredClass}">
            <input type="checkbox" data-discover-index="${index}" ${checked} ${disabled} />
            <div class="discover-meta">
              <p class="discover-model">${escapeHtml(d.model)}</p>
              <p class="discover-ip">${escapeHtml(d.address)}${d.protocolVersion ? ` · v${escapeHtml(d.protocolVersion)}` : ""}</p>
              ${badge}
              <div class="discover-name-row">
                <span>표시 이름</span>
                <input type="text" data-name-index="${index}" value="${name}" maxlength="64" ${disabled} />
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

  async function addSelectedDevices() {
    const selections = [];
    els.discoverList.querySelectorAll("[data-discover-index]:checked:not(:disabled)").forEach((input) => {
      const index = Number(input.getAttribute("data-discover-index"));
      const device = state.discovered[index];
      if (!device) return;
      const nameInput = els.discoverList.querySelector(`[data-name-index="${index}"]`);
      const name = (nameInput?.value || device.suggestedName || "").trim();
      selections.push({ address: device.address, name });
    });

    if (!selections.length) return;

    els.addSelectedBtn.disabled = true;
    try {
      const added = await connection.invoke("AddDiscoveredDevices", selections);
      appendLog(`[UI] 네트워크 검색에서 ${added}대 추가`);
      els.addDialog.close("ok");
    } catch (err) {
      showError(err);
      updateAddSelectedState();
    }
  }

  function normalizeDevices(list) {
    return (list || []).map(normalizeDevice).sort((a, b) => a.order - b.order);
  }

  function normalizeDevice(d) {
    const slot1Fallback = normalizeSlot(d.slot1 ?? d.Slot1, 1);
    const slot2Fallback = normalizeSlot(d.slot2 ?? d.Slot2, 2);
    const rawSlots = d.slots ?? d.Slots;
    const slots =
      Array.isArray(rawSlots) && rawSlots.length > 0
        ? rawSlots.map((s, i) => normalizeSlot(s, i + 1))
        : [slot1Fallback, slot2Fallback];

    return {
      id: d.id ?? d.Id,
      name: d.name ?? d.Name ?? "",
      address: d.address ?? d.Address ?? "",
      order: d.order ?? d.Order ?? 0,
      isSelected: d.isSelected ?? d.IsSelected ?? false,
      isConnected: d.isConnected ?? d.IsConnected ?? false,
      transportStatus: d.transportStatus ?? d.TransportStatus ?? "unknown",
      activeSlotId: d.activeSlotId ?? d.ActiveSlotId ?? null,
      supportsSlotSelect: d.supportsSlotSelect ?? d.SupportsSlotSelect ?? true,
      isPreviewMode: d.isPreviewMode ?? d.IsPreviewMode ?? false,
      isStreaming: d.isStreaming ?? d.IsStreaming ?? false,
      spillStatus: d.spillStatus ?? d.SpillStatus ?? "idle",
      spillMessage: d.spillMessage ?? d.SpillMessage ?? "",
      capabilities: normalizeCapabilities(d.capabilities ?? d.Capabilities),
      slots,
      slot1: slots[0] || slot1Fallback,
      slot2: slots[1] || slot2Fallback,
    };
  }

  function normalizeCapabilities(caps) {
    caps = caps || {};
    const resolved = caps.capabilitiesResolved ?? caps.CapabilitiesResolved ?? false;
    const bool = (a, b, fallback) => {
      const v = caps[a] ?? caps[b];
      return typeof v === "boolean" ? v : fallback;
    };
    // 미확정일 때는 서버가 false를 주더라도 "아직 모름"으로 취급 — 버튼 숨김
    if (!resolved) {
      return {
        model: caps.model ?? caps.Model ?? "",
        slotCount: caps.slotCount ?? caps.SlotCount ?? 2,
        canPlay: false,
        canStop: false,
        canRecord: false,
        canPreview: false,
        canSelectSlot: false,
        canRecordSpill: false,
        canStream: false,
        capabilitiesResolved: false,
      };
    }
    return {
      model: caps.model ?? caps.Model ?? "",
      slotCount: caps.slotCount ?? caps.SlotCount ?? 2,
      canPlay: bool("canPlay", "CanPlay", false),
      canStop: bool("canStop", "CanStop", false),
      canRecord: bool("canRecord", "CanRecord", false),
      canPreview: bool("canPreview", "CanPreview", false),
      canSelectSlot: bool("canSelectSlot", "CanSelectSlot", false),
      canRecordSpill: bool("canRecordSpill", "CanRecordSpill", false),
      canStream: bool("canStream", "CanStream", false),
      capabilitiesResolved: true,
    };
  }

  function isCapsReady(d) {
    return !!(d.isConnected && d.capabilities?.capabilitiesResolved);
  }

  function canBatchRecord(d) {
    return isCapsReady(d) && !!d.capabilities?.canRecord && !isRecording(d.transportStatus);
  }

  function canBatchStop(d) {
    return isCapsReady(d) && !!d.capabilities?.canStop && isRecording(d.transportStatus);
  }

  function normalizeSlot(slot, fallbackId) {
    slot = slot || {};
    return {
      slotId: slot.slotId ?? slot.SlotId ?? fallbackId,
      status: slot.status ?? slot.Status ?? "unknown",
      recordingTime: slot.recordingTime ?? slot.RecordingTime ?? "--:--:--",
      isActive: slot.isActive ?? slot.IsActive ?? false,
      remainingBytes: slot.remainingBytes ?? slot.RemainingBytes ?? 0,
      totalBytes: slot.totalBytes ?? slot.TotalBytes ?? 0,
      freePercent: slot.freePercent ?? slot.FreePercent ?? 0,
      capacityLabel: slot.capacityLabel ?? slot.CapacityLabel ?? "용량 정보 없음",
    };
  }

  function isRecording(status) {
    const value = String(status || "").trim().toLowerCase();
    return value === "record" || value === "recording";
  }

  function render() {
    syncUi({ full: true });
  }

  function sameDeviceStructure(prev, next) {
    if (prev.length !== next.length) return false;
    for (let i = 0; i < prev.length; i++) {
      if (String(prev[i].id) !== String(next[i].id)) return false;
      if (prev[i].order !== next[i].order) return false;
    }
    return true;
  }

  function syncUi({ full = false } = {}) {
    renderSelection();
    renderPresets();
    renderActions();
    if (full || !els.grid.querySelector(".device-card")) renderGrid();
    else state.devices.forEach(patchCard);
  }

  function patchCard(d) {
    const card = els.grid.querySelector(`.device-card[data-id="${cssEscape(d.id)}"]`);
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
    if (title && title.textContent !== d.name) title.textContent = d.name;

    const ip = card.querySelector(".card-ip");
    if (ip && ip.textContent !== d.address) ip.textContent = d.address;

    const ipRow = card.querySelector(".card-ip-row");
    if (ipRow) {
      let ipBtn = ipRow.querySelector('[data-action="edit-ip"]');
      if (d.isConnected) {
        if (ipBtn) ipBtn.remove();
      } else if (!ipBtn) {
        ipBtn = document.createElement("button");
        ipBtn.type = "button";
        ipBtn.className = "icon-btn icon-btn-ip";
        ipBtn.dataset.action = "edit-ip";
        ipBtn.dataset.id = d.id;
        ipBtn.title = "IP 변경";
        ipBtn.textContent = "✎IP";
        ipRow.appendChild(ipBtn);
      } else {
        ipBtn.dataset.id = d.id;
      }
    }
    const model = card.querySelector(".card-model");
    if (model) {
      const text = d.capabilities?.model ? d.capabilities.model : "";
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

  function cssEscape(value) {
    if (window.CSS?.escape) return CSS.escape(String(value));
    return String(value).replace(/"/g, '\\"');
  }

  function renderSelection() {
    if (!state.devices.length) {
      els.selectionList.innerHTML = `<span class="selection-item">등록된 장비가 없습니다</span>`;
      return;
    }

    els.selectionList.innerHTML = state.devices
      .map((d) => {
        const probing = d.isConnected && !d.capabilities?.capabilitiesResolved;
        const noRemote =
          d.isConnected &&
          d.capabilities?.capabilitiesResolved &&
          !d.capabilities.canPlay &&
          !d.capabilities.canRecord &&
          !d.capabilities.canStop;
        let tag = "";
        let title = "";
        let extraClass = "";
        if (!d.isConnected) {
          tag = `<span class="offline-tag">OFF</span>`;
          title = "미연결 — 명령 전송 불가, 자동 재연결 시도 중";
          extraClass = "offline";
        } else if (probing) {
          tag = `<span class="probe-tag">확인중</span>`;
          title = "지원 기능 확인 중 — 일괄 녹화/중지 대상에서 제외";
          extraClass = "probing";
        } else if (noRemote) {
          tag = `<span class="noroute-tag">제어불가</span>`;
          title = "원격 제어 기능 없음 — 일괄 명령 대상에서 제외";
          extraClass = "no-remote";
        }
        return `
      <label class="selection-item ${extraClass}" title="${title}">
        <input type="checkbox" data-select="${d.id}" ${d.isSelected ? "checked" : ""} />
        <span>${escapeHtml(d.name)}</span>
        ${tag}
      </label>`;
      })
      .join("");

    els.selectionList.querySelectorAll(".selection-item").forEach((label) => {
      label.addEventListener("click", async (e) => {
        e.preventDefault();
        const input = label.querySelector("[data-select]");
        if (!input) return;
        const next = !input.checked;
        input.checked = next;
        try {
          await connection.invoke("SetSelected", input.getAttribute("data-select"), next);
        } catch (err) {
          showError(err);
        }
      });
    });
  }

  function renderPresets() {
    if (!els.presetList) return;
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
      btn.addEventListener("click", async () => {
        try {
          await connection.invoke("ApplyPreset", btn.getAttribute("data-preset-apply"));
        } catch (err) {
          showError(err);
        }
      });
    });

    els.presetList.querySelectorAll("[data-preset-delete]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        if (!confirm("이 프리셋을 삭제할까요?")) return;
        try {
          await connection.invoke("DeletePreset", btn.getAttribute("data-preset-delete"));
        } catch (err) {
          showError(err);
        }
      });
    });
  }

  function openActionEditor(action) {
    els.actionId.value = action?.id || "";
    els.actionName.value = action?.name || "";
    els.actionCommand.value = action?.command || "record";
    els.actionDialogTitle.textContent = action ? "원터치 액션 수정" : "원터치 액션 만들기";
    refreshActionDevicePicker(action?.deviceIds || []);
    els.actionDialog.showModal();
    els.actionName.focus();
  }

  function refreshActionDevicePicker(selectedIds) {
    const keepSelection =
      selectedIds !== undefined
        ? selectedIds
        : [...els.actionDeviceList.querySelectorAll("input[type=checkbox]:checked")].map((el) => el.value);
    const selected = new Set(keepSelection.map(String));
    if (isDeviceStatusCommand(els.actionCommand.value)) {
      els.actionDeviceList.innerHTML =
        `<div class="action-empty">등록된 전체 장비 상태를 로그에 표시합니다. 대상 장비 선택이 필요 없습니다.</div>`;
      return;
    }
    if (!state.devices.length) {
      els.actionDeviceList.innerHTML =
        `<div class="action-empty">등록된 장비가 없습니다. 먼저 장비를 추가하세요.</div>`;
      return;
    }
    els.actionDeviceList.innerHTML = state.devices
      .map(
        (d) => `
        <label>
          <input type="checkbox" value="${escapeHtml(String(d.id))}" ${
            selected.has(String(d.id)) ? "checked" : ""
          } />
          <span>${escapeHtml(d.name)}</span>
          <span class="dev-meta">${escapeHtml(d.address)}</span>
        </label>`
      )
      .join("");
  }

  function renderActions() {
    if (!els.actionList) return;
    if (!state.actions.length) {
      els.actionList.innerHTML =
        `<span class="action-empty">저장된 원터치 액션 없음 — [액션 만들기]로 추가</span>`;
      return;
    }

    const nameById = new Map(state.devices.map((d) => [String(d.id), d.name]));
    els.actionList.innerHTML = state.actions
      .map((a) => {
        const cmd = ACTION_COMMAND_LABELS[a.command] || a.command;
        const names = isDeviceStatusCommand(a.command)
          ? "전체 장비"
          : a.deviceIds
              .map((id) => nameById.get(String(id)) || String(id).slice(0, 8))
              .join(", ");
        return `
      <div class="action-chip">
        <button type="button" class="action-run" data-action-run="${a.id}" title="실행">
          <span class="action-run-name">${escapeHtml(a.name)}</span>
          <span class="action-run-meta">${escapeHtml(cmd)} · ${escapeHtml(names || "장비 없음")}</span>
        </button>
        <button type="button" class="action-edit" data-action-edit="${a.id}" title="수정">✎</button>
        <button type="button" class="action-delete" data-action-delete="${a.id}" title="삭제">✕</button>
      </div>`;
      })
      .join("");

    els.actionList.querySelectorAll("[data-action-run]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        try {
          await connection.invoke("RunAction", btn.getAttribute("data-action-run"));
        } catch (err) {
          showError(err);
        }
      });
    });

    els.actionList.querySelectorAll("[data-action-edit]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const action = state.actions.find((a) => String(a.id) === btn.getAttribute("data-action-edit"));
        if (action) openActionEditor(action);
      });
    });

    els.actionList.querySelectorAll("[data-action-delete]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        if (!confirm("이 원터치 액션을 삭제할까요?")) return;
        try {
          await connection.invoke("DeleteAction", btn.getAttribute("data-action-delete"));
        } catch (err) {
          showError(err);
        }
      });
    });
  }

  function renderGrid() {
    if (!state.devices.length) {
      els.grid.innerHTML = `
        <div class="empty-state">
          <div>
            <strong>장비를 검색해 추가하세요</strong>
            <p>상단 [장비 추가]로 네트워크의 HyperDeck을 찾아 선택합니다.</p>
          </div>
        </div>`;
      return;
    }

    els.grid.innerHTML = state.devices.map(cardHtml).join("");
  }

  // 이벤트 위임: 카드 부분 업데이트 후에도 리스너 유지
  els.grid.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-action]");
    if (!btn || !els.grid.contains(btn)) return;
    onCardAction({ currentTarget: btn, preventDefault: () => e.preventDefault() });
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
          <p class="card-ip-row">
            <span class="card-ip">${escapeHtml(d.address)}</span>
            ${
              d.isConnected
                ? ""
                : `<button type="button" class="icon-btn icon-btn-ip" data-action="edit-ip" data-id="${d.id}" title="IP 변경">✎IP</button>`
            }
          </p>
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
    const caps = d.capabilities || normalizeCapabilities({});
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
      title = msg || "Spill 완료 — 활성 슬롯이 바뀌었는지 확인하세요";
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
    // 슬롯 전환 버튼 제거 — 용량/상태 모니터링만
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
          <span class="slot-time" title="남은 녹화 시간">${escapeHtml(slot.recordingTime)}</span>
        </div>
        <div class="slot-capacity" title="${escapeHtml(slot.capacityLabel)}">
          <div class="capacity-bar"><span class="capacity-fill ${barClass}" style="width:${usedPct}%"></span></div>
          <div class="capacity-meta">
            <span>${escapeHtml(slot.capacityLabel)}</span>
            <span>${slot.totalBytes > 0 ? `${pct.toFixed(0)}% 여유` : ""}</span>
          </div>
        </div>
      </div>`;
  }

  async function onCardAction(e) {
    const btn = e.currentTarget;
    const action = btn.getAttribute("data-action");
    const id = btn.getAttribute("data-id");
    try {
      switch (action) {
        case "play":
          await connection.invoke("Play", id);
          break;
        case "record":
          await connection.invoke("Record", id);
          break;
        case "stop":
          await connection.invoke("Stop", id);
          break;
        case "slot":
          // 슬롯 전환 비활성
          break;
        case "edit-ip": {
          const card = state.devices.find((x) => x.id === id);
          els.addressId.value = id;
          els.addressValue.value = card?.address || "";
          els.addressDialog.showModal();
          els.addressValue.focus();
          break;
        }
        case "preview": {
          const enable = btn.getAttribute("data-enable") === "true";
          await connection.invoke("Preview", id, enable);
          break;
        }
        case "spill":
          await connection.invoke("RecordSpill", id);
          break;
        case "stream": {
          const enable = btn.getAttribute("data-enable") === "true";
          await connection.invoke("Stream", id, enable);
          break;
        }
        case "up":
          await connection.invoke("MoveUp", id);
          break;
        case "down":
          await connection.invoke("MoveDown", id);
          break;
        case "remove":
          if (!confirm("이 장비를 삭제할까요?")) return;
          await connection.invoke("RemoveDevice", id);
          break;
        case "rename": {
          const device = state.devices.find((d) => d.id === id);
          els.renameId.value = id;
          els.renameName.value = device?.name || "";
          els.renameDialog.showModal();
          els.renameName.focus();
          break;
        }
      }
    } catch (err) {
      showError(err);
    }
  }

  function appendLog(line) {
    const atBottom =
      els.logView.scrollTop + els.logView.clientHeight >= els.logView.scrollHeight - 24;
    els.logView.textContent += (els.logView.textContent ? "\n" : "") + line;
    if (atBottom) els.logView.scrollTop = els.logView.scrollHeight;
  }

  function setConnBadge(ok, text) {
    els.connBadge.textContent = text;
    els.connBadge.classList.toggle("badge-on", ok);
    els.connBadge.classList.toggle("badge-off", !ok);
  }

  function escapeHtml(value) {
    return String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function showError(err) {
    console.error(err);
    appendLog(`[UI] 오류: ${err?.message || err}`);
  }

  async function start() {
    try {
      await connection.start();
      setConnBadge(true, "연결됨");
    } catch (err) {
      setConnBadge(false, "연결 실패");
      showError(err);
      setTimeout(start, 3000);
    }
  }

  start();
})();
