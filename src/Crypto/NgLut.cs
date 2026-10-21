using System;
using System.Threading.Tasks;

namespace RpfStudio
{
    /// <summary>RoundB-Verschlüsselung über Lookup-Tabellen (Port von CodeWalkers BuildLUTs2).</summary>
    public sealed class NgLut
    {
        public byte[][] LUT0 = new byte[256][];
        public byte[][] LUT1 = new byte[256][];
        public byte[] Indices = new byte[65536];

        public byte LookUp(uint v)
        {
            uint h16 = (v & 0xFFFF0000) >> 16;
            uint l8 = (v & 0x0000FF00) >> 8;
            uint l0 = (v & 0x000000FF);
            return LUT0[LUT1[Indices[h16]][l8]][l0];
        }

        public static NgLut[] Build(uint[][] t)
        {
            var temp = new byte[16][];
            var tempLUTS = new byte[16][];
            for (int i = 0; i < 16; i++) { temp[i] = new byte[65536]; tempLUTS[i] = new byte[16777216]; }

            var t0 = t[0]; var t1 = t[1]; var t2 = t[2]; var t3 = t[3];
            var t4 = t[4]; var t5 = t[5]; var t6 = t[6]; var t7 = t[7];
            var t8 = t[8]; var t9 = t[9]; var t10 = t[10]; var t11 = t[11];
            var t12 = t[12]; var t13 = t[13]; var t14 = t[14]; var t15 = t[15];

            Parallel.For(0, 0x100, k1 =>
            {
                for (long k2 = 0; k2 < 0x1000000; k2++)
                {
                    long i = k1 * 0x1000000L + k2;
                    byte b0 = (byte)((i >> 0) & 0xFF);
                    byte b1 = (byte)((i >> 8) & 0xFF);
                    byte b2 = (byte)((i >> 16) & 0xFF);
                    byte b3 = (byte)((i >> 24) & 0xFF);

                    uint x1 = t0[b0] ^ t7[b1] ^ t10[b2] ^ t13[b3];
                    uint x2 = t1[b0] ^ t4[b1] ^ t11[b2] ^ t14[b3];
                    uint x3 = t2[b0] ^ t5[b1] ^ t8[b2] ^ t15[b3];
                    uint x4 = t3[b0] ^ t6[b1] ^ t9[b2] ^ t12[b3];

                    if (x1 < 65536) { temp[0][x1] = b0; temp[7][x1] = b1; temp[10][x1] = b2; temp[13][x1] = b3; }
                    if (x2 < 65536) { temp[1][x2] = b0; temp[4][x2] = b1; temp[11][x2] = b2; temp[14][x2] = b3; }
                    if (x3 < 65536) { temp[2][x3] = b0; temp[5][x3] = b1; temp[8][x3] = b2; temp[15][x3] = b3; }
                    if (x4 < 65536) { temp[3][x4] = b0; temp[6][x4] = b1; temp[9][x4] = b2; temp[12][x4] = b3; }

                    if ((x1 & 0xFF) == 0) { tempLUTS[0][x1 >> 8] = b0; tempLUTS[7][x1 >> 8] = b1; tempLUTS[10][x1 >> 8] = b2; tempLUTS[13][x1 >> 8] = b3; }
                    if ((x2 & 0xFF) == 0) { tempLUTS[1][x2 >> 8] = b0; tempLUTS[4][x2 >> 8] = b1; tempLUTS[11][x2 >> 8] = b2; tempLUTS[14][x2 >> 8] = b3; }
                    if ((x3 & 0xFF) == 0) { tempLUTS[2][x3 >> 8] = b0; tempLUTS[5][x3 >> 8] = b1; tempLUTS[8][x3 >> 8] = b2; tempLUTS[15][x3 >> 8] = b3; }
                    if ((x4 & 0xFF) == 0) { tempLUTS[3][x4 >> 8] = b0; tempLUTS[6][x4 >> 8] = b1; tempLUTS[9][x4 >> 8] = b2; tempLUTS[12][x4 >> 8] = b3; }
                }
            });

            var res = new NgLut[16];
            for (int i = 0; i < 16; i++)
            {
                var lut = new NgLut();
                for (int block = 0; block < 256; block++)
                {
                    var xl = new byte[256];
                    Buffer.BlockCopy(temp[i], block * 256, xl, 0, 256);
                    lut.LUT0[xl[0]] = xl;
                }
                for (int block = 0; block < 256; block++)
                {
                    var xl = new byte[256];
                    Buffer.BlockCopy(tempLUTS[i], block * 256, xl, 0, 256);
                    lut.LUT1[xl[0]] = xl;
                }
                for (int block = 0; block < 65536; block++) lut.Indices[block] = tempLUTS[i][block * 256];
                res[i] = lut;
            }
            return res;
        }

