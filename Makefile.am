if BUILD_GUI
gui_SUBDIRS = widgets
else
gui_SUBDIRS =
endif

SUBDIRS = interfaces classes glue arch backends $(gui_SUBDIRS) frontends lib wrapper test doc

EXTRA_DIST = RELEASE-NOTES-0.1.0.txt RELEASE-NOTES-0.2.0.txt RELEASE-NOTES-0.2.2.txt \
	     RELEASE-NOTES-0.3.txt README.FreeBSD README.build
