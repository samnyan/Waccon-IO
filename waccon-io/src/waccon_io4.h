#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <windows.h>

struct waccon_io4_config {
    uint8_t vk_test;
    uint8_t vk_service;
    uint8_t vk_coin;
    uint8_t vk_vol_up;
    uint8_t vk_vol_down;
};

void waccon_io4_config_load(struct waccon_io4_config *config, const wchar_t *filename);
void waccon_io4_map_buttons(
    bool test_pressed,
    bool service_pressed,
    bool coin_pressed,
    bool vol_up_pressed,
    bool vol_down_pressed,
    uint8_t *opbtn,
    uint8_t *gamebtn);
void waccon_io4_poll(const struct waccon_io4_config *config, uint8_t *opbtn, uint8_t *gamebtn);
