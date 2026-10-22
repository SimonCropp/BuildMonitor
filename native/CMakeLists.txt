cmake_minimum_required(VERSION 3.24)
project(buildmonitor_ui LANGUAGES C CXX)

#
# Everything about this build that is not BuildMonitor's own is PlatformHeads', fetched at a tag:
# the raylib and Dear ImGui it pins, the raylib 6.0 options it switches off, and how the library
# is compiled and linked. It was a copy of DiffEngine's here, and three fixes made to that copy
# were found again in this one, each by a user's machine: a library that needed glibc 2.38, a
# window that stayed blank, and libraries recorded as needed that a stock Ubuntu lacks.
#
# The tag is the one .github/workflows/build-native.yml calls and Directory.Packages.props names.
#
include(FetchContent)
FetchContent_Declare(
  platformheads
  GIT_REPOSITORY https://github.com/SimonCropp/PlatformHeads.git
  GIT_TAG 0.4.0
  GIT_SHALLOW ON)
FetchContent_MakeAvailable(platformheads)
include(${platformheads_SOURCE_DIR}/cmake/PlatformHeads.cmake)

platformheads_defaults()

# PNG is the one picture format anything here reads or writes: a row's icons arrive as PNG, and a
# capture is saved as one.
platformheads_fetch(FORMATS PNG)

platformheads_add_library(buildmonitor_ui SOURCES src/bm.cpp)
