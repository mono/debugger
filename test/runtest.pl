#!/usr/bin/perl -w

use strict;

unless ($#ARGV == 1) {
    print STDERR "Usage: $0 debugger-path testcase.exe\n";
    exit 1;
}

my $filename = $ARGV [1];
my $commandfile = $filename;
$commandfile =~ s/\.exe$/.cmd/;

unless (-f $filename) {
    print STDERR "Testcase $filename does not exist\n";
    exit 2;
}

unless (-f $commandfile) {
    print STDERR "Command file $commandfile does not exist.\n";
    exit 3;
}

my $debugger = $ARGV [0];

my $retval = system ("$debugger $filename < $commandfile");
if ($retval != 0) {
    exit 4;
} else {
    exit 0;
}



