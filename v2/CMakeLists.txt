cmake_minimum_required(VERSION 3.24)
project(spice_v2 VERSION 0.1.0 LANGUAGES C CXX)

option(SPICE_V2_BUILD_TESTS "Build Spice V2 unit, audit, and scale tests" ON)
option(SPICE_V2_ENABLE_COVERAGE "Enable GCC/Clang coverage instrumentation on owned targets" OFF)
option(SPICE_V2_BUILD_FUZZER "Build Clang libFuzzer + ASan/UBSan parser target" OFF)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_library(spice_v2_warnings INTERFACE)
if(MSVC)
    target_compile_options(spice_v2_warnings INTERFACE /W4 /WX /permissive-)
    target_compile_definitions(spice_v2_warnings INTERFACE NOMINMAX _CRT_SECURE_NO_WARNINGS)
else()
    target_compile_options(spice_v2_warnings INTERFACE -Wall -Wextra -Wpedantic -Werror)
    if(SPICE_V2_ENABLE_COVERAGE)
        target_compile_options(spice_v2_warnings INTERFACE --coverage -O0 -g)
        target_link_options(spice_v2_warnings INTERFACE --coverage)
    endif()
endif()

# Optional SuiteSparse KLU discovery (vcpkg CONFIG or system libsuitesparse-dev fallback).
find_package(KLU CONFIG QUIET)
set(SPICE_V2_KLU_FALLBACK OFF)
if(NOT KLU_FOUND)
    find_path(SPICE_KLU_INCLUDE_DIR NAMES klu.h PATH_SUFFIXES suitesparse)
    find_library(SPICE_KLU_LIBRARY NAMES klu)
    if(SPICE_KLU_INCLUDE_DIR AND SPICE_KLU_LIBRARY)
        set(SPICE_V2_KLU_FALLBACK ON)
    endif()
endif()
if(KLU_FOUND OR SPICE_V2_KLU_FALLBACK)
    message(STATUS "Spice V2: SuiteSparse KLU ENABLED")
else()
    message(STATUS "Spice V2: SuiteSparse KLU not found (using built-in SparseLu)")
endif()

# 1. Core layer: typed IDs, validated CSC patterns, immutable Circuit IR.
add_library(spice_core STATIC
    cpp/core/types.cpp
    cpp/core/circuit.cpp
)
target_include_directories(spice_core PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_core PRIVATE spice_v2_warnings)

# 2. DAE layer: universal Device interface (depends only on spice_core).
add_library(spice_dae INTERFACE)
target_include_directories(spice_dae INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_dae INTERFACE spice_core)

# 3. Circuit-independent linear solver layer (depends only on spice_core; ARCH-LIN-001).
add_library(spice_linalg STATIC
    cpp/linalg/dense_lu.cpp
    cpp/linalg/sparse_lu.cpp
    cpp/linalg/klu_solver.cpp
)
target_include_directories(spice_linalg PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_linalg PUBLIC spice_core PRIVATE spice_v2_warnings)
if(KLU_FOUND)
    target_compile_definitions(spice_linalg PRIVATE SPICE_V2_HAS_KLU=1)
    if(TARGET SuiteSparse::KLU)
        target_link_libraries(spice_linalg PRIVATE SuiteSparse::KLU)
    elseif(TARGET KLU::KLU)
        target_link_libraries(spice_linalg PRIVATE KLU::KLU)
    endif()
elseif(SPICE_V2_KLU_FALLBACK)
    target_compile_definitions(spice_linalg PRIVATE SPICE_V2_HAS_KLU=1)
    target_include_directories(spice_linalg PRIVATE ${SPICE_KLU_INCLUDE_DIR})
    target_link_libraries(spice_linalg PRIVATE ${SPICE_KLU_LIBRARY})
endif()

# 4. Generic OSDI 0.3/0.4 dynamic model loader (ARCH-MOD-001, ARCH-MOD-002).
add_library(spice_osdi STATIC
    cpp/osdi/osdi_loader.cpp
)
target_include_directories(spice_osdi PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_osdi
    PUBLIC spice_core spice_dae
    PRIVATE ${CMAKE_DL_LIBS} spice_v2_warnings
)

# 5. Built-in linear/nonlinear DAE devices, compiled B-source VM, and CSC pre-binding compiler.
add_library(spice_devices STATIC
    cpp/devices/expr_vm.cpp
    cpp/devices/linear_devices.cpp
    cpp/devices/semiconductor_devices.cpp
)
target_include_directories(spice_devices PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_devices
    PUBLIC spice_core spice_dae spice_osdi
    PRIVATE spice_v2_warnings
)

# 6. Dimensioned Newton & continuation controller (ARCH-EQ-002).
add_library(spice_solver STATIC
    cpp/solver/newton.cpp
)
target_include_directories(spice_solver PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_solver
    PUBLIC spice_devices spice_linalg
    PRIVATE spice_v2_warnings
)