        public static void SaveAll(string path, NgLut[][] luts)
        {
            using var bw = new System.IO.BinaryWriter(System.IO.File.Create(path));
            for (int r = 0; r < 17; r++)
            {
                if (luts[r] == null) continue;
                for (int i = 0; i < 16; i++)
                {
                    var l = luts[r][i];
                    for (int a = 0; a < 256; a++) bw.Write(l.LUT0[a]);
                    for (int a = 0; a < 256; a++) bw.Write(l.LUT1[a]);
                    bw.Write(l.Indices);
                }
            }
        }

        public static NgLut[][] LoadAll(string path)
        {
            using var br = new System.IO.BinaryReader(System.IO.File.OpenRead(path));
            var res = new NgLut[17][];
            for (int r = 0; r < 17; r++)
            {
                if (r == 0 || r == 1 || r == 16) continue;
                res[r] = new NgLut[16];
                for (int i = 0; i < 16; i++)
                {
                    var l = new NgLut();
                    for (int a = 0; a < 256; a++) l.LUT0[a] = br.ReadBytes(256);
                    for (int a = 0; a < 256; a++) l.LUT1[a] = br.ReadBytes(256);
                    l.Indices = br.ReadBytes(65536);
                    res[r][i] = l;
                }
            }
            return res;
        }

        public static NgLut[][] BuildAll(uint[][][] dec, Action<string> log = null)
        {
            var res = new NgLut[17][];
            for (int r = 0; r < 17; r++)
            {
                if (r == 0 || r == 1 || r == 16) continue;
                var t0 = DateTime.Now;
                res[r] = Build(dec[r]);
                log?.Invoke($"  LUT Runde {r}/16 fertig ({DateTime.Now - t0:ss\\.ff}s)");
            }
            return res;
        }

        static byte[] RoundAny(byte[] d, byte[] key272, int r, uint[][][] aEnc, NgLut[][] bLut)
        {
            if (r == 0 || r == 1 || r == 16) return NgEncrypt.EncRound(d, key272, aEnc[r], r);
            var rk = new byte[16];
            Buffer.BlockCopy(key272, r * 16, rk, 0, 16);
            return EncryptRoundB(d, rk, bLut[r]);
        }

        public static byte[] EncryptAll(byte[] data, byte[] key272, uint[][][] aEnc, NgLut[][] bLut)
        {
            var outp = new byte[data.Length];
            int blocks = data.Length / 16;
            var blk = new byte[16];
            for (int i = 0; i < blocks; i++)
            {
                Buffer.BlockCopy(data, i * 16, blk, 0, 16);
                var x = RoundAny(blk, key272, 16, aEnc, bLut);
                for (int r = 15; r >= 2; r--) x = RoundAny(x, key272, r, aEnc, bLut);
                x = RoundAny(x, key272, 1, aEnc, bLut);
                x = RoundAny(x, key272, 0, aEnc, bLut);
                Buffer.BlockCopy(x, 0, outp, i * 16, 16);
            }
            int left = data.Length % 16;
            if (left != 0) Buffer.BlockCopy(data, data.Length - left, outp, data.Length - left, left);
            return outp;
        }

        public static byte[] EncryptRoundB(byte[] data, byte[] keyBytes, NgLut[] lut)
        {
            var b = (byte[])data.Clone();
            for (int i = 0; i < 16; i++) b[i] ^= keyBytes[i];
            uint U(int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

            // Tabelle -> zugehoerige Gruppe (x1=0..3, x2=4..7, x3=8..11, x4=12..15)
            int[] groupOf = { 0, 4, 8, 12, 4, 8, 12, 0, 8, 12, 0, 4, 12, 0, 4, 8 };
            var r = new byte[16];
            for (int i = 0; i < 16; i++) r[i] = lut[i].LookUp(U(groupOf[i]));
            return r;
        }
    }
}
