project(mini C)

if(ENABLE_LLVM OR ENABLE_LLVM_RUNTIME OR HOST_BROWSER)
  enable_language(CXX)
endif()

include(FindPython)

include_directories(
  ${PROJECT_BINARY_DIR}/
  ${PROJECT_BINARY_DIR}/../..
  ${CMAKE_CURRENT_SOURCE_DIR}/../..
  ${PROJECT_SOURCE_DIR}/../
  )

if(HOST_DARWIN)
  set(OS_LIBS "-framework CoreFoundation" "-framework Foundation")

  if(CMAKE_SYSTEM_VARIANT STREQUAL "maccatalyst")
    set(OS_LIBS ${OS_LIBS} "-lobjc" "-lc++")
  endif()
elseif(HOST_IOS OR HOST_TVOS)
set(OS_LIBS "-framework CoreFoundation" "-lobjc" "-lc++")
elseif(HOST_ANDROID)
set(OS_LIBS m dl log)
elseif(HOST_LINUX)
set(OS_LIBS pthread m dl)
elseif(HOST_WIN32)
set(OS_LIBS bcrypt.lib Mswsock.lib ws2_32.lib psapi.lib version.lib advapi32.lib winmm.lib kernel32.lib)
elseif(HOST_SOLARIS)
  set(OS_LIBS socket pthread m ${CMAKE_DL_LIBS})
elseif(HOST_FREEBSD)
  set(OS_LIBS pthread m)
elseif(HOST_HAIKU)
  set(OS_LIBS network)
endif()

#
# SUBDIRS
#
include(../component/CMakeLists.txt)

if(HOST_WIN32)
  # /OPT:ICF merges idential functions breaking mono_lookup_icall_symbol ()
  add_link_options(/OPT:NOICF)
endif()

#
# MINI
#

set(mini_common_sources
    mini.c
    mini-runtime.c
    seq-points.c
    seq-points.h
    ir-emit.h
    method-to-ir.c
    cfgdump.h
    cfgdump.c
    calls.c
    decompose.c
    mini.h
    optflags-def.h
    jit-icalls.h
    jit-icalls.c
    trace.c
    trace.h
    patch-info.h
    mini-ops.h
    mini-arch.h
    dominators.c
    cfold.c
    regalloc.h
    helpers.c
    liveness.c
    ssa.c
    abcremoval.c
    abcremoval.h
    local-propagation.c
    driver.c
    debug-mini.c
    linear-scan.c
    aot-compiler.h
    aot-compiler.c
    aot-runtime.c
    aot-runtime-wasm.c
    graph.c
    mini-codegen.c
    mini-exceptions.c
    mini-trampolines.c
    branch-opts.c
    mini-generic-sharing.c
    simd-methods.h
    simd-intrinsics.c
    mini-unwind.h
    unwind.c
    image-writer.h
    image-writer.c
    dwarfwriter.h
    dwarfwriter.c
    mini-gc.h
    mini-gc.c
    mini-llvm.h
    mini-llvm-cpp.h
    llvm-jit.h
    alias-analysis.c
    mini-cross-helpers.c
    arch-stubs.c
    llvm-runtime.h
    llvm-intrinsics.h
    llvm-intrinsics-types.h
    type-checking.c
    lldb.h
    lldb.c
    memory-access.c
    intrinsics.c
    mini-profiler.c
    interp-stubs.c
    aot-runtime.h
    ee.h
    mini-runtime.h
    llvmonly-runtime.h
    llvmonly-runtime.c
    monovm.h
    monovm.c)

set(debugger_sources
  debugger-agent-external.h
  debugger-agent-external.c
  )

set(amd64_sources
    mini-amd64.c
    mini-amd64.h
    exceptions-amd64.c
    tramp-amd64.c
    mini-amd64-gsharedvt.c
    mini-amd64-gsharedvt.h
    tramp-amd64-gsharedvt.c
    cpu-amd64.h)

set(x86_sources
    mini-x86.c
    mini-x86.h
    exceptions-x86.c
    tramp-x86.c
    mini-x86-gsharedvt.c
    tramp-x86-gsharedvt.c
    cpu-x86.h)

