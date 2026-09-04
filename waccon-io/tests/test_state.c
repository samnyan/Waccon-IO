#include <assert.h>
#include <stdint.h>

#include "waccon/shared_memory.h"
#include "waccon/mercuryio.h"
#include "waccon_io4.h"
#include "waccon_state.h"
#include "waccon_touch.h"

int main(void)
{
    struct waccon_state state;
    struct waccon_led_data leds = {0};
    struct waccon_touch_mapping mapping = {
        .width = 1080,
        .height = 1920,
        .center_x = 0.5f,
        .center_y = 0.5f,
        .radius = 1.0f,
        .inner_radius = 0.6f,
    };
    uint8_t opbtn = 0;
    uint8_t gamebtn = 0;

    assert(sizeof(struct waccon_shm_input) == 262);
    assert(sizeof(struct waccon_shm_output) == 1932);

    waccon_state_init(&state);
    waccon_state_set_buttons(&state, 0x05, 0x03);
    waccon_state_get_buttons(&state, &opbtn, &gamebtn);
    assert(opbtn == 0x05);
    assert(gamebtn == 0x03);

    leds.unitCount = 2;
    leds.rgba[0] = 0x11;
    leds.rgba[4] = 0x22;
    waccon_state_publish_leds(&state, &leds);
    assert(state.output.unit_count == 2);
    assert(state.output.rgba[0] == 0x11);
    assert(state.output.rgba[4] == 0x22);

    waccon_state_set_buttons(&state, 0, 0);
    waccon_state_get_buttons(&state, &opbtn, &gamebtn);
    assert(opbtn == 0);
    assert(gamebtn == 0);

    waccon_io4_map_buttons(true, false, true, false, true, &opbtn, &gamebtn);
    assert(opbtn == (MERCURY_IO_OPBTN_TEST | MERCURY_IO_OPBTN_COIN));
    assert(gamebtn == MERCURY_IO_GAMEBTN_VOL_DOWN);

    waccon_io4_map_buttons(false, true, false, true, false, &opbtn, &gamebtn);
    assert(opbtn == MERCURY_IO_OPBTN_SERVICE);
    assert(gamebtn == MERCURY_IO_GAMEBTN_VOL_UP);

    assert(!waccon_touch_mapping_contains(&mapping, 0.5f, 0.5f));
    assert(waccon_touch_mapping_cell(&mapping, 0.9f, 0.5f) == 75);
    assert(waccon_touch_mapping_cell(&mapping, 0.1f, 0.5f) == 195);
    assert(waccon_touch_mapping_cell(&mapping, 0.5f, 0.1f) == -1);

    waccon_state_destroy(&state);
    return 0;
}
