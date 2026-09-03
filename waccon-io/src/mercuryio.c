#include <process.h>
#include <string.h>

#include "waccon/mercuryio.h"
#include "waccon_state.h"

static struct waccon_state waccon;
static HANDLE touch_thread;
static bool touch_cells[WACCON_IO_TOUCH_CELLS];

static unsigned int __stdcall waccon_touch_thread_proc(void *ctx)
{
    struct waccon_state *state = ctx;
    mercury_io_touch_callback_t callback;

    for (;;) {
        EnterCriticalSection(&state->touch_lock);
        callback = state->touch_callback;
        LeaveCriticalSection(&state->touch_lock);

        if (InterlockedCompareExchange(&state->touch_stop, 0, 0) != 0) {
            break;
        }

        if (callback != NULL) {
            callback(touch_cells);
        }

        Sleep(1);
    }

    InterlockedExchange(&state->touch_running, 0);
    return 0;
}

uint16_t mercury_io_get_api_version(void)
{
    return WACCON_IO_API_VERSION;
}

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
    /* Providers will publish into this state in later phases. */
    return S_OK;
}

void mercury_io_get_opbtns(uint8_t *opbtn)
{
    waccon_state_get_buttons(&waccon, opbtn, NULL);
}

void mercury_io_get_gamebtns(uint8_t *gamebtn)
{
    waccon_state_get_buttons(&waccon, NULL, gamebtn);
}

HRESULT mercury_io_touch_init(void)
{
    return S_OK;
}

void mercury_io_touch_start(mercury_io_touch_callback_t callback)
{
    unsigned int thread_id;

    if (callback == NULL) {
        return;
    }

    EnterCriticalSection(&waccon.touch_lock);
    waccon.touch_callback = callback;
    LeaveCriticalSection(&waccon.touch_lock);

    if (InterlockedCompareExchange(&waccon.touch_running, 1, 0) != 0) {
        return;
    }

    InterlockedExchange(&waccon.touch_stop, 0);
    touch_thread = (HANDLE) _beginthreadex(
        NULL,
        0,
        waccon_touch_thread_proc,
        &waccon,
        0,
        &thread_id);

    if (touch_thread == NULL) {
        InterlockedExchange(&waccon.touch_running, 0);
    }
}

void mercury_io_touch_set_leds(struct waccon_led_data data)
{
    /* LED snapshot storage is introduced with the IPC phase. */
    (void) data;
}
