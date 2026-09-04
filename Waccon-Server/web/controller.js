import {
    activeCellList,
    cellForPoint,
    createLayout,
    encodeInputSnapshot,
    decodeLedSnapshot,
    ledColorForCell,
    sameCells,
    TouchLayout,
} from './controller-core.js';

const canvas = document.getElementById('canvas');
const context = canvas.getContext('2d');
const statusOutput = document.getElementById('status');
const settingsButton = document.getElementById('settings-button');
const settingsDialog = document.getElementById('settings-dialog');
const settingsSave = document.getElementById('settings-save');
const ledBrightnessAmplifier = document.getElementById('led-brightness-amplifier');

const SettingsStorageKey = 'waccon.controller.settings';
const settings = {
    ledBrightnessAmplifier: false,
};

const Wcon = Object.freeze({
    version: 0x0100,
    headerLength: 24,
    hello: 1,
    welcome: 2,
    inputSnapshot: 3,
    clearInput: 4,
    ledSnapshot: 7,
});

const activePointers = new Map();
const activeCells = new Uint8Array(TouchLayout.cellCount);
const ledColors = new Uint8ClampedArray(TouchLayout.cellCount * 4);
const sourceId = crypto.getRandomValues(new Uint32Array(1))[0];

let sequence = 0;
let socket;
let connected = false;
let layout = createLayout(0, 0);

function resize() {
    const scale = window.devicePixelRatio || 1;
    const width = window.innerWidth;
    const height = window.innerHeight;
    canvas.width = Math.floor(width * scale);
    canvas.height = Math.floor(height * scale);
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    context.setTransform(scale, 0, 0, scale, 0, 0);
    layout = createLayout(width, height);
    rebuildCells();
    draw();
}

function sendFrame(kind, payload = new Uint8Array()) {
    if (socket?.readyState !== WebSocket.OPEN) return;
    const frame = new Uint8Array(Wcon.headerLength + payload.length);
    const view = new DataView(frame.buffer);
    frame.set([0x57, 0x43, 0x4f, 0x4e]); // WCON
    view.setUint16(4, Wcon.version, true);
    view.setUint16(6, kind, true);
    view.setUint32(8, payload.length, true);
    view.setUint32(12, sequence++, true);
    view.setBigUint64(16, BigInt(Date.now()) * 1000n, true);
    frame.set(payload, Wcon.headerLength);
    socket.send(frame);
}

function sendSnapshot() {
    if (!connected) return;
    // Unlike toucca's legacy 30-byte bitset, WCON carries 240 one-byte cells
    // at payload offsets 2..241, matching struct waccon_shm_input exactly.
    sendFrame(Wcon.inputSnapshot, encodeInputSnapshot(activeCells, sourceId, BigInt(Date.now()) * 1000n));
}

function clearInput() {
    activePointers.clear();
    activeCells.fill(0);
    draw();
    if (connected) sendFrame(Wcon.clearInput);
}

function pointerPosition(event) {
    const bounds = canvas.getBoundingClientRect();
    return {
        x: (event.clientX - bounds.left) * layout.width / bounds.width,
        y: (event.clientY - bounds.top) * layout.height / bounds.height,
    };
}

function rebuildCells() {
    const next = new Uint8Array(TouchLayout.cellCount);
    for (const point of activePointers.values()) {
        const cell = cellForPoint(point.x, point.y, layout);
        if (cell >= 0) next[cell] = 1;
    }
    if (sameCells(activeCells, next)) return false;
    activeCells.set(next);
    return true;
}

function updatePointers() {
    if (!rebuildCells()) return;
    sendSnapshot();
    draw();
}

function draw() {
    context.clearRect(0, 0, layout.width, layout.height);
    context.lineWidth = 1;
    context.strokeStyle = 'rgba(255, 255, 255, 0.32)';
    context.fillStyle = 'rgba(50, 110, 255, 0.55)';
    for (let side = 0; side < 2; side++) {
        for (let ring = 0; ring < TouchLayout.ringCount; ring++) {
            const innerRadius = TouchLayout.innerRadius +
                (TouchLayout.outerRadius - TouchLayout.innerRadius) * ring / TouchLayout.ringCount;
            const outerRadius = TouchLayout.innerRadius +
                (TouchLayout.outerRadius - TouchLayout.innerRadius) * (ring + 1) / TouchLayout.ringCount;
            for (let sector = 0; sector < TouchLayout.sectorCount; sector++) {
                const start = TouchLayout.startAngle +
                    (TouchLayout.endAngle - TouchLayout.startAngle) * sector / TouchLayout.sectorCount;
                const end = TouchLayout.startAngle +
                    (TouchLayout.endAngle - TouchLayout.startAngle) * (sector + 1) / TouchLayout.sectorCount;
                context.beginPath();
                if (side === 0) {
                    context.arc(layout.centerX, layout.centerY, layout.baseRadius * innerRadius, start, end);
                    context.arc(layout.centerX, layout.centerY, layout.baseRadius * outerRadius, end, start, true);
                } else {
                    context.arc(layout.centerX, layout.centerY, layout.baseRadius * innerRadius, Math.PI - start, Math.PI - end, true);
                    context.arc(layout.centerX, layout.centerY, layout.baseRadius * outerRadius, Math.PI - end, Math.PI - start);
                }
                context.closePath();
                const cell = side * TouchLayout.sideCellCount + ring * TouchLayout.sectorCount + sector;
                if (activeCells[cell]) {
                    const offset = cell * 4;
                    const alpha = Math.max(0.35, ledColors[offset + 3] / 255);
                    context.fillStyle = `rgba(${ledColors[offset]}, ${ledColors[offset + 1]}, ${ledColors[offset + 2]}, ${alpha})`;
                    context.fill();
                } else {
                    const offset = cell * 4;
                    if (ledColors[offset] || ledColors[offset + 1] || ledColors[offset + 2]) {
                        context.fillStyle = amplifiedLedColor(cell);
                        context.fill();
                    }
                    context.stroke();
                }
            }
        }
    }
}

