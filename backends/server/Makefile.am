INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@

bin_PROGRAMS = mono-debugger-server

mono_debugger_server_SOURCES = \
	server.c		\
	server.h		\
	#

mono_debugger_server_LDADD = @SERVER_DEPENDENCIES_LIBS@

lib_LTLIBRARIES = libmonodebuggerserver.la

libmonodebuggerserver_la_SOURCES = \
	spawn.c			\
	library.c		\
	server.h		\
	#

libmonodebuggerserver_la_LIBADD = @BASE_DEPENDENCIES_LIBS@

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@

CLEANFILES = lib*.a lib*.dll