set(arm64_sources
    mini-arm64.c
    mini-arm64.h
    exceptions-arm64.c
    tramp-arm64.c
    mini-arm64-gsharedvt.c
    mini-arm64-gsharedvt.h
    tramp-arm64-gsharedvt.c
    cpu-arm64.h)

set(arm_sources
    mini-arm.c
    mini-arm.h
    exceptions-arm.c
    tramp-arm.c
    mini-arm-gsharedvt.c
    tramp-arm-gsharedvt.c
    cpu-arm.h)

set(riscv64_sources
    mini-riscv.c
    mini-riscv.h
    exceptions-riscv.c
    tramp-riscv.c
    cpu-riscv64.h)

set(s390x_sources
    mini-s390x.c
    mini-s390x.h
    exceptions-s390x.c
    tramp-s390x.c
    cpu-s390x.h)

set(wasm_sources
    mini-wasm.c
    tramp-wasm.c
    exceptions-wasm.c
    cpu-wasm.h)

set(powerpc64_sources
    mini-ppc.c
    mini-ppc.h
    exceptions-ppc.c
    tramp-ppc.c
    cpu-ppc64.h)

if(TARGET_AMD64)
set(arch_sources ${amd64_sources})
elseif(TARGET_X86)
set(arch_sources ${x86_sources})
elseif(TARGET_ARM64)
set(arch_sources ${arm64_sources})
elseif(TARGET_ARM)
set(arch_sources ${arm_sources})
elseif(TARGET_RISCV64)
set(arch_sources ${riscv64_sources})
elseif(TARGET_S390X)
set(arch_sources ${s390x_sources})
elseif(TARGET_WASM)
set(arch_sources ${wasm_sources})
elseif(TARGET_POWERPC64)
set(arch_sources ${powerpc64_sources})
endif()

set(darwin_sources
    mini-darwin.c)

set(windows_sources
    mini-windows.c
    mini-windows-tls-callback.c
    mini-windows.h
    )

set(posix_sources
    mini-posix.c)

if(HOST_DARWIN)
set(os_sources "${darwin_sources};${posix_sources}")
elseif(HOST_LINUX OR HOST_SOLARIS OR HOST_FREEBSD OR HOST_HAIKU)
set(os_sources "${posix_sources}")
elseif(HOST_WIN32)
set(os_sources "${windows_sources}")
endif()

set(interp_sources
    interp/interp.h
    interp/interp-internals.h
    interp/interp.c
    interp/interp-intrins.h
    interp/interp-intrins.c
    interp/mintops.h
    interp/mintops.c
    interp/transform.c
    interp/transform-opt.c
    interp/tiering.h
    interp/tiering.c
    interp/jiterpreter.c
    interp/interp-pgo.c)
set(interp_simd_sources
    interp/interp-simd.c)
set(interp_stub_sources
    interp-stubs.c)

if(NOT DISABLE_INTERPRETER)
  if(HOST_WASM)
    set(mini_interp_sources ${interp_sources})
  else()
    set(mini_interp_sources ${interp_sources} ${interp_simd_sources})
  endif()
else()
set(mini_interp_sources ${interp_stub_sources})
endif()

if(ENABLE_INTERP_LIB)
add_library(mono-ee-interp STATIC "${interp_sources}")
target_link_libraries(mono-ee-interp PRIVATE monoapi eglib_api)
target_include_directories(mono-ee-interp PRIVATE ${PROJECT_BINARY_DIR}/../..
  ${PROJECT_SOURCE_DIR}/../..
  ${PROJECT_SOURCE_DIR}/..)
install(TARGETS mono-ee-interp LIBRARY)
if(HOST_WASM AND CMAKE_BUILD_TYPE STREQUAL "Debug")
# Always optimize the interpreter, even in Debug builds.  Unoptimized interp_exec_method and
# generate_code are so big that some browsers overflow the stack with even a few recursive
# invocations (e.g. during .cctor initialization)
target_compile_options(mono-ee-interp PRIVATE -O1)
endif()
endif()

if(ENABLE_LLVM)
set(llvm_sources
    mini-llvm.c
    mini-llvm-cpp.cpp
    llvm-jit.cpp)
