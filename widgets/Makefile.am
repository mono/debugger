SUBDIRS = sources generator gtk mono.debugger.gui

api: 
	PERLLIB=$(GTKSHARP_PREFIX)/share/perl5 PATH=$(GTKSHARP_PREFIX)/bin:$$PATH gapi.pl gtksourceview.sources generator
	PERLLIB=$(GTKSHARP_PREFIX)/share/perl5 PATH=$(GTKSHARP_PREFIX)/bin:$$PATH gapi.pl debuggerwidget.sources generator

