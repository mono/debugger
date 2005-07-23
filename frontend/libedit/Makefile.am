lib_LTLIBRARIES = libmonodebuggerreadline.la

libmonodebuggerreadline_la_SOURCES = \
	chared.c common.c el.c emacs.c fcns.c help.c hist.c key.c map.c \
	parse.c prompt.c read.c refresh.c search.c sig.c term.c tty.c vi.c

libmonodebuggerreadline_la_LIBADD = @BASE_DEPENDENCIES_LIBS@ @READLINE_DEPLIBS@
libmonodebuggerreadline_la_LDFLAGS = -no-undefined -export-dynamic -shared

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@ @glue_cflags@

CLEANFILES = lib*.a lib*.dll

