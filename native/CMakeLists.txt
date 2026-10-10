cmake_minimum_required(VERSION 3.24)
project(buildmonitor_ui LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
  set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

#
# Sources are fetched rather than vendored as submodules.
#
# Nothing in a normal `dotnet build` touches this directory: the built binaries are committed to
# the runtimes folder of the native head, so only this file's own build needs raylib and imgui.
# The versions are pinned by tag here, in the same file as the build configuration.
#
include(FetchContent)

# raylib, static so the shipped artifact is a single file per RID.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(BUILD_GAMES OFF CACHE BOOL "" FORCE)
set(CUSTOMIZE_BUILD ON CACHE BOOL "" FORCE)
# raylib 6.0 reads every SUPPORT_ flag in its config.h into an option that defaults to ON, the ones
# config.h defines as 0 included (raysan5/raylib#5844, fixed after 6.0), and CUSTOMIZE_BUILD skips
# config.h's own values. So each of those is on unless it is switched off here.
# SUPPORT_CUSTOM_FRAME_CONTROL was: EndDrawing then neither puts the frame on the screen, reads
# input nor waits for the next frame, and bm_present leaves all three to it for every frame it
# draws. So the Linux window stayed black, and from the first input bm_present drew one frame
# after another with no wait between them and read nothing more. A capture draws into a texture
# and never gets that far, which is why the pixel snapshots passed throughout;
# PixelTests.PresentWaitsForTheNextFrame is what notices. The busy wait loop was on the same way,
# and WaitTime is how bm_present waits out a frame it does not draw, so a tray with its window
# hidden spun through every one of them: a seventh of a core, for as long as it ran. The rest are
# formats and features nothing here uses.
foreach(flag IN ITEMS
    SUPPORT_CUSTOM_FRAME_CONTROL
    SUPPORT_BUSY_WAIT_LOOP
    SUPPORT_GPU_SKINNING
    SUPPORT_FILEFORMAT_TGA
    SUPPORT_FILEFORMAT_JPG
    SUPPORT_FILEFORMAT_PSD
    SUPPORT_FILEFORMAT_HDR
    SUPPORT_FILEFORMAT_PIC
    SUPPORT_FILEFORMAT_PNM
    SUPPORT_FILEFORMAT_KTX
    SUPPORT_FILEFORMAT_ASTC
    SUPPORT_FILEFORMAT_PKM
    SUPPORT_FILEFORMAT_PVR
    SUPPORT_FILEFORMAT_BDF
    SUPPORT_FILEFORMAT_FLAC)
  set(${flag} OFF CACHE BOOL "" FORCE)
endforeach()
# raylib spins for the last part of every wait by default, for timing precision nothing here
# needs: most of a millisecond of a core per frame, for as long as the tray runs.
set(SUPPORT_PARTIALBUSY_WAIT_LOOP OFF CACHE BOOL "" FORCE)
# F12 saves a screenshot into the working directory, which is wherever the tray was started from.
# raylib looks for the key at the end of a frame it has drawn, so that is F12 pressed while frames
# are being drawn, as they are for as long as the pointer is crossing the window.
set(SUPPORT_SCREEN_CAPTURE OFF CACHE BOOL "" FORCE)
set(SUPPORT_MODULE_RAUDIO OFF CACHE BOOL "" FORCE)
set(SUPPORT_MODULE_RMODELS OFF CACHE BOOL "" FORCE)
# PNG is the one picture format anything here reads or writes: a row's icons arrive as PNG, and a
# capture is saved as one. The other four that config.h has on, nothing here loads or saves.
foreach(flag IN ITEMS
    SUPPORT_FILEFORMAT_BMP
    SUPPORT_FILEFORMAT_GIF
    SUPPORT_FILEFORMAT_QOI
    SUPPORT_FILEFORMAT_DDS)
  set(${flag} OFF CACHE BOOL "" FORCE)
endforeach()
set(SUPPORT_FILEFORMAT_PNG ON CACHE BOOL "" FORCE)

FetchContent_Declare(
  raylib
  GIT_REPOSITORY https://github.com/raysan5/raylib.git
  GIT_TAG 6.0
  GIT_SHALLOW ON)

# imgui has no CMake build of its own, so it is fetched as plain sources and compiled below.
FetchContent_Declare(
  imgui
  GIT_REPOSITORY https://github.com/ocornut/imgui.git
  GIT_TAG v1.92.9b
  GIT_SHALLOW ON)

FetchContent_MakeAvailable(raylib)
FetchContent_MakeAvailable(imgui)

add_library(buildmonitor_ui SHARED
  src/bm.cpp
  ${imgui_SOURCE_DIR}/imgui.cpp
  ${imgui_SOURCE_DIR}/imgui_draw.cpp
  ${imgui_SOURCE_DIR}/imgui_tables.cpp
  ${imgui_SOURCE_DIR}/imgui_widgets.cpp)

target_include_directories(buildmonitor_ui PRIVATE include ${imgui_SOURCE_DIR})
# ImWchar is sixteen bits unless this is defined, and the robot a Dependabot row carries is
# U+1F916. Without it the codepoint does not survive ImGui's own UTF-8 decoding, whatever font is
# loaded. Set on the target, so imgui's own sources are compiled with it too: it changes a type
# they share with this one.
target_compile_definitions(buildmonitor_ui PRIVATE IMGUI_USE_WCHAR32)
target_link_libraries(buildmonitor_ui PRIVATE raylib)

# Only the libraries something is actually called in are recorded as needed. raylib hands on
# everything CMake's FindX11 found, which includes libSM and libICE wherever their headers are
# installed, and nothing here or in raylib calls either. A library recorded as needed has to be
# on the machine for this one to load at all, and a stock Ubuntu has neither of those two.
# Ubuntu's own compiler passes this by default, so a library built on the runner never recorded
# them. The manylinux container build-native builds in does not pass it.
if(UNIX AND NOT APPLE)
  target_link_options(buildmonitor_ui PRIVATE LINKER:--as-needed)
endif()

# The managed side probes for a bare name; keep it identical on every platform apart from the
# platform's own prefix and extension.
set_target_properties(buildmonitor_ui PROPERTIES
  OUTPUT_NAME "buildmonitor_ui"
  C_VISIBILITY_PRESET hidden
  CXX_VISIBILITY_PRESET hidden)

if(MSVC)
  target_compile_options(buildmonitor_ui PRIVATE /W3 /permissive-)
  target_compile_definitions(buildmonitor_ui PRIVATE _CRT_SECURE_NO_WARNINGS)
else()
  target_compile_options(buildmonitor_ui PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()

if(APPLE)
  set_target_properties(buildmonitor_ui PROPERTIES SUFFIX ".dylib")
endif()
