INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@

lib_LTLIBRARIES = libmonodebuggerserver.la

libmonodebuggerserver_la_SOURCES = \
	library.c		\
	i386-linux-ptrace.c	\
	server.h		\
	breakpoints.c		\
	breakpoints.h		\
	#

libmonodebuggerserver_la_LIBADD = @SERVER_DEPENDENCIES_LIBS@

INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@

CLEANFILES = lib*.a lib*.dll
