lib_LTLIBRARIES = libmonodebuggerreadline.la

libmonodebuggerreadline_la_SOURCES = \
	mono-debugger-readline.c mono-debugger-readline.h readline.c readline.h \
	chared.c common.c el.c emacs.c fcns.c help.c hist.c history.c key.c map.c \
	parse.c prompt.c read.c refresh.c search.c sig.c term.c tty.c vi.c vis.c unvis.c \
	strlcpy.c strlcat.c chared.h common.h el.h emacs.h fcns.h help.h histedit.h hist.h \
	key.h map.h parse.h prompt.h read.h refresh.h search.h sig.h sys.h term.h tty.h \
	vi.h vis.h el_term.h filecomplete.h tokenizer.c

libmonodebuggerreadline_la_LIBADD = @BASE_DEPENDENCIES_LIBS@ @READLINE_DEPLIBS@
libmonodebuggerreadline_la_LDFLAGS = -no-undefined -shared -Wl,-version-script=$(srcdir)/ldscript

EXTRA_DIST = ldscript

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@ @glue_cflags@

CLEANFILES = lib*.a lib*.dll

