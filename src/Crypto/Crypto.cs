using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace RpfStudio
{
    public static class Crypto
    {
        // SHA1 of the 32-byte AES key, taken from CodeWalker's GTA5Keys
        static readonly byte[] AesKeyHash =
        {
            0xA0,0x79,0x61,0x28,0xA7,0x75,0x72,0x0A,0xC2,0x04,0xD9,0x81,0x9F,0x68,0xC1,0x72,0xE3,0x95,0x2C,0x6D
        };

        public static byte[] FindAesKey(string exePath)
        {
            var exe = File.ReadAllBytes(exePath);
            var hash = new byte[20];
            for (int off = 0; off + 32 <= exe.Length; off += 8)
            {
                SHA1.HashData(exe.AsSpan(off, 32), hash);
                bool ok = true;
                for (int i = 0; i < 20; i++) { if (hash[i] != AesKeyHash[i]) { ok = false; break; } }
                if (ok)
                {
                    var key = new byte[32];
                    Buffer.BlockCopy(exe, off, key, 0, 32);
                    return key;
                }
            }
            return null;
        }

        public static uint Jenk(byte[] data)
        {
            uint h = 0;
            foreach (var b in data)
            {
                h += b;
                h += h << 10;
                h ^= h >> 6;
            }
            h += h << 3;
            h ^= h >> 11;
            h += h << 15;
            return h;
        }

        public static byte[] AesDecrypt(byte[] data, byte[] key)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256; aes.Key = key; aes.Mode = CipherMode.ECB; aes.Padding = PaddingMode.None;
            var buf = (byte[])data.Clone();
            int len = buf.Length - buf.Length % 16;
            if (len > 0) aes.CreateDecryptor().TransformBlock(buf, 0, len, buf, 0);
            return buf;
        }

        public static byte[] Inflate(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var outp = new MemoryStream();
            ds.CopyTo(outp);
            return outp.ToArray();
        }

        public static byte[] Deflate(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        public sealed class NgKeys
        {
            public byte[][] Keys;              // 101 x 272
            public uint[][][] Decrypt;         // 17 x 16 x 256
            public byte[] Lut;                 // 256
        }

        public static NgKeys LoadNgKeys(string magicPath, byte[] aesKey)
        {
            var m = File.ReadAllBytes(magicPath);
            var rnd = new Random((int)Jenk(aesKey));
            var rb1 = new byte[m.Length]; var rb2 = new byte[m.Length];
            var rb3 = new byte[m.Length]; var rb4 = new byte[m.Length];
            rnd.NextBytes(rb1); rnd.NextBytes(rb2); rnd.NextBytes(rb3); rnd.NextBytes(rb4);

            var db = new byte[m.Length];
            for (int i = 0; i < m.Length; i++)
                db[i] = (byte)((m[i] - rb1[i] - rb2[i] - rb3[i] - rb4[i]) & 0xFF);

            db = AesDecrypt(db, aesKey);
            var plain = Inflate(db);

            var k = new NgKeys
            {
                Keys = new byte[101][],
                Decrypt = new uint[17][][],
                Lut = new byte[256]
            };
            int p = 0;
            for (int i = 0; i < 101; i++) { k.Keys[i] = new byte[272]; Buffer.BlockCopy(plain, p, k.Keys[i], 0, 272); p += 272; }
            for (int r = 0; r < 17; r++)
            {
                k.Decrypt[r] = new uint[16][];
                for (int b = 0; b < 16; b++)
                {
                    k.Decrypt[r][b] = new uint[256];
                    for (int e = 0; e < 256; e++) { k.Decrypt[r][b][e] = BitConverter.ToUInt32(plain, p); p += 4; }
                }
            }
            Buffer.BlockCopy(plain, p, k.Lut, 0, 256);
            return k;
        }

        public static uint NameHash(string name, byte[] lut)
        {
            uint result = 0;
            foreach (var ch in name)
            {
                uint temp = 1025u * (lut[(byte)ch] + result);
                result = (temp >> 6) ^ temp;
            }
            return 32769u * (((9u * result) >> 11) ^ (9u * result));
        }

        public static byte[] GetNgKey(NgKeys k, string name, uint length)
        {
            uint idx = (NameHash(name, k.Lut) + length + (101 - 40)) % 0x65;
            return k.Keys[idx];
        }

        static uint[][] SubKeys(byte[] key)
        {
            var sk = new uint[17][];
            for (int i = 0; i < 17; i++)
            {
                sk[i] = new uint[4];
                for (int j = 0; j < 4; j++) sk[i][j] = BitConverter.ToUInt32(key, (i * 4 + j) * 4);
            }
            return sk;
        }

        static byte[] RoundA(byte[] d, uint[] key, uint[][] t)
        {
            uint x1 = t[0][d[0]] ^ t[1][d[1]] ^ t[2][d[2]] ^ t[3][d[3]] ^ key[0];
            uint x2 = t[4][d[4]] ^ t[5][d[5]] ^ t[6][d[6]] ^ t[7][d[7]] ^ key[1];
            uint x3 = t[8][d[8]] ^ t[9][d[9]] ^ t[10][d[10]] ^ t[11][d[11]] ^ key[2];
            uint x4 = t[12][d[12]] ^ t[13][d[13]] ^ t[14][d[14]] ^ t[15][d[15]] ^ key[3];
            var r = new byte[16];
            BitConverter.GetBytes(x1).CopyTo(r, 0); BitConverter.GetBytes(x2).CopyTo(r, 4);
            BitConverter.GetBytes(x3).CopyTo(r, 8); BitConverter.GetBytes(x4).CopyTo(r, 12);
            return r;
        }

        static byte[] RoundB(byte[] d, uint[] key, uint[][] t)
        {
            uint x1 = t[0][d[0]] ^ t[7][d[7]] ^ t[10][d[10]] ^ t[13][d[13]] ^ key[0];
            uint x2 = t[1][d[1]] ^ t[4][d[4]] ^ t[11][d[11]] ^ t[14][d[14]] ^ key[1];
            uint x3 = t[2][d[2]] ^ t[5][d[5]] ^ t[8][d[8]] ^ t[15][d[15]] ^ key[2];
            uint x4 = t[3][d[3]] ^ t[6][d[6]] ^ t[9][d[9]] ^ t[12][d[12]] ^ key[3];
            var r = new byte[16];
            BitConverter.GetBytes(x1).CopyTo(r, 0); BitConverter.GetBytes(x2).CopyTo(r, 4);
            BitConverter.GetBytes(x3).CopyTo(r, 8); BitConverter.GetBytes(x4).CopyTo(r, 12);
            return r;
        }

        public static byte[] DecryptNG(byte[] data, byte[] key, uint[][][] tables)
        {
            var sk = SubKeys(key);
            var outp = new byte[data.Length];
            int blocks = data.Length / 16;
            var block = new byte[16];
            for (int i = 0; i < blocks; i++)
            {
                Buffer.BlockCopy(data, i * 16, block, 0, 16);
                var b = RoundA(block, sk[0], tables[0]);
                b = RoundA(b, sk[1], tables[1]);
                for (int r = 2; r <= 15; r++) b = RoundB(b, sk[r], tables[r]);
                b = RoundA(b, sk[16], tables[16]);
                Buffer.BlockCopy(b, 0, outp, i * 16, 16);
            }
            int left = data.Length % 16;
            if (left != 0) Buffer.BlockCopy(data, data.Length - left, outp, data.Length - left, left);
            return outp;
        }
    }
}
