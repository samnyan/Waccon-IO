#include "waccon_log.h"

#include <stdarg.h>
#include <stdio.h>
#include <windows.h>

static bool log_enabled = true;
static bool console_ready;
static CRITICAL_SECTION log_lock;
static LONG log_lock_state;

static void waccon_log_lock_init(void)
{
    if (InterlockedCompareExchange(&log_lock_state, 1, 0) == 0) {
        InitializeCriticalSection(&log_lock);
        InterlockedExchange(&log_lock_state, 2);
    } else {
        while (InterlockedCompareExchange(&log_lock_state, 2, 2) != 2) Sleep(0);
    }
}

void waccon_log_set_enabled(bool enabled)
{
    log_enabled = enabled;
}

bool waccon_log_is_enabled(void)
{
    return log_enabled;
}

void waccon_log_open_console(bool enabled)
{
    if (!enabled || console_ready) return;
    if (GetConsoleWindow() == NULL) {
        if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
    }
    if (GetConsoleWindow() != NULL) {
        FILE *stream;
        freopen_s(&stream, "CONOUT$", "w", stdout);
        freopen_s(&stream, "CONOUT$", "w", stderr);
        console_ready = true;
    }
}

void waccon_log(const char *format, ...)
{
    char message[1024];
    char line[1100];
    va_list args;
    int length;

    if (!log_enabled || format == NULL) return;
    waccon_log_lock_init();
    va_start(args, format);
    _vsnprintf_s(message, sizeof(message), _TRUNCATE, format, args);
    va_end(args);
    length = _snprintf_s(line, sizeof(line), _TRUNCATE, "Waccon IO: %s", message);
    if (length < 0) return;

    EnterCriticalSection(&log_lock);
    if (console_ready) {
        DWORD written;
        WriteConsoleA(GetStdHandle(STD_ERROR_HANDLE), line, (DWORD)length, &written, NULL);
    } else {
        OutputDebugStringA(line);
    }
    LeaveCriticalSection(&log_lock);
}

void waccon_log_window_event(const char *event, HWND hwnd, UINT message, int x, int y)
{
    waccon_log("window-hook event=%s hwnd=%p msg=0x%04X client=(%d,%d)\n",
        event != NULL ? event : "unknown", hwnd, message, x, y);
}
