step
break "D.cs" 54
continue
assert line 54
assert accessable $a
assert accessable $b
assert accessable $f
assert kind fundamental $a
assert contents "5" $a
assert contents "7" $b
assert contents "Hello World" $hello
