break "TestManagedTypes.cs":86
break "TestManagedTypes.cs":98
break "TestManagedTypes.cs":108
continue
assert line 86
assert accessible $a
assert accessible $b
assert accessible $f
assert kind fundamental $a
assert contents "5" $a
assert contents "7" $b
assert contents "Hello World" $hello
continue
assert line 98
assert accessible $a
assert accessible $boxed_a
assert kind fundamental $a
assert kind pointer $boxed_a
assert accessible *$boxed_a
assert kind fundamental *$boxed_a
assert contents "5" *$boxed_a
continue
assert line 108
assert accessible $hello
assert accessible $boxed_hello
assert kind fundamental $hello
assert kind pointer $boxed_hello
assert accessible *$boxed_hello
assert kind fundamental *$boxed_hello
assert contents "Hello World" *$boxed_hello
