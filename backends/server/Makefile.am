INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@

lib_LTLIBRARIES = libmonodebuggerserver.la

EXTRA_libmonodebuggerserver_la_SOURCES = \
	i386-linux-ptrace.c		\
	i386-linux-ptrace.h		\
	i386-win32.c			\
	i386-win32.h			\
	i386-arch.c			\
	i386-arch.h			\
	i386-ptrace.c

if MARTIN_HACKS
martin_hack_srclist = \
	remote-client.c		\
	Debugger-stubs.c	\
	Debugger-common.c

martin_hack_progs = remote-server

Debugger-stubs.c Debugger-common.c Debugger-skels.c Debugger.h : Debugger.idl
	orbit-idl-2 $(srcdir)/Debugger.idl
else
martin_hack_srclist =
martin_hack_progs =
endif

if PLATFORM_POWERPC
server_sources = powerpc-darwin.c
else
server_sources = i386-ptrace.c
endif

noinst_PROGRAMS = \
	$(martin_hack_progs)

remote_server_SOURCES = \
	remote-server.c		\
	debugger-srv.c		\
	Debugger-skels.c	\
	$(server_sources)

libmonodebuggerserver_la_SOURCES = \
	server.h		\
	library.c		\
	$(martin_hack_srclist)	\
	$(server_sources)	\
	breakpoints.c		\
	breakpoints.h

remote_server_LDADD = @SERVER_DEPENDENCIES_LIBS@ libmonodebuggerserver.la

libmonodebuggerserver_la_LIBADD = @SERVER_DEPENDENCIES_LIBS@ @server_sources@
libmonodebuggerserver_la_LDFLAGS = -no-undefined -module -export-dynamic -shared

CLEANFILES = lib*.a lib*.dll
