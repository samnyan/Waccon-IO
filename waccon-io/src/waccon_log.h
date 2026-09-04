#pragma once

#include <stdbool.h>
#include <windows.h>

void waccon_log_set_enabled(bool enabled);
bool waccon_log_is_enabled(void);
void waccon_log_open_console(bool enabled);
void waccon_log(const char *format, ...);
void waccon_log_window_event(const char *event, HWND hwnd, UINT message, int x, int y);
