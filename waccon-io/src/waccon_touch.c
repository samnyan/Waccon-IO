#include "waccon_touch.h"
#include "waccon_log.h"

#include <math.h>
#include <stdlib.h>
#include <string.h>

static struct waccon_touch_backend *active_backend;
static struct waccon_touch_mapping active_mapping;

static void waccon_touch_update_client_size(HWND hwnd)
{
    RECT rect;
    int32_t width;
    int32_t height;

    if (active_backend == NULL || !active_backend->attached || !GetClientRect(hwnd, &rect)) return;
    width = rect.right - rect.left;
    height = rect.bottom - rect.top;
    if (width <= 0 || height <= 0) return;
    EnterCriticalSection(&active_backend->lock);
    active_mapping.width = width;
    active_mapping.height = height;
    LeaveCriticalSection(&active_backend->lock);
    waccon_log("touch mapping resized client=%ldx%ld\n", width, height);
}

static float waccon_config_float(const wchar_t *key, float fallback, const wchar_t *filename)
{
    wchar_t value[64];
    wchar_t *end;
    float parsed;

    if (GetPrivateProfileStringW(L"waccon", key, L"", value, _countof(value), filename) == 0) return fallback;
    parsed = wcstof(value, &end);
    if (end == value || *end != L'\0') return fallback;
    return parsed;
}

void waccon_touch_mapping_config_load(struct waccon_touch_mapping *mapping, const wchar_t *filename)
{
    float center_x;
    float center_y;
    float radius;
    float inner_radius;
    float start_angle;

    if (mapping == NULL || filename == NULL) return;
    center_x = waccon_config_float(L"centerX", mapping->center_x, filename);
    center_y = waccon_config_float(L"centerY", mapping->center_y, filename);
    radius = waccon_config_float(L"radius", mapping->radius, filename);
    inner_radius = waccon_config_float(L"innerRadius", mapping->inner_radius, filename);
    start_angle = waccon_config_float(L"startAngle", mapping->start_angle * 57.295779513f, filename);

    if (center_x >= 0.0f && center_x <= 1.0f) mapping->center_x = center_x;
    if (center_y >= 0.0f && center_y <= 1.0f) mapping->center_y = center_y;
    if (radius > 0.0f && radius <= 2.0f) mapping->radius = radius;
    if (inner_radius >= 0.0f && inner_radius < mapping->radius) mapping->inner_radius = inner_radius;
    if (start_angle >= -180.0f && start_angle <= 0.0f) mapping->start_angle = start_angle * 0.01745329252f;
    mapping->end_angle = mapping->start_angle + 3.141592654f;
    mapping->reverse = GetPrivateProfileIntW(L"waccon", L"reverse", mapping->reverse, filename) != 0;
}

void waccon_touch_set_mapping(const struct waccon_touch_mapping *mapping)
{
    if (mapping == NULL) return;
    if (active_backend != NULL && active_backend->attached) {
        EnterCriticalSection(&active_backend->lock);
        active_mapping = *mapping;
        LeaveCriticalSection(&active_backend->lock);
    } else {
        active_mapping = *mapping;
    }
}

static float waccon_touch_mapping_distance(const struct waccon_touch_mapping *mapping, float x, float y)
{
    float base_radius;
    float dx;
    float dy;

    if (mapping == NULL || mapping->width <= 0 || mapping->height <= 0) return -1.0f;
    base_radius = (float)(mapping->width < mapping->height ? mapping->width : mapping->height) * 0.5f;
    dx = (x - mapping->center_x) * (float)mapping->width;
    dy = (y - mapping->center_y) * (float)mapping->height;
    return sqrtf(dx * dx + dy * dy) / base_radius;
}

bool waccon_touch_mapping_contains(const struct waccon_touch_mapping *mapping, float x, float y)
{
    if (mapping == NULL || mapping->radius <= mapping->inner_radius) return false;
    float distance = waccon_touch_mapping_distance(mapping, x, y);
    return distance <= mapping->radius && distance >= mapping->inner_radius;
}