else()
set(llvm_sources)
endif()

if(ENABLE_LLVM)
set(llvm_runtime_sources
    llvm-runtime.cpp)
elseif(ENABLE_LLVM_RUNTIME AND NOT HOST_WASM)
set(llvm_runtime_sources
    llvm-runtime.cpp)
else()
set(llvm_runtime_sources)
endif()

if(TARGET_BROWSER AND MONO_CROSS_COMPILE)
set(profiler_sources ../profiler/browser.c)
else()
set(profiler_sources "")
endif()

set(mini_sources "main-core.c;${mini_common_sources};${arch_sources};${os_sources};${mini_interp_sources};${llvm_sources};${debugger_sources};${profiler_sources};${llvm_runtime_sources}")

if(LLVM_INCLUDEDIR)
  include_directories(BEFORE SYSTEM "${LLVM_INCLUDEDIR}")
endif()

if(HOST_WIN32)
set(mini_sources "${mini_sources};${VERSION_FILE_RC_PATH}") # this is generated by GenerateNativeVersionFile in Arcade
elseif(NOT HOST_BROWSER AND NOT HOST_WASI)
set(mini_sources "${mini_sources};${VERSION_FILE_PATH}") # this is generated by GenerateNativeVersionFile in Arcade
endif()

set(monosgen-sources "${mini_sources}")

if(HOST_WIN32 AND NOT DISABLE_SHARED_LIBS)
  add_library(monosgen-objects_shared OBJECT "${monosgen-sources}")
  target_compile_definitions(monosgen-objects_shared PRIVATE -DMONO_DLL_EXPORT)
  target_link_libraries (monosgen-objects_shared PRIVATE monoapi eglib_api utils_objects_shared sgen_objects_shared metadata_objects_shared)
endif()

add_library(monosgen-objects OBJECT "${monosgen-sources}")

set(MONOSGEN_OBJECTS_LINKABLE_LIBS "")
list(APPEND MONOSGEN_OBJECTS_LINKABLE_LIBS monoapi eglib_api utils_objects sgen_objects metadata_objects)
if (NOT CLR_CMAKE_USE_SYSTEM_ZLIB)
  list(APPEND MONOSGEN_OBJECTS_LINKABLE_LIBS zlib)
endif()
target_link_libraries (monosgen-objects PRIVATE ${MONOSGEN_OBJECTS_LINKABLE_LIBS})

if(NOT HOST_WIN32)
    target_compile_definitions(monosgen-objects PRIVATE -DMONO_DLL_EXPORT)
endif()

add_library(monosgen-static STATIC $<TARGET_OBJECTS:eglib_objects> $<TARGET_OBJECTS:utils_objects> $<TARGET_OBJECTS:sgen_objects> $<TARGET_OBJECTS:metadata_objects> $<TARGET_OBJECTS:monosgen-objects> $<TARGET_OBJECTS:minipal_objects>)
set_target_properties(monosgen-static PROPERTIES OUTPUT_NAME ${MONO_LIB_NAME})
target_link_libraries(monosgen-static PRIVATE dn-containers)

if(DISABLE_COMPONENTS OR AOT_COMPONENTS)
  # add component fallback stubs into static mono library when components have been disabled.
  target_sources(monosgen-static PRIVATE "${mono-components-stub-objects}")
endif()

if(NOT DISABLE_LIBS)
  install(TARGETS monosgen-static LIBRARY)
