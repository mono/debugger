#!/bin/bash

csc /target:library /out:Mono.Debugger.dll /r:Mono.CSharp.Debugger.dll /recurse:..\\classes\\*.cs /recurse:..\\interfaces\\*.cs
csc /target:library /out:Mono.Debugger.Backend.dll /r:Mono.GetOptions.dll /r:Mono.Debugger.dll /r:Mono.CSharp.Debugger.dll /recurse:..\\backends\\*.cs /recurse:..\\arch\\*.cs
csc /out:Interpreter.exe /r:Mono.GetOptions.dll /r:Mono.Debugger.dll /r:Mono.Debugger.Backend.dll /r:Mono.CSharp.Debugger.dll /recurse:..\\frontends\\command-line\\*.cs /recurse:..\\frontends\\scripting\\*.cs




