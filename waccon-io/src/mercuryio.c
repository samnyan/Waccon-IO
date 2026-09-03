#include <process.h>
#include <string.h>

#include "waccon/mercuryio.h"
#include "waccon_state.h"
#include "waccon_touch.h"

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

static HWND waccon_find_window(void)
{
    HWND hwnd = FindWindowW(NULL, L"Mercury  ");
    if (hwnd == NULL) hwnd = FindWindowW(NULL, L"WACCA");
    return hwnd;
}

static void waccon_ensure_touch_window(void)
{
    RECT rect;
    if (touch_backend.attached) return;
    if (game_window == NULL) game_window = waccon_find_window();
    if (game_window == NULL || !GetClientRect(game_window, &rect)) return;
    touch_mapping.width = rect.right - rect.left;
    touch_mapping.height = rect.bottom - rect.top;
    if (SUCCEEDED(waccon_touch_attach(&touch_backend, game_window, &touch_mapping))) {
        waccon_touch_poll(&touch_backend, touch_cells);
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

uint16_t mercury_io_get_api_version(void) { return WACCON_IO_API_VERSION; }

HRESULT mercury_io_init(void)
{
    static LONG initialized;
    if (InterlockedCompareExchange(&initialized, 1, 0) == 0) {
        waccon_state_init(&waccon);
        memset(touch_cells, 0, sizeof(touch_cells));
    }
    return S_OK;
}

HRESULT mercury_io_poll(void)
{
    waccon_state_poll(&waccon);
    waccon_ensure_touch_window();
    if (touch_backend.attached) waccon_touch_poll(&touch_backend, touch_cells);
    else if (game_window != NULL) waccon_mouse_poll(game_window, touch_cells);
    return S_OK;
}

void mercury_io_get_opbtns(uint8_t *opbtn) { waccon_state_get_buttons(&waccon, opbtn, NULL); }
void mercury_io_get_gamebtns(uint8_t *gamebtn) { waccon_state_get_buttons(&waccon, NULL, gamebtn); }
HRESULT mercury_io_touch_init(void) { return S_OK; }

void mercury_io_touch_start(mercury_io_touch_callback_t callback)
{
    unsigned int thread_id;
    if (callback == NULL) return;
    EnterCriticalSection(&waccon.touch_lock);
    waccon.touch_callback = callback;
    LeaveCriticalSection(&waccon.touch_lock);
    if (InterlockedCompareExchange(&waccon.touch_running, 1, 0) != 0) return;
    InterlockedExchange(&waccon.touch_stop, 0);
    touch_thread = (HANDLE) _beginthreadex(NULL, 0, waccon_touch_thread_proc, &waccon, 0, &thread_id);
    if (touch_thread == NULL) InterlockedExchange(&waccon.touch_running, 0);
}

void mercury_io_touch_set_leds(struct waccon_led_data data)
{
    waccon_state_publish_leds(&waccon, &data);
}
