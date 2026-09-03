#include "waccon_state.h"

#include <string.h>

void waccon_state_init(struct waccon_state *state)
{
    memset(state, 0, sizeof(*state));
    InitializeCriticalSection(&state->touch_lock);
}

void waccon_state_destroy(struct waccon_state *state)
{
    DeleteCriticalSection(&state->touch_lock);
}

void waccon_state_set_buttons(struct waccon_state *state, uint8_t opbtn, uint8_t gamebtn)
{
    InterlockedExchange(&state->opbtn, opbtn);
    InterlockedExchange(&state->gamebtn, gamebtn);
}

void waccon_state_get_buttons(const struct waccon_state *state, uint8_t *opbtn, uint8_t *gamebtn)
{
    if (opbtn != NULL) {
        *opbtn = (uint8_t) InterlockedCompareExchange((LONG *) &state->opbtn, 0, 0);
    }

    if (gamebtn != NULL) {
        *gamebtn = (uint8_t) InterlockedCompareExchange((LONG *) &state->gamebtn, 0, 0);
    }
}
