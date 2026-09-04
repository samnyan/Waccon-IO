#include <process.h>
#include <string.h>

#include "waccon/mercuryio.h"
#include "waccon_state.h"
#include "waccon_touch.h"
#include "waccon_log.h"
#include "waccon_io4.h"

static struct waccon_state waccon;
static struct waccon_touch_backend touch_backend;
static struct waccon_touch_mapping touch_mapping = {
    .left = 0,
    .top = 0,
    .width = 1,
    .height = 1,
    .center_x = 0.50f,
    .center_y = 0.50f,
    .radius = 1.00f,
    .inner_radius = 0.60f,
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

static struct waccon_io4_config io4_config;
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
    waccon_io4_config_load(&io4_config, L".\\segatools.ini");
    waccon_log("io4 keys test=0x%02X service=0x%02X coin=0x%02X volup=0x%02X voldown=0x%02X\n",
        io4_config.vk_test, io4_config.vk_service, io4_config.vk_coin,
        io4_config.vk_vol_up, io4_config.vk_vol_down);
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
    int old_show_count = -1;
    int attempts = 0;
    int show_count = -1;

    if (!cursor_enabled) return;
    info.cbSize = sizeof(info);
    if (!GetCursorInfo(&info)) {
        waccon_log("cursor GetCursorInfo failed error=%lu\n", GetLastError());
        return;
    }
    if ((info.flags & CURSOR_SHOWING) == 0) {
        for (attempts = 0; attempts < 16; attempts++) {
            old_show_count = show_count;
            show_count = ShowCursor(TRUE);
            if (show_count >= 0) break;
        }
        waccon_log("cursor was hidden, ShowCursor(TRUE) old=%d new=%d attempts=%d\n", old_show_count, show_count, attempts + 1);
    }
    cursor = LoadCursorW(NULL, MAKEINTRESOURCEW(32512));
    if (cursor != NULL) SetCursor(cursor);
}

static uint32_t window_scan_attempts;
static uint64_t last_window_scan_ms;

static void waccon_collect_touch_cells(bool cells[WACCON_IO_TOUCH_CELLS])
{
    bool local_cells[WACCON_IO_TOUCH_CELLS];
    size_t i;

    waccon_state_get_touch(&waccon, cells);
    if (touch_backend.attached && wintouch_enabled) {
        waccon_touch_poll(&touch_backend, local_cells);
        for (i = 0; i < WACCON_IO_TOUCH_CELLS; i++) cells[i] = cells[i] || local_cells[i];
    } else if (mouse_enabled && game_window != NULL) {
        waccon_mouse_poll(game_window, local_cells);
        for (i = 0; i < WACCON_IO_TOUCH_CELLS; i++) cells[i] = cells[i] || local_cells[i];
    }
}

static void waccon_ensure_touch_window(void)
{
    RECT rect;
    uint64_t now = GetTickCount64();
    if (touch_backend.attached && (!IsWindow(touch_backend.hwnd) || !IsWindowVisible(touch_backend.hwnd))) {
        waccon_log("attached window disappeared, resetting hwnd=%p\n", touch_backend.hwnd);
        waccon_touch_detach(&touch_backend);
        game_window = NULL;
    }
    if (touch_backend.attached || !wintouch_enabled) return;
    if (now - last_window_scan_ms < 100) return;
    last_window_scan_ms = now;
    window_scan_attempts++;
    if (game_window == NULL) game_window = waccon_find_window();
    if (game_window == NULL || !GetClientRect(game_window, &rect)) {
        if (window_scan_attempts == 1 || window_scan_attempts % 10 == 0) {
            waccon_log("game window not ready, scan attempt=%lu\n", window_scan_attempts);
        }
        game_window = NULL;
        return;
    }
    touch_mapping.width = rect.right - rect.left;
    touch_mapping.height = rect.bottom - rect.top;
    waccon_log("attempting WinTouch attach hwnd=%p client=%ldx%ld attempt=%lu\n",
        game_window, touch_mapping.width, touch_mapping.height, window_scan_attempts);
    if (SUCCEEDED(waccon_touch_attach(&touch_backend, game_window, &touch_mapping))) {
        waccon_log("WinTouch attached\n");
        waccon_touch_poll(&touch_backend, touch_cells);
    } else {
        waccon_log("WinTouch attach failed error=%lu\n", GetLastError());
        game_window = NULL;
    }
}

static unsigned int __stdcall waccon_touch_thread_proc(void *ctx)
{
    struct waccon_state *state = ctx;
    mercury_io_touch_callback_t callback;
    bool callback_cells[WACCON_IO_TOUCH_CELLS];
    for (;;) {
        EnterCriticalSection(&state->touch_lock);
        callback = state->touch_callback;
        LeaveCriticalSection(&state->touch_lock);
        if (InterlockedCompareExchange(&state->touch_stop, 0, 0) != 0) break;
        waccon_ensure_touch_window();
        waccon_collect_touch_cells(callback_cells);
        if (callback != NULL) callback(callback_cells);
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
    static uint8_t last_keyboard_opbtn;
    static uint8_t last_keyboard_gamebtn;
    uint8_t keyboard_opbtn;
    uint8_t keyboard_gamebtn;
    uint64_t now = GetTickCount64();
    waccon_state_poll(&waccon);
    waccon_io4_poll(&io4_config, &keyboard_opbtn, &keyboard_gamebtn);
    if (keyboard_opbtn != last_keyboard_opbtn || keyboard_gamebtn != last_keyboard_gamebtn) {
        waccon_log("io4 keyboard opbtn=0x%02X gamebtn=0x%02X\n", keyboard_opbtn, keyboard_gamebtn);
        last_keyboard_opbtn = keyboard_opbtn;
        last_keyboard_gamebtn = keyboard_gamebtn;
    }
    waccon_ensure_touch_window();
    waccon_collect_touch_cells(touch_cells);
    if (game_window != NULL) waccon_cursor_update();
    if (now - last_log >= 1000) {
        waccon_log("poll alive hwnd=%p wintouch_attached=%d cursor=%d\n", game_window, touch_backend.attached, cursor_enabled);
        last_log = now;
    }
    return S_OK;
}

void mercury_io_get_opbtns(uint8_t *opbtn)
{
    uint8_t shared = 0;
    uint8_t keyboard = 0;
    waccon_state_get_buttons(&waccon, &shared, NULL);
    waccon_io4_poll(&io4_config, &keyboard, NULL);
    if (opbtn != NULL) *opbtn = shared | keyboard;
}

void mercury_io_get_gamebtns(uint8_t *gamebtn)
{
    uint8_t shared = 0;
    uint8_t keyboard = 0;
    waccon_state_get_buttons(&waccon, NULL, &shared);
    waccon_io4_poll(&io4_config, NULL, &keyboard);
    if (gamebtn != NULL) *gamebtn = shared | keyboard;
}
HRESULT mercury_io_touch_init(void)
{
    waccon_log("mercury_io_touch_init\n");
    waccon_ensure_touch_window();
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
