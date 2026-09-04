#pragma once

#include <stdint.h>

#define WACCON_SHM_MAGIC 0x4E4F4357u /* "WCON" in little-endian */
#define WACCON_SHM_MAJOR 1u
#define WACCON_SHM_MINOR 1u
#define WACCON_SHM_NAME "Local\\WACCON_SHARED_BUFFER"
#define WACCON_SHM_MAX_LEDS 480u
#define WACCON_SHM_TOUCH_CELLS 240u

#define WACCON_SHM_CAP_INPUT 0x00000001u
#define WACCON_SHM_CAP_LED_OUTPUT 0x00000002u
#define WACCON_SHM_CAP_TOUCH_CONTACTS 0x00000004u
#define WACCON_SHM_CAP_SERVER_STATUS 0x00000008u
#define WACCON_SHM_CAP_IO_STATUS 0x00000010u
#define WACCON_SHM_DEFAULT_LEASE_MS 500u

#pragma pack(push, 1)
struct waccon_shm_input {
    uint8_t opbtn;
    uint8_t gamebtn;
    uint8_t touch_cells[WACCON_SHM_TOUCH_CELLS];
    uint32_t source_id;
    uint64_t timestamp_us;
    uint64_t lease_ms;
};

struct waccon_shm_output {
    uint32_t unit_count;
    uint8_t rgba[WACCON_SHM_MAX_LEDS * 4];
    uint64_t timestamp_us;
};

/* Each process owns and updates only its own endpoint record. Times are
 * GetTickCount64 milliseconds, so they can be compared across local processes. */
struct waccon_shm_endpoint {
    uint32_t process_id;
    uint16_t protocol_major;
    uint16_t protocol_minor;
    uint64_t started_ms;
    uint64_t heartbeat_ms;
    uint64_t input_sequence;
};

struct waccon_shm {
    uint32_t magic;
    uint16_t major;
    uint16_t minor;
    uint32_t struct_size;
    uint32_t capabilities;
    volatile uint32_t input_sequence;
    volatile uint32_t output_sequence;
    struct waccon_shm_input input;
    struct waccon_shm_output output;
    struct waccon_shm_endpoint server;
    struct waccon_shm_endpoint io;
};
#pragma pack(pop)
