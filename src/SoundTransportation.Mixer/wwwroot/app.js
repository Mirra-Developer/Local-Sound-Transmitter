const channelsEl = document.querySelector("#channels");
const template = document.querySelector("#channel-template");
const statusEl = document.querySelector("#status");
const refreshButton = document.querySelector("#refresh");
const saveConfigButton = document.querySelector("#save-config");
const configMessage = document.querySelector("#config-message");
const nodes = new Map();

const configEls = {
  receiverEnabled: document.querySelector("#receiver-enabled"),
  receiverAuto: document.querySelector("#receiver-auto"),
  receiverChannels: document.querySelector("#receiver-channels"),
  transmitterEnabled: document.querySelector("#transmitter-enabled"),
  transmitterName: document.querySelector("#transmitter-name"),
  transmitterId: document.querySelector("#transmitter-id"),
  targets: document.querySelector("#targets"),
  udpPort: document.querySelector("#udp-port"),
  audioOutputEnabled: document.querySelector("#audio-output-enabled"),
  localSessionMuteEnabled: document.querySelector("#local-session-mute-enabled"),
  localLoopbackEnabled: document.querySelector("#local-loopback-enabled"),
  localLoopbackName: document.querySelector("#local-loopback-name"),
  localLoopbackOutput: document.querySelector("#local-loopback-output")
};

document.querySelectorAll(".tab").forEach((tab) => {
  tab.addEventListener("click", () => switchView(tab.dataset.view));
});

refreshButton.addEventListener("click", () => {
  loadStatus();
  loadChannels();
  loadConfig();
});
saveConfigButton.addEventListener("click", saveConfig);
document.querySelector("#add-receiver-channel").addEventListener("click", () => addReceiverChannelRow({ outputEnabled: true }));
document.querySelector("#add-target").addEventListener("click", () => addTargetRow({ port: Number(configEls.udpPort.value) || 5055 }));

setInterval(loadChannels, 250);
loadStatus();
loadChannels();
loadConfig();

function switchView(viewId) {
  document.querySelectorAll(".tab").forEach((tab) => tab.classList.toggle("active", tab.dataset.view === viewId));
  document.querySelectorAll(".view").forEach((view) => view.classList.toggle("active", view.id === viewId));
}

async function loadStatus() {
  const response = await fetch("/api/status");
  const status = await response.json();
  const receiver = status.receiverEnabled ? "RX on" : "RX off";
  const transmitter = status.transmitterEnabled ? "TX on" : "TX off";
  statusEl.textContent = `${receiver} · ${transmitter} · UDP ${status.udpPort} · ${status.sampleRate} Hz · stereo`;
}

async function loadChannels() {
  const response = await fetch("/api/channels");
  const channels = await response.json();
  const seen = new Set();

  for (const channel of channels) {
    seen.add(channel.id);
    const node = nodes.get(channel.id) ?? createChannelNode(channel.id);
    renderChannel(node, channel);
  }

  for (const [id, node] of nodes) {
    if (!seen.has(id)) {
      node.root.remove();
      nodes.delete(id);
    }
  }
}

function createChannelNode(id) {
  const root = template.content.firstElementChild.cloneNode(true);
  channelsEl.append(root);

  const node = {
    root,
    title: root.querySelector("h2"),
    meta: root.querySelector(".meta"),
    state: root.querySelector(".state"),
    meter: root.querySelector(".meter span"),
    volume: root.querySelector(".volume input[type='range']"),
    volumePercent: root.querySelector(".volume-percent"),
    sourceIp: root.querySelector(".source-ip input"),
    bind: root.querySelector(".bind"),
    mute: root.querySelector(".mute"),
    solo: root.querySelector(".solo"),
    output: root.querySelector(".output")
  };

  node.volume.addEventListener("change", () => patchChannel(id, { volume: Number(node.volume.value) }));
  node.volume.addEventListener("input", () => {
    if (document.activeElement !== node.volumePercent) {
      node.volumePercent.value = Math.round(Number(node.volume.value) * 100);
    }
  });
  node.volumePercent.addEventListener("change", () => patchChannel(id, { volume: percentToVolume(node.volumePercent.value) }));
  node.volumePercent.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      node.volumePercent.blur();
    }
  });
  node.bind.addEventListener("click", () => patchChannel(id, { sourceIp: node.sourceIp.value.trim() }));
  node.mute.addEventListener("click", () => patchChannel(id, { muted: !node.mute.classList.contains("active") }));
  node.solo.addEventListener("click", () => patchChannel(id, { solo: !node.solo.classList.contains("active") }));
  node.output.addEventListener("click", () => patchChannel(id, { outputEnabled: !node.output.classList.contains("active") }));

  nodes.set(id, node);
  return node;
}

