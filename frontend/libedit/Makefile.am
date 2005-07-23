lib_LTLIBRARIES = libmonodebuggerreadline.la

libmonodebuggerreadline_la_SOURCES = \
	mono-debugger-readline.c readline.c \
	chared.c common.c el.c emacs.c fcns.c help.c hist.c history.c key.c map.c \
	parse.c prompt.c read.c refresh.c search.c sig.c term.c tty.c vi.c vis.c unvis.c \
	strlcpy.c strlcat.c

libmonodebuggerreadline_la_LIBADD = @BASE_DEPENDENCIES_LIBS@ @READLINE_DEPLIBS@
libmonodebuggerreadline_la_LDFLAGS = -no-undefined -shared -Wl,-version-script=$(srcdir)/ldscript

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@ @glue_cflags@

CLEANFILES = lib*.a lib*.dll

