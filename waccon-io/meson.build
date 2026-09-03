project('waccon-io', 'c', version : '0.1.0', default_options : ['warning_level=3'])

cc = meson.get_compiler('c')
inc = include_directories('include')

waccon_io_lib = shared_library(
    'waccon_io',
    sources : [
        'src/mercuryio.c',
        'include/waccon/mercuryio.h',
        'include/waccon/shared_memory.h',
        'src/waccon_state.c',
        'src/waccon_state.h',
        'src/waccon_touch.c',
        'src/waccon_touch.h',
    ],
    include_directories : inc,
    vs_module_defs : 'waccon_io.def',
    name_prefix : '',
    install : false,
)

test_exe = executable(
    'waccon_io_test',
    sources : [
        'tests/test_state.c',
        'src/waccon_state.c',
        'src/waccon_state.h',
        'src/waccon_touch.c',
        'src/waccon_touch.h',
    ],
    include_directories : [inc, include_directories('src')],
)

test('waccon_io_state', test_exe)
