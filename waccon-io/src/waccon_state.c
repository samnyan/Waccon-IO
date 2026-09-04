#include "waccon_state.h"
#include "waccon_log.h"

#include <string.h>

static uint64_t waccon_now_ms(void)
{
    return GetTickCount64();
}

static bool waccon_shm_is_compatible(const struct waccon_shm *shm)
{
    return shm != NULL &&
        shm->magic == WACCON_SHM_MAGIC &&
        shm->major == WACCON_SHM_MAJOR &&
        shm->minor == WACCON_SHM_MINOR &&
        shm->struct_size == sizeof(struct waccon_shm);
}

static void waccon_shm_update_io_status(struct waccon_state *state)
{
    if (state->shm == NULL) return;
    state->shm->capabilities |= WACCON_SHM_CAP_IO_STATUS;
    state->shm->io.process_id = GetCurrentProcessId();
    state->shm->io.protocol_major = WACCON_SHM_MAJOR;
    state->shm->io.protocol_minor = WACCON_SHM_MINOR;
    state->shm->io.started_ms = state->started_ms;
    state->shm->io.heartbeat_ms = waccon_now_ms();
    state->shm->io.input_sequence = state->last_input_sequence;
}

static void waccon_shm_write_input(struct waccon_state *state)
{
    uint32_t seq;

    if (state->shm == NULL) return;
    seq = state->shm->input_sequence;
    state->shm->input_sequence = seq | 1u;
    memcpy(&state->shm->input, &state->input, sizeof(state->input));
    state->shm->input_sequence = (seq + 2u) & ~1u;
    state->last_input_sequence = state->shm->input_sequence;
    waccon_shm_update_io_status(state);
}

static void waccon_shm_read_input(struct waccon_state *state)
{
    uint32_t before;
    uint32_t after;
    struct waccon_shm_input input;

    if (state->shm == NULL) return;

    before = state->shm->input_sequence;
    if ((before & 1u) != 0 || before == state->last_input_sequence) return;
    memcpy(&input, &state->shm->input, sizeof(input));
    after = state->shm->input_sequence;
    if (before != after || (after & 1u) != 0) return;
    if (input.lease_ms > 0 && input.lease_ms <= 60000) {
        state->input = input;
        state->input_deadline_ms = waccon_now_ms() + input.lease_ms;
        state->last_input_sequence = after;
        state->shm->io.input_sequence = after;
        waccon_log("shared input accepted seq=%lu source=%lu lease=%llums\n",
            after, input.source_id, (unsigned long long) input.lease_ms);
    }
}

void waccon_state_init(struct waccon_state *state)
{
    DWORD mapping_error;
    memset(state, 0, sizeof(*state));
    state->started_ms = waccon_now_ms();
    InitializeCriticalSection(&state->touch_lock);
    InitializeCriticalSection(&state->input_lock);
    InitializeCriticalSection(&state->output_lock);
    state->shm_mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, sizeof(struct waccon_shm), WACCON_SHM_NAME);
    mapping_error = GetLastError();
    if (state->shm_mapping != NULL) {
        state->shm = MapViewOfFile(state->shm_mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(struct waccon_shm));
        if (state->shm != NULL) {
            if (mapping_error != ERROR_ALREADY_EXISTS) {
                memset(state->shm, 0, sizeof(*state->shm));
                state->shm->magic = WACCON_SHM_MAGIC;
                state->shm->major = WACCON_SHM_MAJOR;
                state->shm->minor = WACCON_SHM_MINOR;
                state->shm->struct_size = sizeof(struct waccon_shm);
                state->shm->capabilities = WACCON_SHM_CAP_INPUT | WACCON_SHM_CAP_LED_OUTPUT;
            } else if (!waccon_shm_is_compatible(state->shm)) {
                waccon_log("shared memory ABI mismatch magic=0x%08lX version=%u.%u size=%lu expected=1.1/%zu\n",
                    state->shm->magic, state->shm->major, state->shm->minor,
                    state->shm->struct_size, sizeof(struct waccon_shm));
                UnmapViewOfFile(state->shm);
                state->shm = NULL;
            }
            if (state->shm != NULL) {
                waccon_shm_update_io_status(state);
                waccon_log("shared memory ready created=%d server_pid=%lu server_heartbeat=%llums\n",
                    mapping_error != ERROR_ALREADY_EXISTS,
                    state->shm->server.process_id,
                    (unsigned long long) state->shm->server.heartbeat_ms);
            }
        }
    }
}

void waccon_state_destroy(struct waccon_state *state)
{
    if (state->shm != NULL) UnmapViewOfFile(state->shm);
    if (state->shm_mapping != NULL) CloseHandle(state->shm_mapping);
    DeleteCriticalSection(&state->output_lock);
    DeleteCriticalSection(&state->input_lock);
    DeleteCriticalSection(&state->touch_lock);
}

void waccon_state_set_buttons(struct waccon_state *state, uint8_t opbtn, uint8_t gamebtn)
{
    EnterCriticalSection(&state->input_lock);
    state->input.opbtn = opbtn;
    state->input.gamebtn = gamebtn;
    state->input.timestamp_us = GetTickCount64() * 1000;
    waccon_shm_write_input(state);
    LeaveCriticalSection(&state->input_lock);
}

void waccon_state_get_buttons(const struct waccon_state *state, uint8_t *opbtn, uint8_t *gamebtn)
{
    EnterCriticalSection((LPCRITICAL_SECTION) &state->input_lock);
    if (opbtn != NULL) *opbtn = state->input.opbtn;
    if (gamebtn != NULL) *gamebtn = state->input.gamebtn;
    LeaveCriticalSection((LPCRITICAL_SECTION) &state->input_lock);
}

void waccon_state_get_touch(const struct waccon_state *state, bool cells[WACCON_IO_TOUCH_CELLS])
{
    EnterCriticalSection((LPCRITICAL_SECTION) &state->input_lock);
    memcpy(cells, state->input.touch_cells, WACCON_IO_TOUCH_CELLS);
    LeaveCriticalSection((LPCRITICAL_SECTION) &state->input_lock);
}

void waccon_state_poll(struct waccon_state *state)
{
    uint64_t now = waccon_now_ms();

    EnterCriticalSection(&state->input_lock);
    waccon_shm_update_io_status(state);
    waccon_shm_read_input(state);
    if (state->input_deadline_ms != 0 && now >= state->input_deadline_ms) {
        memset(&state->input, 0, sizeof(state->input));
        state->input_deadline_ms = 0;
        waccon_shm_write_input(state);
    }
    LeaveCriticalSection(&state->input_lock);
}

void waccon_state_publish_leds(struct waccon_state *state, const struct waccon_led_data *data)
{
    uint32_t count;
    uint32_t seq;
    if (data == NULL) return;
    count = data->unitCount > WACCON_SHM_MAX_LEDS ? WACCON_SHM_MAX_LEDS : data->unitCount;
    EnterCriticalSection(&state->output_lock);
    memset(&state->output, 0, sizeof(state->output));
    state->output.unit_count = count;
    memcpy(state->output.rgba, data->rgba, count * 4);
    state->output.timestamp_us = GetTickCount64() * 1000;
    if (state->shm != NULL) {
        seq = state->shm->output_sequence;
        state->shm->output_sequence = seq | 1u;
        memcpy(&state->shm->output, &state->output, sizeof(state->output));
        state->shm->output_sequence = (seq + 2u) & ~1u;
    }
    LeaveCriticalSection(&state->output_lock);
}
