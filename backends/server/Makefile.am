INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@

lib_LTLIBRARIES = libmonodebuggerserver.la

libmonodebuggerserver_la_SOURCES = \
	library.c		\
	i386-linux-ptrace.c	\
	server.h		\
	#

libmonodebuggerserver_la_LIBADD = @BASE_DEPENDENCIES_LIBS@

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@

CLEANFILES = lib*.a lib*.dll
