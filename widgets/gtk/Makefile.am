all: gtksourceview-sharp.dll

gtksourceview-sharp.dll: @srcdir@/generated/*.cs
	$(MCS) --unsafe --target library -L $(GTKSHARP_PREFIX)/lib -r glib-sharp.dll -r gtk-sharp.dll -r gdk-sharp.dll -o gtksourceview-sharp.dll --recurse '@srcdir@/*.cs'

clean:
	rm -f *.dll

maintainerclean:
	rm -rf generated

install: all
	cp gtksourceview-sharp.dll $(DESTDIR)@prefix@/lib

EXTRA_DIST = @srcdir@/generated/*.cs

