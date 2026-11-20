cmake_minimum_required(VERSION 3.24)
project(spice_v2 VERSION 0.1.0 LANGUAGES C CXX)

option(SPICE_V2_BUILD_TESTS "Build Spice V2 unit and boundary tests" ON)
option(SPICE_V2_ENABLE_COVERAGE "Enable GCC/Clang coverage instrumentation on owned targets" OFF)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# Shared strict warning & optional coverage flags for owned C++20 targets only.
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
# Slice 0 compiles cleanly without KLU and reports klu_available=0 honestly.
find_package(KLU CONFIG QUIET)

# 1. Core layer: typed IDs, validated CSC sparsity patterns, diagnostics (zero external deps).
add_library(spice_core STATIC
    cpp/core/types.cpp
)
target_include_directories(spice_core PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_core PRIVATE spice_v2_warnings)

# 2. DAE layer: universal Device interface F(x,t) + dQ(x,t)/dt = 0 (depends only on spice_core).
add_library(spice_dae INTERFACE)
target_include_directories(spice_dae INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_dae INTERFACE spice_core)

# 3. Flat versioned C ABI layer (consumes core + dae, exports only spice_capi.h).
add_library(spice_capi SHARED
    cpp/capi/capi.cpp
)
target_compile_definitions(spice_capi PRIVATE SPICE_CAPI_BUILDING_DLL=1)
if(KLU_FOUND)
    target_compile_definitions(spice_capi PRIVATE SPICE_V2_HAS_KLU=1)
endif()
target_include_directories(spice_capi
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    PRIVATE
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/cpp>
)
target_link_libraries(spice_capi PRIVATE spice_core spice_dae spice_v2_warnings)

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
        spice_core
        spice_dae
        spice_capi
        spice_v2_warnings
    )
    if(Catch2_FOUND)
        target_link_libraries(spice_v2_unit_tests PRIVATE Catch2::Catch2WithMain)
        target_compile_definitions(spice_v2_unit_tests PRIVATE SPICE_V2_USE_CATCH2=1)
    endif()

    add_test(NAME spice_v2_unit_tests COMMAND spice_v2_unit_tests)
endif()
