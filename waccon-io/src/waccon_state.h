#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <windows.h>

#include "waccon/mercuryio.h"

struct waccon_state {
    volatile LONG opbtn;
    volatile LONG gamebtn;
    volatile LONG touch_running;
    volatile LONG touch_stop;
    mercury_io_touch_callback_t touch_callback;
    CRITICAL_SECTION touch_lock;
};

void waccon_state_init(struct waccon_state *state);
void waccon_state_destroy(struct waccon_state *state);
void waccon_state_set_buttons(struct waccon_state *state, uint8_t opbtn, uint8_t gamebtn);
void waccon_state_get_buttons(const struct waccon_state *state, uint8_t *opbtn, uint8_t *gamebtn);
