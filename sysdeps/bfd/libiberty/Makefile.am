WARN_CFLAGS = @WARN_CFLAGS@
AM_CFLAGS = $(WARN_CFLAGS)

INCLUDES = -D_GNU_SOURCE -I$(srcdir)/../include

noinst_LTLIBRARIES = libiberty.la

libiberty_la_SOURCES = \
	concat.c hashtab.c hex.c objalloc.c safe-ctype.c \
	xexit.c xmalloc.c xstrerror.c fnmatch.c basename.c \
	xstrdup.c
libiberty_la_LDFLAGS = -export-dynamic
