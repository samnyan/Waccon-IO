#include <assert.h>
#include <stdint.h>

#include "waccon_state.h"

int main(void)
{
    struct waccon_state state;
    uint8_t opbtn = 0;
    uint8_t gamebtn = 0;

    waccon_state_init(&state);
    waccon_state_set_buttons(&state, 0x05, 0x03);
    waccon_state_get_buttons(&state, &opbtn, &gamebtn);

    assert(opbtn == 0x05);
    assert(gamebtn == 0x03);

    waccon_state_set_buttons(&state, 0, 0);
    waccon_state_get_buttons(&state, &opbtn, &gamebtn);
    assert(opbtn == 0);
    assert(gamebtn == 0);

    waccon_state_destroy(&state);
    return 0;
}
