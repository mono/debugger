lib_LTLIBRARIES = libmonodebuggerreadline.la

libmonodebuggerreadline_la_SOURCES = \
	mono-debugger-readline.c	\
	mono-debugger-readline.h

libmonodebuggerreadline_la_LIBADD = @BASE_DEPENDENCIES_LIBS@ @READLINE_DEPLIBS@
libmonodebuggerreadline_la_LDFLAGS = -no-undefined -module -export-dynamic -shared

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@ @glue_cflags@

CLEANFILES = lib*.a lib*.dll

