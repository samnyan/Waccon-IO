#include <assert.h>
#include <stdint.h>
#include <wchar.h>

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
        .start_angle = -1.570796327f,
        .end_angle = 1.570796327f,
    };
    uint8_t opbtn = 0;
    uint8_t gamebtn = 0;
    wchar_t temporary_path[MAX_PATH];

    assert(sizeof(struct waccon_shm_input) == 262);
    assert(sizeof(struct waccon_shm_output) == 1932);
    assert(sizeof(struct waccon_shm_endpoint) == 32);
    assert(sizeof(struct waccon_shm) == 2282);

    waccon_state_init(&state);
    if (state.shm != NULL) {
        assert(state.shm->io.process_id == GetCurrentProcessId());
        assert(state.shm->io.protocol_major == WACCON_SHM_MAJOR);
        assert(state.shm->io.protocol_minor == WACCON_SHM_MINOR);
    }
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

    assert(GetTempPathW(_countof(temporary_path), temporary_path) > 0);
    assert(GetTempFileNameW(temporary_path, L"wcc", 0, temporary_path) != 0);
    assert(WritePrivateProfileStringW(L"waccon", L"centerX", L"0.48", temporary_path));
    assert(WritePrivateProfileStringW(L"waccon", L"centerY", L"0.47", temporary_path));
    assert(WritePrivateProfileStringW(L"waccon", L"radius", L"1.05", temporary_path));
    assert(WritePrivateProfileStringW(L"waccon", L"innerRadius", L"0.58", temporary_path));
    assert(WritePrivateProfileStringW(L"waccon", L"startAngle", L"-84", temporary_path));
    assert(WritePrivateProfileStringW(L"waccon", L"reverse", L"1", temporary_path));
    waccon_touch_mapping_config_load(&mapping, temporary_path);
    assert(mapping.center_x == 0.48f);
    assert(mapping.center_y == 0.47f);
    assert(mapping.radius == 1.05f);
    assert(mapping.inner_radius == 0.58f);
    assert(mapping.reverse == 1);
    assert(mapping.start_angle < -1.46f && mapping.start_angle > -1.47f);
    DeleteFileW(temporary_path);

    waccon_state_destroy(&state);
    return 0;
}
