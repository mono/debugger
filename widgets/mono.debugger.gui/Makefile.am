MCS=mcs

all: debugger-widgets.dll

debugger-widgets.dll: generated/*.cs
	$(MCS) --unsafe --target library -L $(GTKSHARP_PREFIX)/lib -r glib-sharp.dll -r gtk-sharp.dll -r gdk-sharp.dll -o debugger-widgets.dll --recurse '*.cs'

clean:
	rm -f *.dll
	rm -rf generated

install: all
	cp debugger-widgets.dll $(DESTDIR)@prefix@/lib

