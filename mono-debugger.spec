%define name	mono-debugger
%define version	0.5
%define release	1

Summary:	Mono Debugger.
Name:		%name
Version:	%version
Release:	%release
License:	GPL
Group:		System/Development
Source0:	%name-%version.tar.gz
URL:		http://www.go-mono.com/
BuildRoot:	%{_tmppath}/%{name}-%{version}-root
BuildRequires:	pkgconfig
Requires:	mono >= 0.29
Requires:	glib2 >= 2.2
ExclusiveArch:	%{ix86}

%description
The Mono Debugger

%prep
%setup

%build
(CC="cc -gdwarf-2" ./configure --sysconfdir=%{_sysconfdir} --prefix=%{_prefix} --mandir=%{_mandir})
make

%install
rm -rf %{buildroot}
%makeinstall

%clean
rm -rf %{buildroot}

%files
%defattr(-, root, root)
%{_bindir}/*
%{_libdir}/*
%{_libexecdir}/*
%doc AUTHORS README README.FreeBSD README.build TODO NEWS ChangeLog RELEASE-NOTES*

%changelog
* Tue Dec 9 2003 Martin Baulig <martin@ximian.com> 0.5-1
- initial release
