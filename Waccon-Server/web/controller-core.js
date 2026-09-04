export const TouchLayout = Object.freeze({
    cellCount: 240,
    sideCellCount: 120,
    ringCount: 4,
    sectorCount: 30,
    innerRadius: 0.6,
    outerRadius: 1.0,
    startAngle: -Math.PI / 2,
    endAngle: Math.PI / 2,
});

export const WconLed = Object.freeze({
    payloadLength: 1932,
    unitCountOffset: 0,
    rgbaOffset: 4,
    timestampOffset: 1924,
    maxUnits: 480,
    channelsPerUnit: 4,
    segmentsPerSide: 6,
    segmentCellColumns: 5,
    segmentCellRows: 4,
    ledsPerCell: 2,
    ledsPerSegment: 40,
    sideSegmentCount: 6,
});

export function createLayout(width, height) {
    return {
        width,
        height,
        centerX: width / 2,
        centerY: height / 2,
        baseRadius: Math.min(width, height) / 2,
    };
}

export function cellForPoint(x, y, layout) {
    const dx = x - layout.centerX;
    const dy = y - layout.centerY;
    if (dx === 0 || layout.baseRadius <= 0) return -1;

    const distance = Math.hypot(dx, dy) / layout.baseRadius;
    if (distance < TouchLayout.innerRadius || distance > TouchLayout.outerRadius) return -1;

    const side = dx < 0 ? 1 : 0;
    const ring = clamp(Math.floor(
        ((distance - TouchLayout.innerRadius) / (TouchLayout.outerRadius - TouchLayout.innerRadius)) * TouchLayout.ringCount + 0.00001),
    0, TouchLayout.ringCount - 1);
    const angle = Math.atan2(dy, Math.abs(dx));
    const sector = clamp(Math.floor(
        ((angle - TouchLayout.startAngle) / (TouchLayout.endAngle - TouchLayout.startAngle)) * TouchLayout.sectorCount),
    0, TouchLayout.sectorCount - 1);
    return side * TouchLayout.sideCellCount + ring * TouchLayout.sectorCount + sector;
}

export function ledUnitForCell(cell, half = 0) {
    // `cell` is a touch-zone index (0..239), not an LED index. Every zone
    // owns two raw RGBA LED units, selected by `half` (0=upper, 1=lower).
    if (!Number.isInteger(cell) || cell < 0 || cell >= TouchLayout.cellCount) throw new RangeError('Cell must be between 0 and 239.');
    if (!Number.isInteger(half) || half < 0 || half >= WconLed.ledsPerCell) throw new RangeError('LED half must be 0 or 1.');

    const side = Math.floor(cell / TouchLayout.sideCellCount);
    const localCell = cell % TouchLayout.sideCellCount;
    const touchRing = Math.floor(localCell / TouchLayout.sectorCount);
    const sector = localCell % TouchLayout.sectorCount;
    const segment = Math.floor(sector / WconLed.segmentCellColumns);
    const column = sector % WconLed.segmentCellColumns;

    const visualRight = side === 0;
    const moduleIndex = visualRight
        ? WconLed.sideSegmentCount + segment
        : WconLed.sideSegmentCount - 1 - segment;

    // LED data is module-based. The module sequence runs in opposite
    // directions on the two physical halves, while the right half keeps the
    // module's columns in natural order and the left half reverses them.
    const columnInModule = visualRight
        ? column
        : WconLed.segmentCellColumns - 1 - column;
    const rowInModule = WconLed.segmentCellRows - 1 - touchRing;
    const ledHalf = visualRight ? 1 - half : half;

    return moduleIndex * WconLed.ledsPerSegment
        + columnInModule * (WconLed.segmentCellRows * WconLed.ledsPerCell)
        + rowInModule * WconLed.ledsPerCell
        + ledHalf;
}

export function decodeLedSnapshot(payload) {
    if (payload?.byteLength !== WconLed.payloadLength) throw new RangeError('Expected exactly 1932 LED bytes.');
    const bytes = payload instanceof Uint8Array ? payload : new Uint8Array(payload);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const unitCount = Math.min(view.getUint32(WconLed.unitCountOffset, true), WconLed.maxUnits);
    const colors = new Uint8ClampedArray(TouchLayout.cellCount * 4);
    for (let cell = 0; cell < TouchLayout.cellCount; cell++) {
        const target = cell * 4;
        for (let channel = 0; channel < WconLed.channelsPerUnit; channel++) {
            let value = 0;
            for (let half = 0; half < WconLed.ledsPerCell; half++) {
                const unit = ledUnitForCell(cell, half);
                if (unit < unitCount) value = Math.max(value, bytes[WconLed.rgbaOffset + unit * WconLed.channelsPerUnit + channel]);
            }
            colors[target + channel] = value;
        }
    }
    return { unitCount, colors };
}

export function ledColorForCell(colors, cell) {
    if (!(colors instanceof Uint8ClampedArray) || colors.length !== TouchLayout.cellCount * 4) {
        throw new RangeError('Expected 240 RGBA cell colors.');
    }
    const offset = cell * 4;
    const alpha = Math.max(0.35, colors[offset + 3] / 255);
    return `rgba(${colors[offset]}, ${colors[offset + 1]}, ${colors[offset + 2]}, ${alpha})`;
}

export const WconInput = Object.freeze({
    payloadLength: 262,
    touchOffset: 2,
    sourceIdOffset: 242,
    timestampOffset: 246,
});

export function encodeInputSnapshot(cells, sourceId, timestampUs) {
    if (cells.length !== TouchLayout.cellCount) throw new RangeError('Expected exactly 240 touch cells.');
    const payload = new Uint8Array(WconInput.payloadLength);
    for (let cell = 0; cell < TouchLayout.cellCount; cell++) payload[WconInput.touchOffset + cell] = cells[cell] ? 1 : 0;
    const view = new DataView(payload.buffer);
    view.setUint32(WconInput.sourceIdOffset, sourceId, true);
    view.setBigUint64(WconInput.timestampOffset, timestampUs, true);
    return payload;
}

export function sameCells(left, right) {
    return left.length === right.length && left.every((value, index) => value === right[index]);
}

export function activeCellList(cells) {
    const result = [];
    for (let cell = 0; cell < cells.length; cell++) if (cells[cell]) result.push(cell);
    return result;
}

function clamp(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value));
}
