INCLUDES = \
	-DG_LOG_DOMAIN=\"DebuggerWidgets\" \
	-I$(top_srcdir) \
	-I$(top_srcdir)/src \
	-I$(top_srcdir)/widgets/sources/gtksourceview \
	@BUILD_GTK_CFLAGS@

# libs
lib_LTLIBRARIES = libdebuggerwidgets.la

BUILT_SOURCES = \
	debuggerwidgets-marshal.c \
	debuggerwidgets-marshal.h

$(libdebuggerwidgets_la_SOURCES): $(BUILD_SOURCES)

libdebuggerwidgets_la_SOURCES = \
	debuggerentry.c			\
	debuggerentry.h			\
	debuggerterminal.c		\
	debuggerterminal.h		\
	debuggersourceview.c		\
	debuggersourceview.h		\
	debuggerwidgets-marshal.c	\
	debuggerwidgets-marshal.h

libdebuggerwidgets_la_LIBADD = @BUILD_GTK_LIBS@ \
	$(top_builddir)/widgets/sources/gtksourceview/libgtksourceview.la

debuggerwidgets-marshal.h: debuggerwidgets-marshal.list
	glib-genmarshal \
		--prefix=debuggerwidgets_marshal $(srcdir)/debuggerwidgets-marshal.list --header > xgen-gmc \
	&& cp xgen-gmc $(@F) \
	&& rm -f xgen-gmc

debuggerwidgets-marshal.c: debuggerwidgets-marshal.h
	echo "#include \"debuggerwidgets-marshal.h\"" > xgen-gmc \
	&& glib-genmarshal \
		--prefix=debuggerwidgets_marshal $(srcdir)/debuggerwidgets-marshal.list --body >> xgen-gmc \
	&& cp xgen-gmc $(@F) \
	&& rm -f xgen-gmc

EXTRA_DIST = debuggerwidgets-marshal.list
