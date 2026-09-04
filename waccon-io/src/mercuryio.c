#include <process.h>
#include <string.h>

#include "waccon/mercuryio.h"
#include "waccon_state.h"
#include "waccon_touch.h"
#include "waccon_log.h"

static struct waccon_state waccon;
static struct waccon_touch_backend touch_backend;
static struct waccon_touch_mapping touch_mapping = {
    .left = 0,
    .top = 0,
    .width = 1,
    .height = 1,
    .center_x = 0.48f,
    .center_y = 0.50f,
    .radius = 0.47f,
    .inner_radius = 0.08f,
    .start_angle = -1.5707963f,
    .end_angle = 4.7123890f,
    .side = 0,
    .reverse = 0,
};
static HWND game_window;
static HANDLE touch_thread;
static bool touch_cells[WACCON_IO_TOUCH_CELLS];

static bool cursor_enabled = true;
static bool wintouch_enabled = true;
static bool mouse_enabled = true;

static bool debug_enabled = true;
static bool console_enabled = true;

static void waccon_load_config(void)
{
    cursor_enabled = GetPrivateProfileIntW(L"waccon", L"cursor", 1, L".\\segatools.ini") != 0;
    wintouch_enabled = GetPrivateProfileIntW(L"waccon", L"wintouch", 1, L".\\segatools.ini") != 0;
    mouse_enabled = GetPrivateProfileIntW(L"waccon", L"mouse", 1, L".\\segatools.ini") != 0;
    debug_enabled = GetPrivateProfileIntW(L"waccon", L"debug", 1, L".\\segatools.ini") != 0;
    console_enabled = GetPrivateProfileIntW(L"waccon", L"console", 1, L".\\segatools.ini") != 0;
    waccon_log_set_enabled(debug_enabled);
    waccon_log_open_console(console_enabled);
    waccon_log("config debug=%d console=%d cursor=%d wintouch=%d mouse=%d\n",
        debug_enabled, console_enabled, cursor_enabled, wintouch_enabled, mouse_enabled);
}

static HWND waccon_is_game_window(HWND hwnd)
{
    wchar_t class_name[64];
    DWORD process_id;
    if (hwnd == NULL || !IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != NULL) return NULL;
    GetWindowThreadProcessId(hwnd, &process_id);
    if (process_id != GetCurrentProcessId()) return NULL;
    if (GetClassNameW(hwnd, class_name, _countof(class_name)) > 0 && wcscmp(class_name, L"ConsoleWindowClass") == 0) return NULL;
    return hwnd;
}

static BOOL CALLBACK waccon_find_window_callback(HWND hwnd, LPARAM context)
{
    HWND *result = (HWND *) context;
    wchar_t title[128];
    if (*result != NULL || waccon_is_game_window(hwnd) == NULL) return TRUE;
    GetWindowTextW(hwnd, title, _countof(title));
    if (wcsstr(title, L"Mercury") != NULL || wcsstr(title, L"WACCA") != NULL) {
        *result = hwnd;
        return FALSE;
    }
    if (*result == NULL) *result = hwnd;
    return TRUE;
}

static HWND waccon_find_window(void)
{
    HWND hwnd = FindWindowW(NULL, L"Mercury  ");
    if (hwnd == NULL) hwnd = FindWindowW(NULL, L"WACCA");
    if (waccon_is_game_window(hwnd) != NULL) {
        wchar_t title[128];
        GetWindowTextW(hwnd, title, _countof(title));
        waccon_log("window matched hwnd=%p title=%S\n", hwnd, title);
        return hwnd;
    }
    hwnd = NULL;
    EnumWindows(waccon_find_window_callback, (LPARAM) &hwnd);
    if (hwnd != NULL) {
        wchar_t title[128];
        GetWindowTextW(hwnd, title, _countof(title));
        waccon_log("window enumerated hwnd=%p title=%S\n", hwnd, title);
    } else {
        waccon_log("no suitable current-process game window found\n");
    }
    return hwnd;
}

static void waccon_cursor_update(void)
{
    CURSORINFO info;
    HCURSOR cursor;
    int attempts;

    if (!cursor_enabled) return;
    info.cbSize = sizeof(info);
    if (!GetCursorInfo(&info)) return;
    if ((info.flags & CURSOR_SHOWING) == 0) {
        for (attempts = 0; attempts < 16 && ShowCursor(TRUE) < 0; attempts++) {
            /* Balance the game's hidden ShowCursor calls. */
        }
    }
    cursor = LoadCursorW(NULL, MAKEINTRESOURCEW(32512));
    if (cursor != NULL) SetCursor(cursor);
}

