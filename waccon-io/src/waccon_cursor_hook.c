#include <windows.h>
#include <stdint.h>
#include <string.h>

#include "waccon_cursor_hook.h"
#include "waccon_log.h"

typedef HCURSOR (WINAPI *waccon_set_cursor_fn)(HCURSOR cursor);

static waccon_set_cursor_fn waccon_set_cursor;
static HCURSOR waccon_arrow_cursor;
static bool waccon_cursor_enabled;
static LONG waccon_cursor_hook_installed;
static uint64_t waccon_last_null_log_ms;

static HCURSOR WINAPI waccon_hook_set_cursor(HCURSOR cursor)
{
    uint64_t now;

    if (waccon_cursor_enabled && cursor == NULL) {
        if (waccon_arrow_cursor == NULL) {
            waccon_arrow_cursor = LoadCursorW(NULL, MAKEINTRESOURCEW(32512));
        }
        if (waccon_arrow_cursor != NULL) {
            now = GetTickCount64();
            if (now - waccon_last_null_log_ms >= 1000) {
                waccon_log("cursor SetCursor(NULL) intercepted; restoring IDC_ARROW\n");
                waccon_last_null_log_ms = now;
            }
            cursor = waccon_arrow_cursor;
        }
    }

    return waccon_set_cursor != NULL ? waccon_set_cursor(cursor) : NULL;
}

static bool waccon_cursor_hook_patch_main_module(void)
{
    HMODULE module = GetModuleHandleW(NULL);
    uint8_t *base = (uint8_t *) module;
    IMAGE_DOS_HEADER *dos_header;
    IMAGE_NT_HEADERS *nt_header;
    IMAGE_DATA_DIRECTORY import_directory;
    IMAGE_IMPORT_DESCRIPTOR *descriptor;

    if (module == NULL) return false;
    dos_header = (IMAGE_DOS_HEADER *) base;
    if (dos_header->e_magic != IMAGE_DOS_SIGNATURE) return false;
    nt_header = (IMAGE_NT_HEADERS *) (base + dos_header->e_lfanew);
    if (nt_header->Signature != IMAGE_NT_SIGNATURE) return false;
    import_directory = nt_header->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (import_directory.VirtualAddress == 0) return false;

    descriptor = (IMAGE_IMPORT_DESCRIPTOR *) (base + import_directory.VirtualAddress);
    for (; descriptor->Name != 0; descriptor++) {
        IMAGE_THUNK_DATA *name_thunk;
        IMAGE_THUNK_DATA *iat_thunk;
        const char *import_name = (const char *) (base + descriptor->Name);

        if (_stricmp(import_name, "user32.dll") != 0 || descriptor->OriginalFirstThunk == 0) continue;
        name_thunk = (IMAGE_THUNK_DATA *) (base + descriptor->OriginalFirstThunk);
        iat_thunk = (IMAGE_THUNK_DATA *) (base + descriptor->FirstThunk);
        for (; name_thunk->u1.AddressOfData != 0; name_thunk++, iat_thunk++) {
            IMAGE_IMPORT_BY_NAME *function_name;
            DWORD old_protect;

            if (IMAGE_SNAP_BY_ORDINAL(name_thunk->u1.Ordinal)) continue;
            function_name = (IMAGE_IMPORT_BY_NAME *) (base + name_thunk->u1.AddressOfData);
            if (strcmp((const char *) function_name->Name, "SetCursor") != 0) continue;
            if ((PVOID) (uintptr_t) iat_thunk->u1.Function == (PVOID) waccon_hook_set_cursor) return true;
            if (!VirtualProtect(&iat_thunk->u1.Function, sizeof(iat_thunk->u1.Function), PAGE_READWRITE, &old_protect)) {
                waccon_log("cursor hook VirtualProtect failed error=%lu\n", GetLastError());
                return false;
            }
            InterlockedExchangePointer((PVOID volatile *) &iat_thunk->u1.Function, (PVOID) waccon_hook_set_cursor);
            VirtualProtect(&iat_thunk->u1.Function, sizeof(iat_thunk->u1.Function), old_protect, &old_protect);
            FlushInstructionCache(GetCurrentProcess(), &iat_thunk->u1.Function, sizeof(iat_thunk->u1.Function));
            return true;
        }
    }

    return false;
}

void waccon_cursor_hook_set_enabled(bool enabled)
{
    waccon_cursor_enabled = enabled;
}

void waccon_cursor_hook_install(void)
{
    FARPROC set_cursor_proc;

    if (InterlockedCompareExchange(&waccon_cursor_hook_installed, 1, 0) != 0) return;
    set_cursor_proc = GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetCursor");
    if (set_cursor_proc == NULL) {
        waccon_log("cursor hook could not resolve SetCursor error=%lu\n", GetLastError());
        return;
    }
    waccon_set_cursor = (waccon_set_cursor_fn) set_cursor_proc;
    if (waccon_cursor_hook_patch_main_module()) {
        waccon_log("cursor hook installed for game main module\n");
    } else {
        waccon_log("cursor hook could not find a SetCursor import in game main module\n");
    }
}