int waccon_touch_mapping_cell(const struct waccon_touch_mapping *mapping, float x, float y)
{
    float dx;
    float dy;
    float distance;
    float angle;
    int ring;
    int sector;
    int side;

    if (!waccon_touch_mapping_contains(mapping, x, y)) return -1;
    dx = (x - mapping->center_x) * (float)mapping->width;
    dy = (y - mapping->center_y) * (float)mapping->height;
    if (dx == 0.0f) return -1;
    distance = waccon_touch_mapping_distance(mapping, x, y);
    angle = atan2f(dy, fabsf(dx));
    sector = (int)((angle - mapping->start_angle) /
        ((mapping->end_angle - mapping->start_angle) / 30.0f));
    if (sector < 0) sector = 0;
    if (sector > 29) sector = 29;
    ring = (int)(((distance - mapping->inner_radius) /
        (mapping->radius - mapping->inner_radius)) * 4.0f + 0.00001f);
    if (ring > 3) ring = 3;
    side = mapping->side ? 1 : (x >= mapping->center_x ? 0 : 1);
    if (mapping->reverse) sector = 29 - sector;
    return side * 120 + ring * 30 + sector;
}

void waccon_touch_clear(struct waccon_touch_backend *backend)
{
    EnterCriticalSection(&backend->lock);
    backend->contact_count = 0;
    memset(backend->contacts, 0, sizeof(backend->contacts));
    LeaveCriticalSection(&backend->lock);
}

static void waccon_touch_update(const TOUCHINPUT *input)
{
    POINT point;
    uint32_t i;
    struct waccon_touch_contact *contact = NULL;

    point.x = input->x / 100;
    point.y = input->y / 100;
    ScreenToClient(active_backend->hwnd, &point);
    waccon_log_window_event("touch-contact", active_backend->hwnd, WM_TOUCH, point.x, point.y);
    waccon_log("WM_TOUCH id=%lu flags=0x%lx client=(%ld,%ld)\n", input->dwID, input->dwFlags, point.x, point.y);
    EnterCriticalSection(&active_backend->lock);
    for (i = 0; i < active_backend->contact_count; i++) {
        if (active_backend->contacts[i].id == input->dwID) {
            contact = &active_backend->contacts[i];
            break;
        }
    }
    if (input->dwFlags & TOUCHEVENTF_UP) {
        if (contact != NULL) {
            active_backend->contacts[i] = active_backend->contacts[--active_backend->contact_count];
        }
    } else {
        if (contact == NULL && active_backend->contact_count < WACCON_MAX_CONTACTS) {
            contact = &active_backend->contacts[active_backend->contact_count++];
            memset(contact, 0, sizeof(*contact));
            contact->id = input->dwID;
        }
        if (contact != NULL) {
            int cell;
            contact->phase = (input->dwFlags & TOUCHEVENTF_DOWN) ? WACCON_TOUCH_DOWN : WACCON_TOUCH_MOVE;
            contact->x = (float)point.x / (float)active_mapping.width;
            contact->y = (float)point.y / (float)active_mapping.height;
            contact->pressure = 1.0f;
            cell = waccon_touch_mapping_cell(&active_mapping, contact->x, contact->y);
            waccon_log("WM_TOUCH normalized=(%.3f,%.3f) cell=%d\n", contact->x, contact->y, cell);
        }
    }
    LeaveCriticalSection(&active_backend->lock);
}

LRESULT CALLBACK waccon_touch_wndproc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
    if (msg == WM_SIZE && active_backend != NULL && active_backend->attached) {
        waccon_touch_update_client_size(hwnd);
    }
    if (msg == WM_TOUCH && active_backend != NULL && active_backend->attached) {
        UINT count = LOWORD(wparam);
        waccon_log_window_event("WM_TOUCH", hwnd, msg, -1, -1);
        PTOUCHINPUT inputs = malloc(sizeof(TOUCHINPUT) * count);
        if (inputs != NULL) {
            if (GetTouchInputInfo((HTOUCHINPUT)lparam, count, inputs, sizeof(TOUCHINPUT))) {
                UINT i;
                for (i = 0; i < count; i++) waccon_touch_update(&inputs[i]);
            }
            free(inputs);
        }
        CloseTouchInputHandle((HTOUCHINPUT)lparam);
        return 0;
    }
    if (active_backend != NULL && active_backend->original_wndproc != NULL) {
        return CallWindowProcW(active_backend->original_wndproc, hwnd, msg, wparam, lparam);
    }
    return DefWindowProcW(hwnd, msg, wparam, lparam);
}

