lib_LTLIBRARIES = libmonodebuggerremoting.la

libmonodebuggerremoting_la_SOURCES = \
	mono-debugger-remoting.c

libmonodebuggerremoting_la_LIBADD = @BASE_DEPENDENCIES_LIBS@ @READLINE_DEPLIBS@
libmonodebuggerremoting_la_LDFLAGS = -no-undefined -shared -export-dynamic

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@ @glue_cflags@

CLEANFILES = lib*.a lib*.dll