# 7. Independent physical consistency audits (AUDIT-001 .. AUDIT-005).
add_library(spice_audit STATIC
    cpp/audit/physical_audit.cpp
)
target_include_directories(spice_audit PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_audit PUBLIC spice_devices PRIVATE spice_v2_warnings)

# 8. Analyses: OP, DC sweep, AC, Transient (BE/Trap/BDF2 + LTE), M2c, M3 RF (.HB/.PSS/.PAC/.PXF/.PNOISE).
add_library(spice_analysis STATIC
    cpp/analysis/analyses.cpp
    cpp/analysis/m2c_analyses.cpp
    cpp/analysis/rf_steady_state.cpp
)
target_include_directories(spice_analysis PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_analysis
    PUBLIC spice_devices spice_linalg spice_solver spice_audit
    PRIVATE spice_v2_warnings
)

# 8b. M4a Mixed-Signal Event-Driven Co-Simulation Kernel (decoupled from analog DAE hot loop).
add_library(spice_mixed STATIC
    cpp/mixed/mixed_signal.cpp
)
target_include_directories(spice_mixed PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_mixed
    PUBLIC spice_analysis
    PRIVATE spice_v2_warnings
)

# 9. Native canonical SPICE3f5 parser and hierarchical elaborator (depends only on spice_core).
add_library(spice_parser STATIC
    cpp/parser/spice_parser.cpp
)
target_include_directories(spice_parser PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_parser
    PUBLIC spice_core
    PRIVATE spice_v2_warnings
)

# 10. Stable versioned flat C ABI shared library.
add_library(spice_capi SHARED
    cpp/capi/capi.cpp
)
target_compile_definitions(spice_capi PRIVATE SPICE_CAPI_BUILDING_DLL=1)
target_include_directories(spice_capi
    PUBLIC $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    PRIVATE $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_capi PRIVATE spice_parser spice_analysis spice_v2_warnings)

# 11. Native standalone CLI binary (spice_v2_cli).
add_executable(spice_v2_cli
    cpp/cli/main.cpp
)
target_include_directories(spice_v2_cli PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/cpp
)
target_link_libraries(spice_v2_cli PRIVATE
    spice_parser
    spice_analysis
    spice_v2_warnings
)

