using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RpfStudio
{
    /// <summary>Erzeugt die NG-Verschlüsselungstabellen durch GF(2)-Inversion der Entschlüsselungsrunden.</summary>
    public static class NgEncrypt
    {
        // Byte-Positionen je Ausgabegruppe g (0..3). RoundA (0,1,16) und RoundB (2..15) mischen unterschiedlich.
        static readonly int[][] GA = { new[]{0,1,2,3}, new[]{4,5,6,7}, new[]{8,9,10,11}, new[]{12,13,14,15} };
        static readonly int[][] GB = { new[]{0,7,10,13}, new[]{1,4,11,14}, new[]{2,5,8,15}, new[]{3,6,9,12} };
        public static int[][] Groups(int round) => (round >= 2 && round <= 15) ? GB : GA;

        static byte[] Round(byte[] d, uint[] key, uint[][] t, int[][] pos, bool xorKeyFirst)
        {
            var b = (byte[])d.Clone();
            if (xorKeyFirst)
            {
                var kb = new byte[16];
                Buffer.BlockCopy(key, 0, kb, 0, 16);
                for (int i = 0; i < 16; i++) b[i] ^= kb[i];
            }
            var r = new byte[16];
            for (int g = 0; g < 4; g++)
            {
                var p = pos[g];
                uint x = t[p[0]][b[p[0]]] ^ t[p[1]][b[p[1]]] ^ t[p[2]][b[p[2]]] ^ t[p[3]][b[p[3]]];
                if (!xorKeyFirst) x ^= key[g];
                BitConverter.GetBytes(x).CopyTo(r, g * 4);
            }
            return r;
        }

        sealed class Row
        {
            readonly ulong[] A = new ulong[16];
            public bool B;
            public bool GetA(int i) => ((A[i >> 6] >> (i & 63)) & 1) != 0;
            public void SetA(int i) => A[i >> 6] |= 1UL << (i & 63);
            public static Row Xor(Row a, Row b)
            {
                var n = new Row();
                for (int i = 0; i < 16; i++) n.A[i] = a.A[i] ^ b.A[i];
                n.B = a.B ^ b.B;
                return n;
            }
            public bool[] ToArray()
            {
                var r = new bool[1024];
                for (int i = 0; i < 1024; i++) r[i] = GetA(i);
                return r;
            }
        }

        static bool[] SolveBits(uint[][] t, int[][] pos, int g, int outByte, int outBit, bool special)
        {
            var pivots = new List<Row>(1024);
            var rnd = new Random(unchecked(0x5EED + g * 7919 + outByte * 131 + outBit));
            var first = new Row(); first.SetA(0); first.B = true; pivots.Add(first);

            var buf = new byte[16];
            var noKey = new uint[4];

            for (int pi = 1; pi < 1024; pi++)
            {
                while (true)
                {
                    rnd.NextBytes(buf);
                    var dec = Round(buf, noKey, t, pos, false);
                    var row = new Row();
                    for (int k = 0; k < 4; k++) row.SetA(k * 256 + dec[pos[g][k]]);
                    if ((buf[outByte] & (1 << outBit)) != 0) row.B = true;

                    if (special && pi == 0x2ff) { row = new Row(); row.SetA(0x2ff); row.B = true; }
                    if (special && pi == 0x3ff) { row = new Row(); row.SetA(0x3ff); row.B = true; }

                    for (int k = 0; k < pi; k++) if (row.GetA(k)) row = Row.Xor(row, pivots[k]);
                    if (row.GetA(pi)) { pivots.Add(row); break; }
                }
            }

            var res = new bool[1024];
            for (int j = 1023; j >= 0; j--)
            {
                bool v = pivots[j].B;
                res[j] = v;
                for (int k = 0; k < j; k++)
                    if (pivots[k].GetA(j)) pivots[k].B ^= v;
            }
            return res;
        }

        /// <summary>Berechnet die Verschlüsselungstabelle einer Runde (16 x 256).</summary>
        public static uint[][] BuildRound(uint[][] dec, int round)
        {
            var pos = Groups(round);
            var enc = new uint[16][];
            for (int i = 0; i < 16; i++) enc[i] = new uint[256];

            Parallel.For(0, 4, g =>
            {
                for (int ob = 0; ob < 4; ob++)
                {
                    int outByte = g * 4 + ob;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        var bits = SolveBits(dec, pos, g, outByte, bit, round < 2 || round > 15);
                        uint mask = 1u << (ob * 8 + bit);
                        for (int k = 0; k < 4; k++)
                        {
                            var tab = enc[pos[g][k]];
                            for (int i = 0; i < 256; i++)
                                if (bits[k * 256 + i]) tab[i] |= mask;
                        }
                    }
                }
            });
            return enc;
        }

        public static uint[][][] BuildAll(uint[][][] dec, Action<string> log = null)
        {
            var enc = new uint[17][][];
            for (int r = 0; r < 17; r++)
            {
                var t0 = DateTime.Now;
                enc[r] = BuildRound(dec[r], r);
                log?.Invoke($"  Runde {r + 1}/17 fertig ({DateTime.Now - t0:mm\\:ss\\.fff})");
            }
            return enc;
        }

        public static byte[] EncryptBlock(byte[] data, byte[] key, uint[][][] enc)
        {
            var sk = new uint[17][];
            for (int i = 0; i < 17; i++)
            {
                sk[i] = new uint[4];
                for (int j = 0; j < 4; j++) sk[i][j] = BitConverter.ToUInt32(key, (i * 4 + j) * 4);
            }
            var b = (byte[])data.Clone();
            int i16 = 16;
            b = Round(b, sk[16], enc[16], GA, true);
            for (int r = 15; r >= 2; r--) b = Round(b, sk[r], enc[r], GB, true);
            b = Round(b, sk[1], enc[1], GA, true);
            b = Round(b, sk[0], enc[0], GA, true);
            return b;
        }

        public static byte[] Encrypt(byte[] data, byte[] key, uint[][][] enc)
        {
            var outp = new byte[data.Length];
            int blocks = data.Length / 16;
            var blk = new byte[16];
            for (int i = 0; i < blocks; i++)
            {
                Buffer.BlockCopy(data, i * 16, blk, 0, 16);
                var e = EncryptBlock(blk, key, enc);
                Buffer.BlockCopy(e, 0, outp, i * 16, 16);
            }
            int left = data.Length % 16;
            if (left != 0) Buffer.BlockCopy(data, data.Length - left, outp, data.Length - left, left);
            return outp;
        }

        static uint[] SubKey(byte[] key, int r)
        {
            var s = new uint[4];
            for (int j = 0; j < 4; j++) s[j] = BitConverter.ToUInt32(key, (r * 4 + j) * 4);
            return s;
        }

        public static byte[] EncRound(byte[] d, byte[] key, uint[][] t, int round)
            => Round(d, SubKey(key, round), t, Groups(round), true);

        public static byte[] DecRound(byte[] d, byte[] key, uint[][] t, int round)
            => Round(d, SubKey(key, round), t, Groups(round), false);

        public static void Save(string path, uint[][][] enc)        {
            using var bw = new BinaryWriter(File.Create(path));
            for (int r = 0; r < 17; r++)
                for (int t = 0; t < 16; t++)
                    for (int i = 0; i < 256; i++) bw.Write(enc[r][t][i]);
        }

        public static uint[][][] Load(string path)
        {
            using var br = new BinaryReader(File.OpenRead(path));
            var enc = new uint[17][][];
            for (int r = 0; r < 17; r++)
            {
                enc[r] = new uint[16][];
                for (int t = 0; t < 16; t++) { enc[r][t] = new uint[256]; for (int i = 0; i < 256; i++) enc[r][t][i] = br.ReadUInt32(); }
            }
            return enc;
        }
    }
}
