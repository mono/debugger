namespace BitSet
{
public sealed class BitSet : System.IComparable
{
/*
 * Sorted array of bit-block offsets.
 */
int[] offs;

/*
 * Array of bit-blocks; each holding BITS bits.
 */
ulong[] bits;
/*
 * Number of blocks currently in use.
 */
int inuse;

/*
 * log base 2 of BITS, for the identity: x/BITS == x >> LG_BITS
 */
const int LG_BITS = 6;
/*
 * Number of bits in a block.
 */
const int BITS = 1<<LG_BITS;
/*
 * BITS-1, using the identity: x % BITS == x & (BITS-1)
 */
const int BITS_M1 = BITS-1;

/*
 * Creates an empty set.
 */
public BitSet()
  {
  bits = new ulong[4];
  offs = new int [4];
  inuse = 0;
  }

/*
 * Creates an empty set with the specified size.
 */
public BitSet(int nbits) : this() {}

/*
 * Create a bitset and initialize with a specified value
 */
public BitSet(int nbits, bool val) : this()
  {
  for (int i = 0; i < nbits; i++)
    Set(i, val);
  }

/*
 * Creates a new set from existing set
 */
public BitSet(BitSet set)
  {
  bits = new ulong[set.bits.Length];
  offs = new int [set.offs.Length];
  System.Array.Copy(set.bits, 0, bits, 0, set.bits.Length);
  System.Array.Copy(set.offs, 0, offs, 0, set.offs.Length);
  inuse = set.inuse;
  }

void new_block(int i, int b)
  {
  if (inuse == bits.Length)
    { // resize
    ulong[] nbits = new ulong[inuse+4];
    int [] noffs = new int [inuse+4];
    System.Array.Copy(bits, 0, nbits, 0, inuse);
    System.Array.Copy(offs, 0, noffs, 0, inuse);
    bits = nbits;
    offs = noffs;
    }
  insert_block(i, b);
  }

void insert_block(int i, int b)
  {
  System.Array.Copy(bits, i, bits, i+1, inuse-i);
  System.Array.Copy(offs, i, offs, i+1, inuse-i);
  offs[i] = b;
  bits[i] = 0;
  inuse++;
  }

int BinarySearch(int []x, int i, int m, int val)
  {
  int l = i;
  int r = m;
  while (l < r)
    {
    int p = (l+r)/2;
    if (val < x[p])
      r = p;
    else if (val > x[p])
      l = p+1;
    else
      return p;
    }
  return -l;			// this is the next elem
  }

/*
 * Sets a bit.
 */
public void Set(int bit, bool val)
  {
  int b = bit >> LG_BITS;
  int i = BinarySearch(offs, 0, inuse, b);
  if (i < 0)
    {
    i = -i;				// bitwise complement
    new_block(i, b);
    }
  else if (i >= inuse || offs[i] != b)
    new_block(i, b);
  if (val)
    bits[i] |= (1UL << (bit & BITS_M1) );
  else
    bits[i] &= ~(1UL << (bit & BITS_M1) );
  }

/*
 * Clears all bits.
 */
public void ClearAll()
  {
  inuse = 0;
  }

/*
 * Gets a bit.
 */
public bool Get(int bit)
  {
  int b = bit >> LG_BITS;
  int i = BinarySearch(offs, 0, inuse, b);
  if (i < 0)
    return false;
  if (i >= inuse || offs[i] != b)
    return false;
  return 0 != (bits[i] & (1UL << (bit & BITS_M1)));
  }

delegate ulong BinOp(ulong x, ulong y);

/*
 * Logically ANDs this bit set with the specified set of bits.
 */
public void And(BitSet set)
  {
  binop(this, set, new BinOp(BitSet.AND));
  }

/*
 * Logically ORs this bit set with the specified set of bits.
 */
public void Or(BitSet set)
  {
  binop(this, set, new BinOp(BitSet.OR));
  }

/*
 * Logically XORs this bit set with the specified set of bits.
 */
public void Xor(BitSet set)
  {
  binop(this, set, new BinOp(BitSet.XOR));
  }

static public ulong AND(ulong x, ulong y) { return x & y; }
static public ulong OR(ulong x, ulong y)  { return x | y; }
static public ulong XOR(ulong x, ulong y) { return x ^ y; }

void binop(BitSet a, BitSet b, BinOp op)
  {
  int n_sum = a.inuse + b.inuse;
  ulong[] n_bits = new ulong[n_sum];
  int[] n_offs = new int [n_sum];
  int n_len = 0;
  int a_len = a.bits.Length;
  int b_len = b.bits.Length;

  for (int i = 0, j=0; i < a_len || j < b_len;)
    {
    ulong nb; int no;
    if (i < a_len && ((j >= b_len) || (a.offs[i] < b.offs[j])))
      {
      nb = op(a.bits[i], 0);	// invoke delegate
      no = a.offs[i];
      i++;
      }
    else if (j < b_len && ((i >= a_len) || (a.offs[i] > b.offs[j])))
      {
      nb = op(0, b.bits[j]);	// invoke delegate
      no = b.offs[j];
      j++;
      }
    else
      { // equal keys; merge.
      nb = op(a.bits[i], b.bits[j]); // invoke delegate
      no = a.offs[i];
      i++;
      j++;
      }
    if (nb != 0)
      {
      n_bits[n_len] = nb;
      n_offs[n_len] = no;
      n_len++;
      }
    }

  if (n_len > 0)
    {
    a.bits = new ulong[n_len];
    a.offs = new int[n_len];
    a.inuse = n_len;
    System.Array.Copy(n_bits, 0, a.bits, 0, n_len);
    System.Array.Copy(n_offs, 0, a.offs, 0, n_len);
    }
  else
    {
    bits = new ulong[4];
    offs = new int [4];
    a.inuse = 0;
    }
  }

/*
 * Gets the hashcode.
 */
const uint prime = 1299827;
public override int GetHashCode()
  {
  ulong h = prime;
  for (int i=0; i < inuse; i++)
    h ^= bits[i] * (ulong) offs[i];
  return (int)((h >> 32) ^ h);
  }

public int Count
  {
  get { return GetLength(); }
  }

/*
 * Calculates and returns the set's size
 */
public int GetLength()
  {
  if (inuse == 0)
    return 0;
  return ((1+offs[inuse-1]) << LG_BITS);
  }

/*
 * Compares this object against the specified object.
 * obj - the object to commpare with
 * returns true if the objects are the same; false otherwise.
 */
public override bool Equals(object obj)
  {
  if (obj == null)
    return false;
  if (!(obj is BitSet))
    return false;
  return Equals(this, (BitSet) obj); 
  }

/*
 * Compares two BitSets for equality.
 * return true if the objects are the same; false otherwise.
 */
public static bool Equals(BitSet a, BitSet b)
  {
  for (int i=0, j=0; i < a.inuse || j < b.inuse; )
    {
    if (i < a.inuse && (j >= b.inuse || a.offs[i] < b.offs[j]))
      {
      if (a.bits[i++] != 0)
	return false;
      }
    else if (j < b.inuse && (i >= a.inuse || a.offs[i] > b.offs[j]))
      {
      if (b.bits[j++] != 0)
	return false;
      }
    else
      { // equal keys
      if (a.bits[i++] != b.bits[j++])
	return false;
      }
    }
  return true;
  }

/*
 * Provides a compare function for a BitSort, for use with Sort or
 * binarysearch.
 */
public int CompareTo(object o)
  {
  if (!(o is BitSet))
    throw new System.ApplicationException("Argument must be a BitSet");
  BitSet a = (BitSet) o;

  if (inuse < a.inuse)
    return -1;
  if (inuse > a.inuse)
    return 1;

  for (int i=0, j=0; i < inuse || j < a.inuse; )
    {
    if (i < inuse && (j >= a.inuse || offs[i] < a.offs[j]))
      {
      if (bits[i++] != 0)
	return -1;
      }
    else if (j < a.inuse && (i >= inuse || offs[i] > a.offs[j]))
      {
      if (a.bits[j++] != 0)
	return 1;
      }
    else
      { // equal keys
      long val =  ((long) a.bits[j++]) - ((long) bits[i++]);
      if (val < 0)
	return -1;
      else if (val > 0)
	return 1;
      }
    }
  return 0;
  }


class BitSetEnum : System.Collections.IEnumerator
  {
  int idx = -1;
  int bit = BITS;
  BitSet p;

  public BitSetEnum(BitSet x)
    {
    p = x;
    }

  public void Reset()
    {
    idx = -1;
    bit = BITS;
    }

  public bool MoveNext()
    {
    advance();
    return (idx < p.inuse);
    }

  public object Current
    {
    get
      {
      if (idx < 0)
	return null;
      int r = bit + (p.offs[idx] << LG_BITS);
      return r;
      }
    }

  private void advance()
    {
    int max = BITS;
    if (idx < 0)
      {
      idx++;
      bit=-1;
      }
    while (idx < p.inuse)
      {
      ulong val = p.bits[idx];
      while (++bit < max)
	{
	ulong mask = (1UL<<bit);
	ulong result = val & mask;
	if (result != 0)
	  return;
	}
      idx++;
      bit=-1;
      }
    }
  }

/*
 * Return an IEnumerator which represents set bit indices in this BitSet.
 */
public System.Collections.IEnumerator GetEnumerator()
  {
  return new BitSetEnum(this);
  }

/*
 * Converts the BitSet to a String.
 */
public override string ToString()
  {
  System.Text.StringBuilder sb = new System.Text.StringBuilder();
  sb.Append('{');
  foreach (int bit in this)
    {
    if (sb.Length > 1)
      sb.Append(", ");
    sb.Append(bit);
    }
  sb.Append('}');
  return sb.ToString();
  }

}
}
