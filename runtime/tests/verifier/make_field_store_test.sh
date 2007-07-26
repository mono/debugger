#! /bin/sh

TEST_NAME=$1
TEST_VALIDITY=$2
TEST_OP=$3
TEST_TYPE1=$4
TEST_TYPE2=$5

TEST_NAME=${TEST_VALIDITY}_${TEST_NAME}
TEST_FILE=${TEST_NAME}_generated.il
echo $TEST_FILE
#TEST_TYPE1=`echo $TEST_TYPE1 | sed -s 's/&/\\\&/'`
#TEST_TYPE2=`echo $TEST_TYPE2 | sed -s 's/&/\\\&/'`
sed -e "s/VALIDITY/${TEST_VALIDITY}/g"  -e "s/OPCODE/${TEST_OP}/g" -e "s/TYPE1/${TEST_TYPE1}/g" -e "s/TYPE2/${TEST_TYPE2}/g" > $TEST_FILE <<//EOF

.assembly '${TEST_NAME}_generated'
{
  .hash algorithm 0x00008004
  .ver  0:0:0:0
}

// VALIDITY CIL which breaks the ECMA-335 rules. 
// this CIL should fail verification by a conforming CLI verifier.

.assembly extern mscorlib
{
  .ver 1:0:5000:0
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 ) // .z\V.4..
}

.class interface abstract InterfaceA
{
}

.class interface abstract InterfaceB
{
}

.class sealed MyValueType extends [mscorlib]System.ValueType
{
	.field private int32 fld
}

.class ClassB extends [mscorlib]System.Object
{
    .field public TYPE1 fld
    .field public static TYPE1 sfld
}

.class ClassA extends [mscorlib]System.Object
{
    .field public TYPE1 fld
    .field public static TYPE1 sfld
    .field public initonly TYPE1 const_field
    .field public static initonly TYPE1 st_const_field
}

.class SubClass extends ClassA
{
    .field public TYPE1 subfld
    .field public static TYPE1 subsfld
}

.class explicit Overlapped extends [mscorlib]System.Object
{
    .field[0] public TYPE1 field1
    .field[0] public TYPE1 field2
    .field[8] public TYPE1 field3
    .field[8] public TYPE1 field4
    .field[16] public TYPE1 field5
    
    .field[20] public TYPE1 field10
    .field[20] public static TYPE1 field11
}

.class explicit SubOverlapped extends Overlapped
{
    .field[16] public TYPE1 field6
}

.method public static int32 Main() cil managed
{
	.entrypoint
	.maxstack 2
	.locals init (
		TYPE2 V_1
	)
	ldloc.0
	OPCODE // VALIDITY.
	pop
	ldc.i4.0
	ret
}
//EOF