static void waccon_ensure_touch_window(void)
{
    RECT rect;
    if (touch_backend.attached || !wintouch_enabled) return;
    if (game_window == NULL) game_window = waccon_find_window();
    if (game_window == NULL || !GetClientRect(game_window, &rect)) {
        waccon_log("game window unavailable or GetClientRect failed, hwnd=%p error=%lu\n", game_window, GetLastError());
        return;
    }
    touch_mapping.width = rect.right - rect.left;
    touch_mapping.height = rect.bottom - rect.top;
    waccon_log("attempting WinTouch attach hwnd=%p client=%ldx%ld\n", game_window, touch_mapping.width, touch_mapping.height);
    if (SUCCEEDED(waccon_touch_attach(&touch_backend, game_window, &touch_mapping))) {
        waccon_log("WinTouch attached\n");
        waccon_touch_poll(&touch_backend, touch_cells);
    } else {
        waccon_log("WinTouch attach failed error=%lu\n", GetLastError());
    }
}

static unsigned int __stdcall waccon_touch_thread_proc(void *ctx)
{
    struct waccon_state *state = ctx;
    mercury_io_touch_callback_t callback;
    for (;;) {
        EnterCriticalSection(&state->touch_lock);
        callback = state->touch_callback;
        LeaveCriticalSection(&state->touch_lock);
        if (InterlockedCompareExchange(&state->touch_stop, 0, 0) != 0) break;
        if (callback != NULL) callback(touch_cells);
        Sleep(1);
    }
    InterlockedExchange(&state->touch_running, 0);
    return 0;
}

uint16_t mercury_io_get_api_version(void) {
    waccon_log_open_console(true);
    waccon_log("mercury_io_get_api_version -> 0x0100 pid=%lu\n", GetCurrentProcessId());
    return WACCON_IO_API_VERSION;
}

HRESULT mercury_io_init(void)
{
    static LONG initialized;
    if (InterlockedCompareExchange(&initialized, 1, 0) == 0) {
        waccon_log("mercury_io_init entered, pid=%lu\n", GetCurrentProcessId());
        waccon_load_config();
        waccon_state_init(&waccon);
        memset(touch_cells, 0, sizeof(touch_cells));
    }
    return S_OK;
}

HRESULT mercury_io_poll(void)
{
    static uint64_t last_log;
    uint64_t now = GetTickCount64();
    waccon_state_poll(&waccon);
    waccon_state_get_touch(&waccon, touch_cells);
    waccon_ensure_touch_window();
    if (touch_backend.attached && wintouch_enabled) {
        bool local_cells[WACCON_IO_TOUCH_CELLS];
        size_t i;
        memset(local_cells, 0, sizeof(local_cells));
        waccon_touch_poll(&touch_backend, local_cells);
        for (i = 0; i < WACCON_IO_TOUCH_CELLS; i++) touch_cells[i] = touch_cells[i] || local_cells[i];
    }
    else if (mouse_enabled && game_window != NULL) waccon_mouse_poll(game_window, touch_cells);
    if (game_window != NULL) waccon_cursor_update();
    if (now - last_log >= 1000) {
        waccon_log("poll alive hwnd=%p wintouch_attached=%d cursor=%d\n", game_window, touch_backend.attached, cursor_enabled);
        last_log = now;
    }
    return S_OK;
}

void mercury_io_get_opbtns(uint8_t *opbtn) { waccon_state_get_buttons(&waccon, opbtn, NULL); }
void mercury_io_get_gamebtns(uint8_t *gamebtn) { waccon_state_get_buttons(&waccon, NULL, gamebtn); }
HRESULT mercury_io_touch_init(void)
{
    waccon_log("mercury_io_touch_init\n");
    return S_OK;
}

void mercury_io_touch_start(mercury_io_touch_callback_t callback)
{
    unsigned int thread_id;
    if (callback == NULL) {
        waccon_log("mercury_io_touch_start ignored null callback\n");
        return;
    }
    waccon_log("mercury_io_touch_start callback=%p\n", callback);
    EnterCriticalSection(&waccon.touch_lock);
    waccon.touch_callback = callback;
    LeaveCriticalSection(&waccon.touch_lock);
    if (InterlockedCompareExchange(&waccon.touch_running, 1, 0) != 0) return;
    InterlockedExchange(&waccon.touch_stop, 0);
    touch_thread = (HANDLE) _beginthreadex(NULL, 0, waccon_touch_thread_proc, &waccon, 0, &thread_id);
    if (touch_thread == NULL) {
        InterlockedExchange(&waccon.touch_running, 0);
        waccon_log("touch callback thread creation failed error=%lu\n", GetLastError());
    } else {
        waccon_log("touch callback thread started id=%u handle=%p\n", thread_id, touch_thread);
    }
}

void mercury_io_touch_set_leds(struct waccon_led_data data)
{
    static uint64_t last_log;
    uint64_t now = GetTickCount64();
    if (now - last_log >= 1000) {
        waccon_log("LED callback alive unitCount=%lu\n", data.unitCount);
        last_log = now;
    }
    waccon_state_publish_leds(&waccon, &data);
}
