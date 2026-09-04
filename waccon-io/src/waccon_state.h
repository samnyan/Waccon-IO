#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <windows.h>

#include "waccon/mercuryio.h"
#include "waccon/shared_memory.h"

struct waccon_state {
    volatile LONG opbtn;
    volatile LONG gamebtn;
    volatile LONG touch_running;
    volatile LONG touch_stop;
    mercury_io_touch_callback_t touch_callback;
    CRITICAL_SECTION touch_lock;
    CRITICAL_SECTION input_lock;
    CRITICAL_SECTION output_lock;
    struct waccon_shm_input input;
    struct waccon_shm_output output;
    uint64_t input_deadline_ms;
    uint64_t started_ms;
    uint32_t last_input_sequence;
    HANDLE shm_mapping;
    struct waccon_shm *shm;
};

void waccon_state_init(struct waccon_state *state);
void waccon_state_destroy(struct waccon_state *state);
void waccon_state_set_buttons(struct waccon_state *state, uint8_t opbtn, uint8_t gamebtn);
void waccon_state_get_buttons(const struct waccon_state *state, uint8_t *opbtn, uint8_t *gamebtn);
void waccon_state_get_touch(const struct waccon_state *state, bool cells[WACCON_IO_TOUCH_CELLS]);
void waccon_state_poll(struct waccon_state *state);
void waccon_state_publish_leds(struct waccon_state *state, const struct waccon_led_data *data);
