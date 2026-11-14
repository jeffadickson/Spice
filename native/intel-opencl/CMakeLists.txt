cmake_minimum_required(VERSION 3.20)
project(spice_intel_opencl LANGUAGES CXX)
if(NOT EXISTS "${VIENNACL_ROOT}/viennacl/linalg/gmres.hpp")
  message(FATAL_ERROR "Supply the pinned ViennaCL root")
endif()
add_library(spice_intel_opencl SHARED solver.cpp)
target_compile_features(spice_intel_opencl PRIVATE cxx_std_17)
# ViennaCL 1.7.1 has no public context-erasure operation. Assigning an empty
# context is invalid because its handle copy attempts clRetainContext(NULL).
# Add only a lifecycle accessor in an owned build overlay; numerical sources
# and the pinned dependency checkout remain unchanged, with notices retained.
file(READ "${VIENNACL_ROOT}/viennacl/ocl/backend.hpp" backend_header)
string(REPLACE "\r\n" "\n" backend_header "${backend_header}")
set(anchor "private:\n  static long current_context_id_;")
string(FIND "${backend_header}" "${anchor}" anchor_position)
if(anchor_position LESS 0)
  message(FATAL_ERROR "Pinned ViennaCL lifecycle overlay anchor changed")
endif()
string(REPLACE "${anchor}" "  // Owned lifecycle patch: destroy resources before DLL process-detach.\n  static void release_context(long id)\n  {\n    contexts_.erase(id);\n    initialized_.erase(id);\n  }\n\nprivate:\n  static long current_context_id_;" backend_header "${backend_header}")
file(MAKE_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/compat/viennacl/ocl")
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/compat/viennacl/ocl/backend.hpp" "${backend_header}")
target_include_directories(spice_intel_opencl PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/compat")
target_include_directories(spice_intel_opencl SYSTEM PRIVATE "${VIENNACL_ROOT}")
target_link_libraries(spice_intel_opencl PRIVATE "${OPENCL_LIBRARY}")
if(MSVC)
  # MSVC maximum-speed optimization includes loop/SLP auto-vectorization.
  # Do not claim generated SIMD without inspecting the relevant machine code.
  target_compile_options(spice_intel_opencl PRIVATE /O2 /fp:precise /EHsc)
else()
  target_compile_options(spice_intel_opencl PRIVATE -O3 -fno-fast-math)
endif()
