INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@

lib_LTLIBRARIES = libmonodebuggerserver.la

EXTRA_libmonodebuggerserver_la_SOURCES = \
	i386-linux-ptrace.c

libmonodebuggerserver_la_SOURCES = \
	library.c		\
	@server_sources@	\
	server.h		\
	breakpoints.c		\
	breakpoints.h		\
	#

libmonodebuggerserver_la_LIBADD = @SERVER_DEPENDENCIES_LIBS@

CLEANFILES = lib*.a lib*.dll