function setStatus(value) {
    statusOutput.textContent = value;
}

function loadSettings() {
    try {
        const saved = JSON.parse(localStorage.getItem(SettingsStorageKey) || '{}');
        settings.ledBrightnessAmplifier = saved.ledBrightnessAmplifier === true;
    } catch {
        settings.ledBrightnessAmplifier = false;
    }
    ledBrightnessAmplifier.checked = settings.ledBrightnessAmplifier;
}

function setSettingsOpen(open) {
    settingsDialog.setAttribute('aria-hidden', String(!open));
    settingsButton.setAttribute('aria-expanded', String(open));
}

function saveSettings() {
    settings.ledBrightnessAmplifier = ledBrightnessAmplifier.checked;
    localStorage.setItem(SettingsStorageKey, JSON.stringify(settings));
    setSettingsOpen(false);
    draw();
}

function amplifiedLedColor(cell) {
    // TODO: apply the user-provided amplifier curve here.
    return ledColorForCell(ledColors, cell);
}

async function refreshBridgeStatus() {
    if (!connected) return;
    try {
        const health = await fetch('/health', { cache: 'no-store' }).then(response => response.json());
        setStatus(health.ioConnected ? `I/O bridge connected (PID ${health.ioPid})` : 'Waiting for waccon-io');
    } catch {
        setStatus('Server status unavailable');
    }
}

function connect() {
    socket = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws`);
    socket.binaryType = 'arraybuffer';
    setStatus('Connecting…');
    socket.addEventListener('open', () => sendFrame(Wcon.hello));
    socket.addEventListener('message', event => {
        const frame = new Uint8Array(event.data);
        if (frame.byteLength < Wcon.headerLength || String.fromCharCode(...frame.subarray(0, 4)) !== 'WCON') return;
        const kind = new DataView(frame.buffer, frame.byteOffset, frame.byteLength).getUint16(6, true);
        if (kind === Wcon.welcome) {
            connected = true;
            const details = new TextDecoder().decode(frame.subarray(Wcon.headerLength));
            setStatus(details.includes('io=connected') ? 'I/O bridge connected' : 'Waiting for waccon-io');
            console.info(`Waccon connected: ${details}`);
            sendSnapshot();
        }
        if (kind === Wcon.ledSnapshot) {
            try {
                const { colors } = decodeLedSnapshot(frame.subarray(Wcon.headerLength));
                ledColors.set(colors);
                draw();
            } catch (error) {
                console.warn('Invalid Waccon LED snapshot.', error);
            }
        }
    });
    socket.addEventListener('close', () => {
        connected = false;
        setStatus('Disconnected; retrying…');
        window.setTimeout(connect, 1000);
    });
}

canvas.addEventListener('pointerdown', event => {
    canvas.setPointerCapture(event.pointerId);
    activePointers.set(event.pointerId, pointerPosition(event));
    updatePointers();
});
canvas.addEventListener('pointermove', event => {
    if (!activePointers.has(event.pointerId)) return;
    activePointers.set(event.pointerId, pointerPosition(event));
    updatePointers();
});
for (const eventName of ['pointerup', 'pointercancel', 'lostpointercapture']) {
    canvas.addEventListener(eventName, event => {
        activePointers.delete(event.pointerId);
        updatePointers();
    });
}
window.addEventListener('resize', resize);
window.addEventListener('pagehide', clearInput);
document.addEventListener('visibilitychange', () => {
    if (document.hidden) clearInput();
});
window.setInterval(() => {
    if (connected && activeCellList(activeCells).length > 0) sendSnapshot();
}, 100);
window.setInterval(refreshBridgeStatus, 1000);

settingsButton.addEventListener('click', () => {
    setSettingsOpen(settingsDialog.getAttribute('aria-hidden') === 'true');
});
settingsSave.addEventListener('click', saveSettings);
loadSettings();

resize();
connect();