if(SPICE_V2_BUILD_TESTS)
    enable_testing()
    find_package(Catch2 3 QUIET)

    # Real compiled OSDI 0.3 C test fixture shared libraries (positive + bad-ABI negative).
    add_library(test_osdi_diode SHARED
        tests/fixtures/test_osdi_diode.c
    )
    target_include_directories(test_osdi_diode PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
    )
    target_link_libraries(test_osdi_diode PRIVATE spice_v2_warnings)
    if(NOT WIN32)
        target_link_libraries(test_osdi_diode PRIVATE m)
    endif()

    add_library(test_osdi_bad_abi SHARED
        tests/fixtures/test_osdi_bad_abi.c
    )
    target_include_directories(test_osdi_bad_abi PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
    )
    target_link_libraries(test_osdi_bad_abi PRIVATE spice_v2_warnings)

    add_executable(spice_v2_unit_tests
        tests/capi_and_dae_tests.cpp
    )
    target_include_directories(spice_v2_unit_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_link_libraries(spice_v2_unit_tests PRIVATE
        spice_analysis
        spice_capi
        spice_v2_warnings
    )
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_unit_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_unit_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_unit_tests COMMAND spice_v2_unit_tests)

    add_executable(spice_v2_m2a_tests
        tests/m2a_linear_engine_tests.cpp
    )
    target_include_directories(spice_v2_m2a_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_link_libraries(spice_v2_m2a_tests PRIVATE
        spice_analysis
        spice_capi
        spice_v2_warnings
    )
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_m2a_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_m2a_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_m2a_tests COMMAND spice_v2_m2a_tests)

    add_executable(spice_v2_m2b_tests
        tests/m2b_nonlinear_osdi_tests.cpp
    )
    target_include_directories(spice_v2_m2b_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_compile_definitions(spice_v2_m2b_tests PRIVATE
        SPICE_TEST_OSDI_DIODE_PATH="$<TARGET_FILE:test_osdi_diode>"
        SPICE_TEST_OSDI_BAD_ABI_PATH="$<TARGET_FILE:test_osdi_bad_abi>"
    )
    target_link_libraries(spice_v2_m2b_tests PRIVATE
        spice_analysis
        spice_capi
        spice_v2_warnings
    )
    add_dependencies(spice_v2_m2b_tests test_osdi_diode test_osdi_bad_abi)
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_m2b_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_m2b_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_m2b_tests COMMAND spice_v2_m2b_tests)

    add_executable(spice_v2_m2b_parser_lte_tests
        tests/m2b_parser_lte_cli_tests.cpp
    )
    target_include_directories(spice_v2_m2b_parser_lte_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_compile_definitions(spice_v2_m2b_parser_lte_tests PRIVATE
        SPICE_TEST_OSDI_DIODE_PATH="$<TARGET_FILE:test_osdi_diode>"
        SPICE_V2_CLI_PATH="$<TARGET_FILE:spice_v2_cli>"
    )
    target_link_libraries(spice_v2_m2b_parser_lte_tests PRIVATE
        spice_parser
        spice_analysis
        spice_v2_warnings
    )
    add_dependencies(spice_v2_m2b_parser_lte_tests test_osdi_diode spice_v2_cli)
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_m2b_parser_lte_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_m2b_parser_lte_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_m2b_parser_lte_tests COMMAND spice_v2_m2b_parser_lte_tests)

    add_executable(spice_v2_m2c_tests
        tests/m2c_core_analog_tests.cpp
    )
    target_include_directories(spice_v2_m2c_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_link_libraries(spice_v2_m2c_tests PRIVATE
        spice_parser
        spice_analysis
        spice_v2_warnings
    )
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_m2c_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_m2c_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_m2c_tests COMMAND spice_v2_m2c_tests)

    add_executable(spice_v2_m3_tests
        tests/m3_rf_steady_state_tests.cpp
    )
    target_include_directories(spice_v2_m3_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_compile_definitions(spice_v2_m3_tests PRIVATE
        SPICE_TEST_OSDI_DIODE_PATH="$<TARGET_FILE:test_osdi_diode>"
    )
    target_link_libraries(spice_v2_m3_tests PRIVATE
        spice_analysis
        spice_v2_warnings
    )
    add_dependencies(spice_v2_m3_tests test_osdi_diode)
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_m3_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_m3_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_m3_tests COMMAND spice_v2_m3_tests)

    add_executable(spice_v2_m4_mixed_tests
        tests/m4_mixed_signal_tests.cpp
    )
    target_include_directories(spice_v2_m4_mixed_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_link_libraries(spice_v2_m4_mixed_tests PRIVATE
        spice_mixed
        spice_v2_warnings
    )
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_m4_mixed_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_m4_mixed_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_m4_mixed_tests COMMAND spice_v2_m4_mixed_tests)

    add_executable(spice_v2_semiconductor_deck_tests
        tests/m2c_semiconductor_and_decks_tests.cpp
    )
    target_include_directories(spice_v2_semiconductor_deck_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_compile_definitions(spice_v2_semiconductor_deck_tests PRIVATE
        SPICE_V2_CLI_PATH="$<TARGET_FILE:spice_v2_cli>"
    )
    target_link_libraries(spice_v2_semiconductor_deck_tests PRIVATE
        spice_parser
        spice_analysis
        spice_v2_warnings
    )
    add_dependencies(spice_v2_semiconductor_deck_tests spice_v2_cli)
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_semiconductor_deck_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_semiconductor_deck_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_semiconductor_deck_tests COMMAND spice_v2_semiconductor_deck_tests)

    add_executable(spice_v2_dialect_parity_tests
        tests/m2c_dialect_and_parity_tests.cpp
    )
    target_include_directories(spice_v2_dialect_parity_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_compile_definitions(spice_v2_dialect_parity_tests PRIVATE
        SPICE_V2_CLI_PATH="$<TARGET_FILE:spice_v2_cli>"
    )
    target_link_libraries(spice_v2_dialect_parity_tests PRIVATE
        spice_parser
        spice_analysis
        spice_v2_warnings
    )
    add_dependencies(spice_v2_dialect_parity_tests spice_v2_cli)
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_dialect_parity_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_dialect_parity_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_dialect_parity_tests COMMAND spice_v2_dialect_parity_tests)

    add_executable(spice_v2_fuzz_stress_tests
        tests/fuzz_and_malformed_tests.cpp
    )
    target_include_directories(spice_v2_fuzz_stress_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_link_libraries(spice_v2_fuzz_stress_tests PRIVATE
        spice_parser
        spice_analysis
        spice_capi
        spice_v2_warnings
    )
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_fuzz_stress_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_fuzz_stress_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_fuzz_stress_tests COMMAND spice_v2_fuzz_stress_tests)

    add_executable(spice_v2_slice_a_models_tests
        tests/m2d_slice_a_models_tests.cpp
    )
    target_include_directories(spice_v2_slice_a_models_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/tests
    )
    target_link_libraries(spice_v2_slice_a_models_tests PRIVATE
        spice_parser
        spice_analysis
        spice_v2_warnings
    )
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_slice_a_models_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_slice_a_models_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()
    add_test(NAME spice_v2_slice_a_models_tests COMMAND spice_v2_slice_a_models_tests)
endif()

if(SPICE_V2_BUILD_FUZZER)
    add_executable(spice_v2_parser_fuzzer
        tests/fuzz_parser_target.cpp
        cpp/core/types.cpp
        cpp/core/circuit.cpp
        cpp/parser/spice_parser.cpp
    )
    target_include_directories(spice_v2_parser_fuzzer PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/cpp
    )
    target_compile_options(spice_v2_parser_fuzzer PRIVATE
        -fsanitize=fuzzer,address,undefined -fno-omit-frame-pointer -Wall -Wextra
    )
    target_link_options(spice_v2_parser_fuzzer PRIVATE
        -fsanitize=fuzzer,address,undefined
    )
endif()
