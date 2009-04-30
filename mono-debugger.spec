Name:           mono-debugger
License:        GPL v2 or later; LGPL v2.0 or later; X11/MIT
Group:          Development/Languages/Mono
Summary:        Mono Debugger
Url:            http://www.mono-project.com/Debugger
Version:        2.4.1
Release:        0
BuildRoot:      %{_tmppath}/%{name}-%{version}-build
Source0:        %{name}-%{version}.tar.bz2
Provides:       mono-debugger = %{version}-%{release}
ExclusiveArch:  %ix86 x86_64
Requires:       mono-core >= 2.0
BuildRequires:  mono-devel mono-nunit
# For older distros (but are harmless for new distros)
BuildRequires:  mono-web pkgconfig
#### suse options ###
%if 0%{?suse_version}
# factory needed this... ?
#  All distro versions need it, but it was installed by default up until 10.3
%if 0%{suse_version} > 1020
BuildRequires:  ncurses-devel
%endif
# For SLES9
%if 0%sles_version == 9
%define configure_options export PKG_CONFIG_PATH=$PKG_CONFIG_PATH:/opt/gnome/%_lib/pkgconfig
BuildRequires:  pkgconfig
%endif
%endif
# Fedora options (Bug in fedora images where 'abuild' user is the same id as 'nobody')
%if 0%{?fedora_version} || 0%{?rhel_version}
%define env_options export MONO_SHARED_DIR=/tmp
# Note: this fails to build on fedora5 x86_64 because of this bug:
# https://bugzilla.redhat.com/bugzilla/show_bug.cgi?id=189324
%endif

%description
A debugger is an important tool for development. The Mono Debugger
(MDB) can debug both managed and unmanaged applications.  It provides a
reusable library that can be used to add debugger functionality to
different front-ends. The debugger package includes a console debugger
named "mdb", and MonoDevelop (http://www.monodevelop.com) provides a
GUI interface to the debugger.



Authors:
--------
    Martin Baulig <martin@ximian.com>
    Chris Toshok <toshok@ximian.com>
    Miguel de Icaza <miguel@ximian.com>

%files
%defattr(-, root, root)
%doc AUTHORS COPYING ChangeLog README NEWS
%{_bindir}/mdb*
%{_libdir}/*.so*
%{_prefix}/lib/mono/2.0/mdb*.exe
%{_prefix}/lib/mono/gac/Mono.Debugger*
%{_prefix}/lib/mono/mono-debugger
%{_libdir}/pkgconfig/mono-debugger*.pc

%prep
%setup  -q -n mono-debugger-%{version}

%build
%{?env_options}
%{?configure_options}
CFLAGS="$RPM_OPT_FLAGS"
%if 0%{suse_version} >= 1100
CFLAGS="$RPM_OPT_FLAGS `ncurses5-config --cflags`"
%endif
%configure
make

%install
%{?env_options}
make DESTDIR="$RPM_BUILD_ROOT" install
# Remove unnecessary devel files
rm -f $RPM_BUILD_ROOT%_libdir/libmonodebuggerreadline.*a
rm -f $RPM_BUILD_ROOT%_libdir/libmonodebuggerserver.*a

%clean
rm -rf ${RPM_BUILD_ROOT}

%post -p /sbin/ldconfig

%postun -p /sbin/ldconfig
%if 0%{?fedora_version} || 0%{?rhel_version}
# Allows overrides of __find_provides in fedora distros... (already set to zero on newer suse distros)
%define _use_internal_dependency_generator 0
%endif
%define __find_provides env sh -c 'filelist=($(cat)) && { printf "%s\\n" "${filelist[@]}" | /usr/lib/rpm/find-provides && printf "%s\\n" "${filelist[@]}" | /usr/bin/mono-find-provides ; } | sort | uniq'
%define __find_requires env sh -c 'filelist=($(cat)) && { printf "%s\\n" "${filelist[@]}" | /usr/lib/rpm/find-requires && printf "%s\\n" "${filelist[@]}" | /usr/bin/mono-find-requires ; } | sort | uniq'

%changelog