endif()
if(NOT DISABLE_SHARED_LIBS)
  if(HOST_WIN32)
    add_library(monosgen-shared SHARED "mini-windows-dllmain.c" $<TARGET_OBJECTS:monosgen-objects_shared>)
  else()
    add_library(monosgen-shared SHARED $<TARGET_OBJECTS:monosgen-objects>)
  endif()
  target_compile_definitions(monosgen-shared PRIVATE -DMONO_DLL_EXPORT)
  # musl-libc implements ucontext in a different library on s390x
  if(CLR_CMAKE_TARGET_LINUX_MUSL AND TARGET_S390X)
    target_link_libraries(monosgen-shared PRIVATE ucontext)
  endif()
  set_target_properties(monosgen-shared PROPERTIES OUTPUT_NAME ${MONO_SHARED_LIB_NAME})
  if(MONO_SET_RPATH_ORIGIN)
    set_target_properties(monosgen-shared PROPERTIES INSTALL_RPATH "$ORIGIN")
  endif()

  set(MONOSGENSHARED_LINKABLE_LIBS "")
  list(APPEND MONOSGENSHARED_LINKABLE_LIBS monoapi eglib_objects dn-containers)
  if (NOT CLR_CMAKE_USE_SYSTEM_ZLIB)
    list(APPEND MONOSGENSHARED_LINKABLE_LIBS zlib)
  endif()
  if(HOST_WIN32)
    list(APPEND MONOSGENSHARED_LINKABLE_LIBS utils_objects_shared sgen_objects_shared metadata_objects_shared minipal)
    target_link_libraries(monosgen-shared PRIVATE ${MONOSGENSHARED_LINKABLE_LIBS})
  else()
    list(APPEND MONOSGENSHARED_LINKABLE_LIBS utils_objects sgen_objects metadata_objects minipal)
    target_link_libraries(monosgen-shared PRIVATE ${MONOSGENSHARED_LINKABLE_LIBS})
  endif()
  target_include_directories (monosgen-shared PRIVATE monoapi)
  if(TARGET_WIN32)
    # on Windows the import library for the shared mono library will have the same name as the static library,
    # to avoid a conflict we rename the import library with the .import.lib suffix
    set_target_properties(monosgen-shared PROPERTIES IMPORT_SUFFIX ".import.lib")
  endif()

  set(MONOSGENSHARED_LINKABLE_EXTRALIBS "")
  list(APPEND MONOSGENSHARED_LINKABLE_EXTRALIBS ${OS_LIBS} ${LLVM_LIBS})
  if (CLR_CMAKE_USE_SYSTEM_ZLIB)
    list(APPEND MONOSGENSHARED_LINKABLE_EXTRALIBS ${Z_LIBS})
  else()
    list(APPEND MONOSGENSHARED_LINKABLE_EXTRALIBS zlib)
  endif()
  target_link_libraries(monosgen-shared PRIVATE ${MONOSGENSHARED_LINKABLE_EXTRALIBS})
  
  if(TARGET_DARWIN)
    set_property(TARGET monosgen-shared APPEND_STRING PROPERTY LINK_FLAGS " -Wl,-compatibility_version -Wl,2.0 -Wl,-current_version -Wl,2.0")
  endif()
  if(NOT DISABLE_COMPONENTS AND STATIC_COMPONENTS AND NOT DISABLE_LINK_STATIC_COMPONENTS)
    # if components are built statically, but we're building a shared lib mono,
    # link them into the library
    target_sources(monosgen-shared PRIVATE "${mono-components-objects}")
  elseif(NOT DISABLE_COMPONENTS AND STATIC_COMPONENTS AND DISABLE_LINK_STATIC_COMPONENTS)
    # if components are built statically, we're building a shared lib mono, but we shouldn't link components
    # link the fallback stubs into the runtime
    target_sources(monosgen-shared PRIVATE "${mono-components-stub-objects}")
  elseif(NOT DISABLE_COMPONENTS AND NOT STATIC_COMPONENTS)
    # if components are built dynamically, link the fallback stubs into the runtime
    target_sources(monosgen-shared PRIVATE "${mono-components-stub-objects}")
  elseif(DISABLE_COMPONENTS)
    # if components are disabled, link the fallback stubs into the runtime
    target_sources(monosgen-shared PRIVATE "${mono-components-stub-objects}")
  endif()
  install_with_stripped_symbols(monosgen-shared TARGETS lib)
  if(HOST_WIN32 AND TARGET_AMD64)
    add_library(monosgen-shared-dac SHARED "mini-windows-dlldac.c")
    target_link_libraries(monosgen-shared-dac PRIVATE monoapi eglib_api)
    set_target_properties(monosgen-shared-dac PROPERTIES OUTPUT_NAME ${MONO_SHARED_LIB_NAME}-dac)
  endif()

  if(BUILD_DARWIN_FRAMEWORKS)
    if(TARGET_DARWIN)
      # In cmake, you cannot have list entries which contain a space or semicolon - those are considered
      # record separators (i.e. a list of list(APPEND foo "a" "b;c" "d e") is a five entry list of values
      # a, b, c, d and e.
      # So, in order to treat the components lists as single list entries, swap out the ; character
      # for a temporary replacement character, allowing the full lists to be treated as single entries
      string(REPLACE ";" "*" mono-components-objects-nowhitespace "${mono-components-objects}")
      string(REPLACE ";" "*" mono-components-stub-objects-nowhitespace "${mono-components-stub-objects}")
      list(APPEND FrameworkConfig Mono.debug Mono.release)
      list(APPEND ComponentsObjects "${mono-components-objects-nowhitespace}" "${mono-components-stub-objects-nowhitespace}")
      foreach(frameworkconfig componentsobjects IN ZIP_LISTS FrameworkConfig ComponentsObjects)
        if("${componentsobjects}" STREQUAL "")
          #components list is empty, use stubs instead
          set(componentsobjects "${mono-components-stub-objects-nowhitespace}")
        endif()
        add_library(${frameworkconfig} SHARED $<TARGET_OBJECTS:monosgen-objects>)
        target_compile_definitions(${frameworkconfig} PRIVATE -DMONO_DLL_EXPORT)

        set(FRAMEWORKCONFIG_LINKABLE_LIBS "")
        list(APPEND FRAMEWORKCONFIG_LINKABLE_LIBS monoapi eglib_objects utils_objects sgen_objects metadata_objects dn-containers minipal)
        if (NOT CLR_CMAKE_USE_SYSTEM_ZLIB)
          list(APPEND FRAMEWORKCONFIG_LINKABLE_LIBS zlib)
        endif()
        target_link_libraries(${frameworkconfig} PRIVATE ${FRAMEWORKCONFIG_LINKABLE_LIBS})

        set(FRAMEWORKCONFIG_LINKABLE_EXTRALIBS "")
        list(APPEND FRAMEWORKCONFIG_LINKABLE_EXTRALIBS ${OS_LIBS} ${LLVM_LIBS})
        if (NOT CLR_CMAKE_USE_SYSTEM_ZLIB)
          list(APPEND FRAMEWORKCONFIG_LINKABLE_EXTRALIBS zlib)
        else()
          list(APPEND FRAMEWORKCONFIG_LINKABLE_EXTRALIBS ${Z_LIBS})
        endif()
        target_link_libraries(${frameworkconfig} PRIVATE ${FRAMEWORKCONFIG_LINKABLE_EXTRALIBS})

        set_property(TARGET ${frameworkconfig} APPEND_STRING PROPERTY LINK_FLAGS " -Wl,-compatibility_version -Wl,2.0 -Wl,-current_version -Wl,2.0")
        string(REPLACE "*" ";" componentsobjects-whitespace "${componentsobjects}")
        target_sources(${frameworkconfig} PRIVATE "${componentsobjects-whitespace}")
        set_target_properties(${frameworkconfig} PROPERTIES
          FRAMEWORK TRUE
          FRAMEWORK_VERSION C
          MACOSX_FRAMEWORK_IDENTIFIER net.dot.mono-framework
        )
        install_with_stripped_symbols(${frameworkconfig} TARGETS ${CMAKE_INSTALL_LIBDIR})
      endforeach()
    endif()
  endif()
