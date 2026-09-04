#include "waccon_io4.h"

#include "waccon/mercuryio.h"

void waccon_io4_config_load(struct waccon_io4_config *config, const wchar_t *filename)
{
    if (config == NULL || filename == NULL) return;
    config->vk_test = (uint8_t)GetPrivateProfileIntW(L"io4", L"test", VK_F1, filename);
    config->vk_service = (uint8_t)GetPrivateProfileIntW(L"io4", L"service", VK_F2, filename);
    config->vk_coin = (uint8_t)GetPrivateProfileIntW(L"io4", L"coin", VK_F3, filename);
    config->vk_vol_up = (uint8_t)GetPrivateProfileIntW(L"io4", L"volup", VK_UP, filename);
    config->vk_vol_down = (uint8_t)GetPrivateProfileIntW(L"io4", L"voldown", VK_DOWN, filename);
}

void waccon_io4_map_buttons(
    bool test_pressed,
    bool service_pressed,
    bool coin_pressed,
    bool vol_up_pressed,
    bool vol_down_pressed,
    uint8_t *opbtn,
    uint8_t *gamebtn)
{
    uint8_t operator_buttons = 0;
    uint8_t game_buttons = 0;

    if (test_pressed) operator_buttons |= MERCURY_IO_OPBTN_TEST;
    if (service_pressed) operator_buttons |= MERCURY_IO_OPBTN_SERVICE;
    if (coin_pressed) operator_buttons |= MERCURY_IO_OPBTN_COIN;
    if (vol_up_pressed) game_buttons |= MERCURY_IO_GAMEBTN_VOL_UP;
    if (vol_down_pressed) game_buttons |= MERCURY_IO_GAMEBTN_VOL_DOWN;

    if (opbtn != NULL) *opbtn = operator_buttons;
    if (gamebtn != NULL) *gamebtn = game_buttons;
}

void waccon_io4_poll(const struct waccon_io4_config *config, uint8_t *opbtn, uint8_t *gamebtn)
{
    if (config == NULL) return;
    waccon_io4_map_buttons(
        (GetAsyncKeyState(config->vk_test) & 0x8000) != 0,
        (GetAsyncKeyState(config->vk_service) & 0x8000) != 0,
        (GetAsyncKeyState(config->vk_coin) & 0x8000) != 0,
        (GetAsyncKeyState(config->vk_vol_up) & 0x8000) != 0,
        (GetAsyncKeyState(config->vk_vol_down) & 0x8000) != 0,
        opbtn,
        gamebtn);
}