function renderChannel(node, channel) {
  node.title.textContent = channel.name;
  const source = channel.sourceIp ? `bind ${channel.sourceIp}` : `last ${channel.lastSourceIp ?? "local"}`;
  node.meta.textContent = `${channel.id.slice(0, 8)} · ${source} · buffer ${Math.round(channel.queuedSamples / 96) / 10} ms`;
  node.volume.value = channel.volume;
  if (document.activeElement !== node.volumePercent) {
    node.volumePercent.value = Math.round(channel.volume * 100);
  }
  if (document.activeElement !== node.sourceIp) {
    node.sourceIp.value = channel.sourceIp ?? "";
  }
  node.meter.style.width = `${Math.min(100, Math.round(channel.level * 100))}%`;
  node.mute.classList.toggle("active", channel.muted);
  node.mute.classList.toggle("danger", true);
  node.solo.classList.toggle("active", channel.solo);
  node.output.classList.toggle("active", channel.outputEnabled);
  node.state.classList.toggle("stale", Date.now() - Date.parse(channel.lastSeenUtc) > 3000);
}

async function patchChannel(id, body) {
  await fetch(`/api/channels/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  await loadChannels();
}

function percentToVolume(value) {
  const percent = Math.max(0, Math.min(200, Number(value) || 0));
  return percent / 100;
}

async function loadConfig() {
  const response = await fetch("/api/config");
  const config = await response.json();
  renderConfig(config);
}

function renderConfig(config) {
  configEls.receiverEnabled.checked = config.receiver.enabled;
  configEls.receiverAuto.checked = config.receiver.autoCreateChannels;
  configEls.receiverChannels.replaceChildren();
  for (const channel of config.receiver.channels ?? []) {
    addReceiverChannelRow(channel);
  }

  configEls.transmitterEnabled.checked = config.transmitter.enabled;
  configEls.transmitterName.value = config.transmitter.name ?? "";
  configEls.transmitterId.value = config.transmitter.senderId ?? "";
  configEls.targets.replaceChildren();
  for (const target of config.transmitter.targets ?? []) {
    addTargetRow(target);
  }

  configEls.udpPort.value = config.audio.udpPort;
  configEls.audioOutputEnabled.checked = config.audio.output.enabled;
  configEls.localSessionMuteEnabled.checked = config.audio.localSessionMuteOnRemoteSolo?.enabled ?? true;
  configEls.localLoopbackEnabled.checked = config.audio.localLoopback.enabled;
  configEls.localLoopbackName.value = config.audio.localLoopback.name ?? "";
  configEls.localLoopbackOutput.checked = config.audio.localLoopback.outputEnabled;
}

function addReceiverChannelRow(channel = {}) {
  const row = document.querySelector("#receiver-channel-template").content.firstElementChild.cloneNode(true);
  row.querySelector(".receiver-name").value = channel.name ?? "";
  row.querySelector(".receiver-ip").value = channel.sourceIp ?? "";
  row.querySelector(".receiver-output").checked = channel.outputEnabled ?? true;
  row.querySelector(".remove-row").addEventListener("click", () => row.remove());
  configEls.receiverChannels.append(row);
}

function addTargetRow(target = {}) {
  const row = document.querySelector("#target-template").content.firstElementChild.cloneNode(true);
  row.querySelector(".target-address").value = target.address ?? "";
  row.querySelector(".target-port").value = target.port ?? 5055;
  row.querySelector(".remove-row").addEventListener("click", () => row.remove());
  configEls.targets.append(row);
}

async function saveConfig() {
  const body = collectConfig();
  const response = await fetch("/api/config", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });

  const result = await response.json();
  configMessage.textContent = result.message;
  renderConfig(result.config);
  await loadStatus();
  await loadChannels();
}

function collectConfig() {
  return {
    receiver: {
      enabled: configEls.receiverEnabled.checked,
      autoCreateChannels: configEls.receiverAuto.checked,
      channels: [...configEls.receiverChannels.querySelectorAll(".receiver-row")]
        .map((row) => ({
          name: row.querySelector(".receiver-name").value.trim(),
          sourceIp: row.querySelector(".receiver-ip").value.trim(),
          outputEnabled: row.querySelector(".receiver-output").checked
        }))
        .filter((row) => row.name || row.sourceIp)
    },
    transmitter: {
      enabled: configEls.transmitterEnabled.checked,
      name: configEls.transmitterName.value.trim(),
      senderId: configEls.transmitterId.value.trim(),
      targets: [...configEls.targets.querySelectorAll(".target-row")]
        .map((row) => ({
          address: row.querySelector(".target-address").value.trim(),
          port: Number(row.querySelector(".target-port").value) || 5055
        }))
        .filter((target) => target.address)
    },
    audio: {
      udpPort: Number(configEls.udpPort.value) || 5055,
      output: {
        enabled: configEls.audioOutputEnabled.checked
      },
      localSessionMuteOnRemoteSolo: {
        enabled: configEls.localSessionMuteEnabled.checked
      },
      localLoopback: {
        enabled: configEls.localLoopbackEnabled.checked,
        name: configEls.localLoopbackName.value.trim() || "E Local",
        outputEnabled: configEls.localLoopbackOutput.checked
      }
    }
  };
}