endif()

if(HOST_BROWSER)
  # Add two static libs containing llvm-runtime.cpp compiled for JS based/WASM EH
  # This is the only source file which contains a c++ throw or catch
  add_library(mono-wasm-eh-js STATIC llvm-runtime.cpp)
  target_link_libraries (mono-wasm-eh-js PRIVATE monoapi eglib_api)
  set_target_properties(mono-wasm-eh-js PROPERTIES COMPILE_FLAGS "-fexceptions")
  set_target_properties(mono-wasm-eh-js PROPERTIES LINK_FLAGS "-fexceptions")
  install(TARGETS mono-wasm-eh-js LIBRARY)

  add_library(mono-wasm-eh-wasm STATIC llvm-runtime.cpp)
  target_link_libraries (mono-wasm-eh-wasm PRIVATE monoapi eglib_api)
  set_target_properties(mono-wasm-eh-wasm PROPERTIES COMPILE_FLAGS "-fwasm-exceptions")
  set_target_properties(mono-wasm-eh-wasm PROPERTIES LINK_FLAGS "-fwasm-exceptions")
  install(TARGETS mono-wasm-eh-wasm LIBRARY)
endif()

if(HOST_BROWSER OR HOST_WASI)
  add_library(mono-wasm-simd STATIC interp/interp-simd.c)
  target_link_libraries (mono-wasm-simd PRIVATE monoapi eglib_api)
  set_target_properties(mono-wasm-simd PROPERTIES COMPILE_FLAGS "-msimd128")
  install(TARGETS mono-wasm-simd LIBRARY)
