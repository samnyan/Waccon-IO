#pragma once

#include <stdbool.h>
#include <stdint.h>
#include <windows.h>

#define WACCON_MAX_CONTACTS 16

enum waccon_touch_phase {
    WACCON_TOUCH_DOWN = 1,
    WACCON_TOUCH_MOVE = 2,
    WACCON_TOUCH_UP = 3,
    WACCON_TOUCH_CANCEL = 4,
};

struct waccon_touch_contact {
    uint32_t id;
    enum waccon_touch_phase phase;
    float x;
    float y;
    float pressure;
};

struct waccon_touch_mapping {
    int32_t left;
    int32_t top;
    int32_t width;
    int32_t height;
    float center_x;
    float center_y;
    float radius;
    float inner_radius;
    float start_angle;
    float end_angle;
    uint8_t side;
    uint8_t reverse;
};

struct waccon_touch_backend {
    HWND hwnd;
    WNDPROC original_wndproc;
    CRITICAL_SECTION lock;
    struct waccon_touch_contact contacts[WACCON_MAX_CONTACTS];
    uint32_t contact_count;
    bool attached;
};

bool waccon_touch_mapping_contains(const struct waccon_touch_mapping *mapping, float x, float y);
int waccon_touch_mapping_cell(const struct waccon_touch_mapping *mapping, float x, float y);
void waccon_touch_mapping_config_load(struct waccon_touch_mapping *mapping, const wchar_t *filename);
void waccon_touch_set_mapping(const struct waccon_touch_mapping *mapping);
void waccon_touch_clear(struct waccon_touch_backend *backend);
HRESULT waccon_touch_attach(struct waccon_touch_backend *backend, HWND hwnd, const struct waccon_touch_mapping *mapping);
void waccon_touch_detach(struct waccon_touch_backend *backend);
void waccon_touch_poll(struct waccon_touch_backend *backend, bool cells[240]);
void waccon_mouse_poll(HWND hwnd, bool cells[240]);
LRESULT CALLBACK waccon_touch_wndproc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam);
