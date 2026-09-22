namespace FFPorter.Core.Common.Python;

public static class PyListSort
{
    public static int[] SortedRange(int count, Func<int, int, bool> less)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(less);
        int[] keys = new int[count];
        for (int i = 0; i < count; i++)
            keys[i] = i;
        new MergeState(keys, less).Sort();
        return keys;
    }

    public static void Sort(int[] items, Func<int, int, bool> less)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(less);
        new MergeState(items, less).Sort();
    }

    private sealed class MergeState(int[] list, Func<int, int, bool> less)
    {
        private const int MinGallop = 7;
        private const int MaxMergePending = 85;

        private readonly int[] _a = list;
        private readonly Func<int, int, bool> _lt = less;
        private readonly Run[] _pending = new Run[MaxMergePending];
        private int[] _temp = new int[256];
        private int _n;
        private int _minGallop = MinGallop;

        private struct Run
        {
            public int Base;
            public int Len;
            public int Power;
        }

        public void Sort()
        {
            int remaining = _a.Length;
            if (remaining < 2)
                return;
            int lo = 0;
            int minRun = ComputeMinRun(remaining);
            do
            {
                int n = CountRun(lo, lo + remaining, out bool descending);
                if (descending)
                    Array.Reverse(_a, lo, n);
                if (n < minRun)
                {
                    int force = remaining <= minRun ? remaining : minRun;
                    BinarySort(lo, lo + force, lo + n);
                    n = force;
                }
                FoundNewRun(n);
                _pending[_n++] = new Run { Base = lo, Len = n };
                lo += n;
                remaining -= n;
            } while (remaining != 0);
            MergeForceCollapse();
        }

        private static int ComputeMinRun(int n)
        {
            int r = 0;
            while (n >= 64)
            {
                r |= n & 1;
                n >>= 1;
            }
            return n + r;
        }

        private int CountRun(int lo, int hi, out bool descending)
        {
            descending = false;
            lo++;
            if (lo == hi)
                return 1;
            int n = 2;
            if (_lt(_a[lo], _a[lo - 1]))
            {
                descending = true;
                for (lo++; lo < hi; lo++, n++)
                {
                    if (!_lt(_a[lo], _a[lo - 1]))
                        break;
                }
            }
            else
            {
                for (lo++; lo < hi; lo++, n++)
                {
                    if (_lt(_a[lo], _a[lo - 1]))
                        break;
                }
            }
            return n;
        }

        private void BinarySort(int lo, int hi, int start)
        {
            if (lo == start)
                start++;
            for (; start < hi; start++)
            {
                int l = lo;
                int r = start;
                int pivot = _a[r];
                do
                {
                    int p = l + ((r - l) >> 1);
                    if (_lt(pivot, _a[p]))
                        r = p;
                    else
                        l = p + 1;
                } while (l < r);
                for (int p = start; p > l; p--)
                    _a[p] = _a[p - 1];
                _a[l] = pivot;
            }
        }

        private static int PowerLoop(long s1, long n1, long n2, long n)
        {
            int result = 0;
            long a = 2 * s1 + n1;
            long b = a + n1 + n2;
            while (true)
            {
                ++result;
                if (a >= n)
                {
                    a -= n;
                    b -= n;
                }
                else if (b >= n)
                {
                    break;
                }
                a <<= 1;
                b <<= 1;
            }
            return result;
        }

        private void FoundNewRun(int n2)
        {
            if (_n == 0)
                return;
            int s1 = _pending[_n - 1].Base;
            int n1 = _pending[_n - 1].Len;
            int power = PowerLoop(s1, n1, n2, _a.Length);
            while (_n > 1 && _pending[_n - 2].Power > power)
                MergeAt(_n - 2);
            _pending[_n - 1].Power = power;
        }

        private void MergeForceCollapse()
        {
            while (_n > 1)
            {
                int n = _n - 2;
                if (n > 0 && _pending[n - 1].Len < _pending[n + 1].Len)
                    --n;
                MergeAt(n);
            }
        }

        private void MergeAt(int i)
        {
            int pa = _pending[i].Base;
            int na = _pending[i].Len;
            int pb = _pending[i + 1].Base;
            int nb = _pending[i + 1].Len;

            _pending[i].Len = na + nb;
            if (i == _n - 3)
                _pending[i + 1] = _pending[i + 2];
            --_n;

            int k = GallopRight(_a[pb], _a, pa, na, 0);
            pa += k;
            na -= k;
            if (na == 0)
                return;

            nb = GallopLeft(_a[pa + na - 1], _a, pb, nb, nb - 1);
            if (nb <= 0)
                return;

            if (na <= nb)
                MergeLo(pa, na, pb, nb);
            else
                MergeHi(pa, na, pb, nb);
        }

        private int GallopLeft(int key, int[] array, int start, int n, int hint)
        {
            int at = start + hint;
            long lastOfs = 0;
            long ofs = 1;
            if (_lt(array[at], key))
            {
                long maxOfs = n - hint;
                while (ofs < maxOfs)
                {
                    if (_lt(array[at + (int)ofs], key))
                    {
                        lastOfs = ofs;
                        ofs = (ofs << 1) + 1;
                    }
                    else
                    {
                        break;
                    }
                }
                if (ofs > maxOfs)
                    ofs = maxOfs;
                lastOfs += hint;
                ofs += hint;
            }
            else
            {
                long maxOfs = hint + 1;
                while (ofs < maxOfs)
                {
                    if (_lt(array[at - (int)ofs], key))
                        break;
                    lastOfs = ofs;
                    ofs = (ofs << 1) + 1;
                }
                if (ofs > maxOfs)
                    ofs = maxOfs;
                long k = lastOfs;
                lastOfs = hint - ofs;
                ofs = hint - k;
            }

            ++lastOfs;
            while (lastOfs < ofs)
            {
                long m = lastOfs + ((ofs - lastOfs) >> 1);
                if (_lt(array[start + (int)m], key))
                    lastOfs = m + 1;
                else
                    ofs = m;
            }
            return (int)ofs;
        }

        private int GallopRight(int key, int[] array, int start, int n, int hint)
        {
            int at = start + hint;
            long lastOfs = 0;
            long ofs = 1;
            if (_lt(key, array[at]))
            {
                long maxOfs = hint + 1;
                while (ofs < maxOfs)
                {
                    if (_lt(key, array[at - (int)ofs]))
                    {
                        lastOfs = ofs;
                        ofs = (ofs << 1) + 1;
                    }
                    else
                    {
                        break;
                    }
                }
                if (ofs > maxOfs)
                    ofs = maxOfs;
                long k = lastOfs;
                lastOfs = hint - ofs;
                ofs = hint - k;
            }
            else
            {
                long maxOfs = n - hint;
                while (ofs < maxOfs)
                {
                    if (_lt(key, array[at + (int)ofs]))
                        break;
                    lastOfs = ofs;
                    ofs = (ofs << 1) + 1;
                }
                if (ofs > maxOfs)
                    ofs = maxOfs;
                lastOfs += hint;
                ofs += hint;
            }

            ++lastOfs;
            while (lastOfs < ofs)
            {
                long m = lastOfs + ((ofs - lastOfs) >> 1);
                if (_lt(key, array[start + (int)m]))
                    ofs = m;
                else
                    lastOfs = m + 1;
            }
            return (int)ofs;
        }

        private void EnsureTemp(int need)
        {
            if (need > _temp.Length)
                _temp = new int[need];
        }

        private void MergeLo(int pa, int na, int pb, int nb)
        {
            EnsureTemp(na);
            Array.Copy(_a, pa, _temp, 0, na);
            int dest = pa;
            int ta = 0;
            int b = pb;

            _a[dest++] = _a[b++];
            --nb;
            if (nb == 0)
                goto Succeed;
            if (na == 1)
                goto CopyB;

            int minGallop = _minGallop;
            while (true)
            {
                int acount = 0;
                int bcount = 0;

                while (true)
                {
                    if (_lt(_a[b], _temp[ta]))
                    {
                        _a[dest++] = _a[b++];
                        ++bcount;
                        acount = 0;
                        --nb;
                        if (nb == 0)
                            goto Succeed;
                        if (bcount >= minGallop)
                            break;
                    }
                    else
                    {
                        _a[dest++] = _temp[ta++];
                        ++acount;
                        bcount = 0;
                        --na;
                        if (na == 1)
                            goto CopyB;
                        if (acount >= minGallop)
                            break;
                    }
                }

                ++minGallop;
                do
                {
                    minGallop -= minGallop > 1 ? 1 : 0;
                    _minGallop = minGallop;
                    int k = GallopRight(_a[b], _temp, ta, na, 0);
                    acount = k;
                    if (k != 0)
                    {
                        Array.Copy(_temp, ta, _a, dest, k);
                        dest += k;
                        ta += k;
                        na -= k;
                        if (na == 1)
                            goto CopyB;
                        if (na == 0)
                            goto Succeed;
                    }
                    _a[dest++] = _a[b++];
                    --nb;
                    if (nb == 0)
                        goto Succeed;

                    k = GallopLeft(_temp[ta], _a, b, nb, 0);
                    bcount = k;
                    if (k != 0)
                    {
                        Array.Copy(_a, b, _a, dest, k);
                        dest += k;
                        b += k;
                        nb -= k;
                        if (nb == 0)
                            goto Succeed;
                    }
                    _a[dest++] = _temp[ta++];
                    --na;
                    if (na == 1)
                        goto CopyB;
                } while (acount >= MinGallop || bcount >= MinGallop);
                ++minGallop;
                _minGallop = minGallop;
            }

        Succeed:
            if (na != 0)
                Array.Copy(_temp, ta, _a, dest, na);
            return;

        CopyB:
            Array.Copy(_a, b, _a, dest, nb);
            _a[dest + nb] = _temp[ta];
        }

        private void MergeHi(int pa, int na, int pb, int nb)
        {
            EnsureTemp(nb);
            int dest = pb + nb - 1;
            Array.Copy(_a, pb, _temp, 0, nb);
            int baseA = pa;
            int tb = nb - 1;
            int a = pa + na - 1;

            _a[dest--] = _a[a--];
            --na;
            if (na == 0)
                goto Succeed;
            if (nb == 1)
                goto CopyA;

            int minGallop = _minGallop;
            while (true)
            {
                int acount = 0;
                int bcount = 0;

                while (true)
                {
                    if (_lt(_temp[tb], _a[a]))
                    {
                        _a[dest--] = _a[a--];
                        ++acount;
                        bcount = 0;
                        --na;
                        if (na == 0)
                            goto Succeed;
                        if (acount >= minGallop)
                            break;
                    }
                    else
                    {
                        _a[dest--] = _temp[tb--];
                        ++bcount;
                        acount = 0;
                        --nb;
                        if (nb == 1)
                            goto CopyA;
                        if (bcount >= minGallop)
                            break;
                    }
                }

                ++minGallop;
                do
                {
                    minGallop -= minGallop > 1 ? 1 : 0;
                    _minGallop = minGallop;
                    int k = GallopRight(_temp[tb], _a, baseA, na, na - 1);
                    k = na - k;
                    acount = k;
                    if (k != 0)
                    {
                        dest -= k;
                        a -= k;
                        Array.Copy(_a, a + 1, _a, dest + 1, k);
                        na -= k;
                        if (na == 0)
                            goto Succeed;
                    }
                    _a[dest--] = _temp[tb--];
                    --nb;
                    if (nb == 1)
                        goto CopyA;

                    k = GallopLeft(_a[a], _temp, 0, nb, nb - 1);
                    k = nb - k;
                    bcount = k;
                    if (k != 0)
                    {
                        dest -= k;
                        tb -= k;
                        Array.Copy(_temp, tb + 1, _a, dest + 1, k);
                        nb -= k;
                        if (nb == 1)
                            goto CopyA;
                        if (nb == 0)
                            goto Succeed;
                    }
                    _a[dest--] = _a[a--];
                    --na;
                    if (na == 0)
                        goto Succeed;
                } while (acount >= MinGallop || bcount >= MinGallop);
                ++minGallop;
                _minGallop = minGallop;
            }

        Succeed:
            if (nb != 0)
                Array.Copy(_temp, 0, _a, dest - (nb - 1), nb);
            return;

        CopyA:
            Array.Copy(_a, a + 1 - na, _a, dest + 1 - na, na);
            dest -= na;
            _a[dest] = _temp[tb];
        }
    }
}
