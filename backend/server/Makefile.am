INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@

lib_LTLIBRARIES = libmonodebuggerserver.la

EXTRA_libmonodebuggerserver_la_SOURCES = \
	i386-arch.c			\
	i386-arch.h			\
	x86-arch.h			\
	x86_64-arch.h			\
	x86_64-arch.c			\
	x86-linux-ptrace.c		\
	x86-linux-ptrace.h		\
	x86-ptrace.c			\
	x86-ptrace.h

if PLATFORM_POWERPC
platform_sources =
else
if PLATFORM_X86_DARWIN
platform_sources = \
	x86-ptrace.c \
	darwin-thread-db.c \
	thread-db.h
libmonodebuggerserver_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@ \
	../os/libmonodebuggerbfdglue.la
else
platform_sources = \
	x86-ptrace.c \
	thread-db.c	\
	thread-db.h
libmonodebuggerserver_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@ -lthread_db \
	../os/libmonodebuggerbfdglue.la
endif
endif

libmonodebuggerserver_la_SOURCES = \
	server.h		\
	library.c		\
	$(platform_sources)	\
	breakpoints.c		\
	breakpoints.h		\
	libgtop-glue.c		\
	libgtop-glue.h		\
	linux-proc-service.h

libmonodebuggerserver_la_LDFLAGS = \
	 -no-undefined -export-dynamic -shared

CLEANFILES = lib*.a lib*.dll
