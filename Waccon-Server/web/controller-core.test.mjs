import assert from 'node:assert/strict';
import { cellForPoint, createLayout, decodeLedSnapshot, encodeInputSnapshot, ledColorForCell, ledUnitForCell, LedPreviewCurve, TouchLayout } from './controller-core.js';

const layout = createLayout(1000, 1000);
const point = (angle, radius) => ({
    x: layout.centerX + Math.cos(angle) * radius * layout.baseRadius,
    y: layout.centerY + Math.sin(angle) * radius * layout.baseRadius,
});

assert.equal(cellForPoint(point(0, 0.65).x, point(0, 0.65).y, layout), 15);
assert.equal(cellForPoint(point(Math.PI, 0.65).x, point(Math.PI, 0.65).y, layout), 135);
assert.equal(cellForPoint(point(-Math.PI / 4, 0.65).x, point(-Math.PI / 4, 0.65).y, layout), 7);
assert.equal(cellForPoint(point(-3 * Math.PI / 4, 0.65).x, point(-3 * Math.PI / 4, 0.65).y, layout), 127);
assert.equal(cellForPoint(point(0, 0.85).x, point(0, 0.85).y, layout), 75);

const cells = new Uint8Array(TouchLayout.cellCount);
cells[7] = 1;
cells[135] = 1;
const payload = encodeInputSnapshot(cells, 0x12345678, 999n);
assert.equal(payload.length, 262);
assert.equal(payload[2 + 7], 1);
assert.equal(payload[2 + 135], 1);
assert.equal(new DataView(payload.buffer).getUint32(242, true), 0x12345678);
assert.equal(new DataView(payload.buffer).getBigUint64(246, true), 999n);

assert.equal(ledUnitForCell(0, 0), 247);
assert.equal(ledUnitForCell(0, 1), 246);
assert.equal(ledUnitForCell(4, 0), 279);
assert.equal(ledUnitForCell(19, 1), 398);
assert.equal(ledUnitForCell(30, 0), 245);
// side=0 is visual right (natural columns); side=1 is visual left (mirrored columns).
assert.equal(ledUnitForCell(120, 0), 238);
assert.equal(ledUnitForCell(124, 0), 206);
assert.equal(ledUnitForCell(239, 0), 0);
assert.equal(ledUnitForCell(239, 1), 1);

const ledPayload = new Uint8Array(1932);
const ledView = new DataView(ledPayload.buffer);
ledView.setUint32(0, 480, true);
ledPayload.set([10, 20, 30, 255], 4 + 247 * 4);
ledPayload.set([40, 15, 5, 128], 4 + 246 * 4);
const led = decodeLedSnapshot(ledPayload);
assert.equal(led.unitCount, 480);
assert.deepEqual(Array.from(led.colors.slice(0, 4)), [40, 20, 30, 255]);
led.colors[3] = 0;
assert.equal(ledColorForCell(led.colors, 0), 'rgba(40, 20, 30, 0.35)');

const previewCurve = new LedPreviewCurve(0.4);
assert.equal(previewCurve.convert(0), 0);
assert.equal(previewCurve.convert(255), 255);
assert.deepEqual(previewCurve.convertRgb(8, 2, 0), [64, 37, 0]);
assert.throws(() => previewCurve.setGamma(0), RangeError);
const strongCurve = new LedPreviewCurve(0.25);
assert.ok(strongCurve.convert(8) > previewCurve.convert(8));
