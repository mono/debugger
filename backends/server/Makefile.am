INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@

lib_LTLIBRARIES = libmonodebuggerserver.la

EXTRA_libmonodebuggerserver_la_SOURCES = \
	i386-linux-ptrace.c		\
	i386-linux-ptrace.h		\
	i386-freebsd-ptrace.c		\
	i386-freebsd-ptrace.h		\
	i386-arch.c			\
	i386-arch.h			\
	i386-ptrace.c

libmonodebuggerserver_la_SOURCES = \
	library.c		\
	server.h		\
	breakpoints.c		\
	breakpoints.h		\
	#

libmonodebuggerserver_la_LIBADD = @SERVER_DEPENDENCIES_LIBS@
libmonodebuggerserver_la_LDFLAGS = -no-undefined -module -export-dynamic -shared

CLEANFILES = lib*.a lib*.dll
