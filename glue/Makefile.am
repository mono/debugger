lib_LTLIBRARIES = libmonodebuggerglue.la libmonodebuggerreadline.la

libmonodebuggerglue_la_SOURCES = \
	iochannel.c		\
	mono-debugger-glue.h	\
	#

libmonodebuggerglue_la_LIBADD = @BASE_DEPENDENCIES_LIBS@

libmonodebuggerreadline_la_SOURCES = \
	mono-debugger-readline.c	\
	mono-debugger-readline.h

libmonodebuggerreadline_la_LIBADD = @BASE_DEPENDENCIES_LIBS@ @READLINE_DEPLIBS@

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@ @glue_cflags@

CLEANFILES = lib*.a lib*.dll