HRESULT waccon_touch_attach(struct waccon_touch_backend *backend, HWND hwnd, const struct waccon_touch_mapping *mapping)
{
    if (backend == NULL || hwnd == NULL || mapping == NULL || backend->attached) return E_INVALIDARG;
    memset(backend, 0, sizeof(*backend));
    InitializeCriticalSection(&backend->lock);
    backend->hwnd = hwnd;
    waccon_touch_set_mapping(mapping);
    waccon_log("WinTouch registering hwnd=%p\n", hwnd);
    if (!RegisterTouchWindow(hwnd, 0)) {
        waccon_log("RegisterTouchWindow failed error=%lu\n", GetLastError());
        return HRESULT_FROM_WIN32(GetLastError());
    }
    backend->original_wndproc = (WNDPROC)GetWindowLongPtrW(hwnd, GWLP_WNDPROC);
    if (backend->original_wndproc == NULL) {
        waccon_log("GetWindowLongPtrW(GWLP_WNDPROC) failed error=%lu\n", GetLastError());
        UnregisterTouchWindow(hwnd);
        return HRESULT_FROM_WIN32(GetLastError());
    }
    SetLastError(ERROR_SUCCESS);
    if (SetWindowLongPtrW(hwnd, GWLP_WNDPROC, (LONG_PTR)waccon_touch_wndproc) == 0 && GetLastError() != ERROR_SUCCESS) {
        UnregisterTouchWindow(hwnd);
        return HRESULT_FROM_WIN32(GetLastError());
    }
    backend->attached = true;
    active_backend = backend;
    waccon_log("window-hook installed hwnd=%p original_wndproc=%p\n", hwnd, backend->original_wndproc);
    return S_OK;
}

void waccon_touch_detach(struct waccon_touch_backend *backend)
{
    if (backend == NULL || !backend->attached) return;
    waccon_touch_clear(backend);
    SetWindowLongPtrW(backend->hwnd, GWLP_WNDPROC, (LONG_PTR)backend->original_wndproc);
    UnregisterTouchWindow(backend->hwnd);
    backend->attached = false;
    if (active_backend == backend) active_backend = NULL;
    waccon_log("window-hook removed hwnd=%p\n", backend->hwnd);
    DeleteCriticalSection(&backend->lock);
}

void waccon_mouse_poll(HWND hwnd, bool cells[240])
{
    POINT point;
    RECT rect;
    memset(cells, 0, sizeof(bool) * 240);
    if (hwnd == NULL || GetForegroundWindow() != hwnd) return;
    if (!GetCursorPos(&point) || !ScreenToClient(hwnd, &point)) return;
    if (!GetClientRect(hwnd, &rect)) return;
    if (!(GetAsyncKeyState(VK_LBUTTON) & 0x8000)) return;
    active_mapping.width = rect.right - rect.left;
    active_mapping.height = rect.bottom - rect.top;
    if (active_mapping.width <= 0 || active_mapping.height <= 0) return;
    {
        int cell;
        float normalized_x = (float)point.x / active_mapping.width;
        float normalized_y = (float)point.y / active_mapping.height;
        cell = waccon_touch_mapping_cell(&active_mapping, normalized_x, normalized_y);
        waccon_log_window_event("mouse-left", hwnd, WM_LBUTTONDOWN, point.x, point.y);
        waccon_log("mouse normalized=(%.3f,%.3f) cell=%d client=%ldx%ld\n",
            normalized_x, normalized_y, cell, active_mapping.width, active_mapping.height);
        if (cell >= 0) cells[cell] = true;
    }
}

void waccon_touch_poll(struct waccon_touch_backend *backend, bool cells[240])
{
    uint32_t i;
    memset(cells, 0, sizeof(bool) * 240);
    if (backend == NULL || !backend->attached) return;
    EnterCriticalSection(&backend->lock);
    for (i = 0; i < backend->contact_count; i++) {
        int cell = waccon_touch_mapping_cell(&active_mapping, backend->contacts[i].x, backend->contacts[i].y);
        if (cell >= 0) cells[cell] = true;
    }
    LeaveCriticalSection(&backend->lock);
}
