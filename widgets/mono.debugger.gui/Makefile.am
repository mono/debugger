MCS=mcs

assdir = $(libdir)

ass_SCRIPTS = mono.debugger.gui-sharp.dll

all: mono.debugger.gui-sharp.dll

mono.debugger.gui-sharp.dll: @srcdir@/generated/*.cs
	$(MCS) --unsafe --target library -L $(GTKSHARP_PREFIX)/lib -r glib-sharp.dll -r gtk-sharp.dll -r gdk-sharp.dll -r ../gtk/gtksourceview-sharp.dll -o mono.debugger.gui-sharp.dll --recurse '@srcdir@/*.cs'

clean:
	rm -f *.dll

maintainerclean:
	rm -rf generated

install: all
	cp mono.debugger.gui-sharp.dll $(DESTDIR)@prefix@/lib

EXTRA_DIST = @srcdir@/generated/*.cs
