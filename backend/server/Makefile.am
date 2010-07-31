lib_LTLIBRARIES = libmonodebuggerserver.la

noinst_LTLIBRARIES = libmonodebuggerbfdglue.la

libmonodebuggerbfdglue_la_SOURCES = \
	bfdglue.c \
	bfdglue.h

libmonodebuggerbfdglue_la_LIBADD = \
	../../sysdeps/bfd/opcodes/libopcodes.la ../../sysdeps/bfd/libbfd.la
libmonodebuggerbfdglue_la_LDFLAGS = \
	-no-undefined -module -export-dynamic -shared

libmonodebuggerbfdglue_la_CPPFLAGS = \
	-I$(top_srcdir)/sysdeps/bfd \
	-I$(top_srcdir)/sysdeps/bfd/include \
	-I$(top_srcdir)/sysdeps/bfd/opcodes

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
	../../sysdeps/bfd/libbfd.la
else
platform_sources = \
	x86-ptrace.c \
	thread-db.c	\
	thread-db.h
libmonodebuggerserver_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@ -lthread_db \
	libmonodebuggerbfdglue.la

endif
endif

libmonodebuggerserver_la_CPPFLAGS = @SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@

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
