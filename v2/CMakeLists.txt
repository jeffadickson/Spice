cmake_minimum_required(VERSION 3.24)
project(spice_v2 VERSION 0.1.0 LANGUAGES C CXX)

option(SPICE_V2_BUILD_TESTS "Build Spice V2 unit, audit, and scale tests" ON)
option(SPICE_V2_ENABLE_COVERAGE "Enable GCC/Clang coverage instrumentation on owned targets" OFF)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_library(spice_v2_warnings INTERFACE)
if(MSVC)
    target_compile_options(spice_v2_warnings INTERFACE /W4 /WX /permissive-)
else()
    target_compile_options(spice_v2_warnings INTERFACE -Wall -Wextra -Wpedantic -Werror)
    if(SPICE_V2_ENABLE_COVERAGE)
        target_compile_options(spice_v2_warnings INTERFACE --coverage -O0 -g)
        target_link_options(spice_v2_warnings INTERFACE --coverage)
    endif()
endif()

# Optional SuiteSparse KLU discovery (provided via vcpkg on Windows/Linux).
find_package(KLU CONFIG QUIET)

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
endif()

# 4. Built-in linear DAE devices and CSC pre-binding compiler.
add_library(spice_devices STATIC
    cpp/devices/linear_devices.cpp
)
target_include_directories(spice_devices PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_devices PUBLIC spice_core spice_dae PRIVATE spice_v2_warnings)

# 5. Independent physical consistency audits (AUDIT-001 .. AUDIT-005).
add_library(spice_audit STATIC
    cpp/audit/physical_audit.cpp
)
target_include_directories(spice_audit PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_audit PUBLIC spice_devices PRIVATE spice_v2_warnings)

# 6. Analyses: OP, DC sweep, AC, Transient (BE & Trapezoidal).
add_library(spice_analysis STATIC
    cpp/analysis/analyses.cpp
)
target_include_directories(spice_analysis PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_analysis
    PUBLIC spice_devices spice_linalg spice_audit
    PRIVATE spice_v2_warnings
)

# 7. Stable versioned flat C ABI shared library.
add_library(spice_capi SHARED
    cpp/capi/capi.cpp
)
target_compile_definitions(spice_capi PRIVATE SPICE_CAPI_BUILDING_DLL=1)
target_include_directories(spice_capi
    PUBLIC $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    PRIVATE $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_capi PRIVATE spice_analysis spice_v2_warnings)

if(SPICE_V2_BUILD_TESTS)
    enable_testing()
    find_package(Catch2 3 QUIET)

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
endif()
