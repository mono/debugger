AM_CFLAGS = -DSKEL_DIRECTORY=\""$(prefix)/share/jay"\"

noinst_PROGRAMS=jay
jay_SOURCES = closure.c error.c lalr.c lr0.c main.c mkpar.c output.c reader.c \
              symtab.c verbose.c warshall.c defs.h

EXTRA_DIST = ACKNOWLEDGEMENTS NEW_FEATURES NOTES README README.jay skeleton \
             skeleton.cs jay.1

DISTFILES = $(datafiles) $(sources) jay.1 $(wildcard *.h)