endif()

if(HOST_BROWSER OR HOST_WASI OR TARGET_WASM)
  add_library(mono-wasm-nosimd STATIC interp/interp-nosimd.c)
  target_link_libraries (mono-wasm-nosimd PRIVATE monoapi eglib_api)
  install(TARGETS mono-wasm-nosimd LIBRARY)
endif()

find_package(Python COMPONENTS Interpreter)

add_custom_command(
  OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/cpu-amd64.h
  COMMAND ${Python_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py TARGET_AMD64 ${CMAKE_CURRENT_SOURCE_DIR} cpu-amd64.h amd64_desc ${CMAKE_CURRENT_SOURCE_DIR}/cpu-amd64.mdesc
  DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py mini-ops.h ${CMAKE_CURRENT_SOURCE_DIR}/cpu-amd64.mdesc
  VERBATIM
)

add_custom_command(
  OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/cpu-x86.h
  COMMAND ${Python_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py TARGET_X86 ${CMAKE_CURRENT_SOURCE_DIR} cpu-x86.h x86_desc ${CMAKE_CURRENT_SOURCE_DIR}/cpu-x86.mdesc
  DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py mini-ops.h ${CMAKE_CURRENT_SOURCE_DIR}/cpu-x86.mdesc
  VERBATIM
)

add_custom_command(
  OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/cpu-arm64.h
  COMMAND ${Python_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py TARGET_ARM64 ${CMAKE_CURRENT_SOURCE_DIR} cpu-arm64.h arm64_cpu_desc ${CMAKE_CURRENT_SOURCE_DIR}/cpu-arm64.mdesc
  DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py mini-ops.h ${CMAKE_CURRENT_SOURCE_DIR}/cpu-arm64.mdesc
  VERBATIM
)

add_custom_command(
  OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/cpu-arm.h
  COMMAND ${Python_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py TARGET_ARM ${CMAKE_CURRENT_SOURCE_DIR} cpu-arm.h arm_cpu_desc ${CMAKE_CURRENT_SOURCE_DIR}/cpu-arm.mdesc
  DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py mini-ops.h ${CMAKE_CURRENT_SOURCE_DIR}/cpu-arm.mdesc
  VERBATIM
)

add_custom_command(
  OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/cpu-riscv64.h
  COMMAND ${Python_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py TARGET_RISCV64 ${CMAKE_CURRENT_SOURCE_DIR} cpu-riscv64.h riscv64_cpu_desc ${CMAKE_CURRENT_SOURCE_DIR}/cpu-riscv64.mdesc
  DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py mini-ops.h ${CMAKE_CURRENT_SOURCE_DIR}/cpu-riscv64.mdesc
  VERBATIM
)

add_custom_command(
  OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/cpu-s390x.h
  COMMAND ${Python_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py TARGET_S390X ${CMAKE_CURRENT_SOURCE_DIR} cpu-s390x.h s390x_cpu_desc ${CMAKE_CURRENT_SOURCE_DIR}/cpu-s390x.mdesc
  DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py mini-ops.h ${CMAKE_CURRENT_SOURCE_DIR}/cpu-s390x.mdesc
  VERBATIM
)

add_custom_command(
  OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/cpu-wasm.h
  COMMAND ${Python_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py TARGET_WASM ${CMAKE_CURRENT_SOURCE_DIR} cpu-wasm.h wasm_desc ${CMAKE_CURRENT_SOURCE_DIR}/cpu-wasm.mdesc
  DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py mini-ops.h ${CMAKE_CURRENT_SOURCE_DIR}/cpu-wasm.mdesc
  VERBATIM
)

add_custom_command(
  OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/cpu-ppc64.h
  COMMAND ${Python_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/genmdesc.py TARGET_POWERPC64 ${CMAKE_CURRENT_SOURCE_DIR} cpu-ppc64.h ppc64_cpu_desc ${CMAKE_CURRENT_SOURCE_DIR}/cpu-ppc64.mdesc
  VERBATIM
)

if(NOT DISABLE_EXECUTABLES)
  set(main_sources "main.c")
  if(HOST_WIN32)
    set(sgen_sources "${main_sources};${VERSION_FILE_RC_PATH}")
  else()
    set(sgen_sources "${main_sources}")
  endif()
  add_executable(mono-sgen "${sgen_sources}")
  if(MONO_CROSS_COMPILE_EXECUTABLE_NAME)
    set_target_properties(mono-sgen PROPERTIES OUTPUT_NAME mono-aot-cross)
    if(MONO_SET_RPATH_ORIGIN)
      set_target_properties(mono-sgen PROPERTIES INSTALL_RPATH "$ORIGIN")
    endif()
  endif()
  
  set(MONOSGEN_LINKABLE_LIBS "")
  list(APPEND MONOSGEN_LINKABLE_LIBS monoapi eglib_api monosgen-static dn-containers)
  if (NOT CLR_CMAKE_USE_SYSTEM_ZLIB)
    list(APPEND MONOSGEN_LINKABLE_LIBS zlib)
  endif()
  target_link_libraries(mono-sgen PRIVATE ${MONOSGEN_LINKABLE_LIBS})

  if (HOST_WASM)
    target_link_libraries(mono-sgen PRIVATE mono-wasm-nosimd)
  endif()
  
  set(MONOSGEN_LINKABLE_EXTRALIBS "")
  list(APPEND MONOSGEN_LINKABLE_EXTRALIBS ${OS_LIBS} ${LLVM_LIBS})
  if (CLR_CMAKE_USE_SYSTEM_ZLIB)
    list(APPEND MONOSGEN_LINKABLE_EXTRALIBS ${Z_LIBS})
  else()
    list(APPEND MONOSGEN_LINKABLE_EXTRALIBS zlib)
  endif()
  target_link_libraries(mono-sgen PRIVATE ${MONOSGEN_LINKABLE_EXTRALIBS})

  # musl-libc implements ucontext in a different library on s390x
  if(CLR_CMAKE_TARGET_LINUX_MUSL AND TARGET_S390X)
    target_link_libraries(mono-sgen PRIVATE ucontext)
  endif(CLR_CMAKE_TARGET_LINUX_MUSL AND TARGET_S390X)
  if(NOT DISABLE_COMPONENTS AND STATIC_COMPONENTS AND NOT DISABLE_LINK_STATIC_COMPONENTS)
    # if components are built statically, link them into runtime.
    target_sources(mono-sgen PRIVATE "${mono-components-objects}")
  elseif(NOT DISABLE_COMPONENTS AND STATIC_COMPONENTS AND DISABLE_LINK_STATIC_COMPONENTS)
    # if components are built statically, but we shouldn't link components
    # link the fallback stubs into the runtime
    target_sources(mono-sgen PRIVATE "${mono-components-stub-objects}")
  elseif(NOT DISABLE_COMPONENTS AND NOT STATIC_COMPONENTS)
    # if components are built dynamically, link the fallback stubs into the runtime
    target_sources(mono-sgen PRIVATE "${mono-components-stub-objects}")
  elseif(DISABLE_COMPONENTS)
    # if components are disabled, link the fallback stubs into the runtime
    # fallback stubs already provided in monosgen-static when components are disabled
  endif()

  if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    target_sources(mono-sgen PRIVATE ${mono_validate_apis_source})
  endif()

  install_with_stripped_symbols(mono-sgen TARGETS bin)
endif()
