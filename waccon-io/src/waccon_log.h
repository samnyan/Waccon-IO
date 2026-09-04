#pragma once

#include <stdbool.h>

void waccon_log_set_enabled(bool enabled);
bool waccon_log_is_enabled(void);
void waccon_log_open_console(bool enabled);
void waccon_log(const char *format, ...);
