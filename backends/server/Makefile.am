INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@

lib_LTLIBRARIES = libmonodebuggerserver.la

EXTRA_libmonodebuggerserver_la_SOURCES = \
	i386-linux-ptrace.c		\
	i386-linux-ptrace.h		\
	i386-win32.c			\
	i386-win32.h			\
	i386-arch.c			\
	i386-arch.h			\
	i386-ptrace.c			\
	i386-ptrace.h

if PLATFORM_POWERPC
platform_sources = powerpc-darwin.c
else
platform_sources = i386-ptrace.c
endif

libmonodebuggerserver_la_SOURCES = \
	server.h		\
	library.c		\
	$(platform_sources)	\
	breakpoints.c		\
	breakpoints.h		\
	mutex.c			\
	mutex.h

libmonodebuggerserver_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@ \
	../../arch/libmonodebuggerbfdglue.la
libmonodebuggerserver_la_LDFLAGS = \
	 -no-undefined -export-dynamic -shared

CLEANFILES = lib*.a lib*.dll
