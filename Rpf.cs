using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RpfStudio
{
    public enum EntryKind { Directory, Binary, Resource }
    public enum ArchiveEnc { None = 0, Open = 0x4E45504F, Aes = 0x0FFFFFF9, Ng = 0x0FEFFFFF }

    public class RpfEntry
    {
        public int Index;
        public EntryKind Kind;
        public uint NameOffset;
        public string Name = "";
        public uint EntriesIndex, EntriesCount;
        public uint FileOffset, FileSize, Uncompressed, EncType;
        public RpfEntry Parent;
        public List<RpfEntry> Children = new List<RpfEntry>();
        public string Path => Parent == null ? Name : Parent.Path + "\\" + Name;
        public bool IsEncrypted => EncType == 1;
        public long StoredSize => FileSize == 0 ? Uncompressed : FileSize;
    }

    public class RpfArchive
    {
        public string FilePath;
        public uint EntryCount, NamesLength;
        public ArchiveEnc Encryption;
        public List<RpfEntry> Entries = new List<RpfEntry>();
        public RpfEntry Root;
        public long FileLength;

        byte[] _entriesRaw;
        byte[] _namesRaw;

        public static Crypto.NgKeys Ng;   // set once at startup, enables NG reading

        public void Load()
        {
            using var fs = File.OpenRead(FilePath);
            using var br = new BinaryReader(fs);
            uint version = br.ReadUInt32();
            if (version != 0x52504637) throw new InvalidDataException("Keine RPF7-Datei.");
            EntryCount = br.ReadUInt32();
            NamesLength = br.ReadUInt32();
            Encryption = (ArchiveEnc)br.ReadUInt32();
            FileLength = fs.Length;
            if (Encryption == ArchiveEnc.Ng && Ng != null)
                _tocKey = Crypto.GetNgKey(Ng, System.IO.Path.GetFileName(FilePath), (uint)FileLength);

            var entries = br.ReadBytes((int)EntryCount * 16);
            var names = br.ReadBytes((int)NamesLength);

            if (Encryption == ArchiveEnc.Ng)
            {
                if (Ng == null) throw new InvalidDataException("NG-verschlüsselt, aber keine NG-Keys geladen (magic.dat + GTA5.exe nötig).");
                var key = Crypto.GetNgKey(Ng, System.IO.Path.GetFileName(FilePath), (uint)FileLength);
                entries = Crypto.DecryptNG(entries, key, Ng.Decrypt);
                names = Crypto.DecryptNG(names, key, Ng.Decrypt);
            }
            else if (Encryption == ArchiveEnc.Aes)
            {
                throw new InvalidDataException("AES-verschlüsselte Archive werden nicht unterstützt.");
            }

            _entriesRaw = entries;
            _namesRaw = names;
            Parse();
        }

        void Parse()
        {
            Entries.Clear();
            for (int i = 0; i < EntryCount; i++)
            {
                int o = i * 16;
                uint y = BitConverter.ToUInt32(_entriesRaw, o);
                uint x = BitConverter.ToUInt32(_entriesRaw, o + 4);
                var e = new RpfEntry { Index = i };
                if (x == 0x7FFFFF00)
                {
                    e.Kind = EntryKind.Directory; e.NameOffset = y;
                    e.EntriesIndex = BitConverter.ToUInt32(_entriesRaw, o + 8);
                    e.EntriesCount = BitConverter.ToUInt32(_entriesRaw, o + 12);
                }
                else if ((x & 0x80000000) == 0)
                {
                    e.Kind = EntryKind.Binary;
                    ulong buf = BitConverter.ToUInt64(_entriesRaw, o);
                    e.NameOffset = (uint)(buf & 0xFFFF);
                    e.FileSize = (uint)((buf >> 16) & 0xFFFFFF);
                    e.FileOffset = (uint)((buf >> 40) & 0xFFFFFF);
                    e.Uncompressed = BitConverter.ToUInt32(_entriesRaw, o + 8);
                    e.EncType = BitConverter.ToUInt32(_entriesRaw, o + 12);
                }
                else
                {
                    e.Kind = EntryKind.Resource;
                    e.NameOffset = y & 0xFFFF;
                    e.FileSize = (y >> 16) & 0xFFFFFF;
                    e.FileOffset = x & 0xFFFFFF;
                    e.Uncompressed = BitConverter.ToUInt32(_entriesRaw, o + 8);
                }
                e.Name = ReadName(e.NameOffset);
                Entries.Add(e);
            }
            Root = Entries[0];
            Root.Name = "";
            var stack = new Stack<RpfEntry>();
            stack.Push(Root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                for (int i = (int)d.EntriesIndex; i < d.EntriesIndex + d.EntriesCount && i < Entries.Count; i++)
                {
                    var c = Entries[i];
                    c.Parent = d;
                    d.Children.Add(c);
                    if (c.Kind == EntryKind.Directory) stack.Push(c);
                }
            }
        }

        string ReadName(uint off)
        {
            if (off >= _namesRaw.Length) return "";
            int end = (int)off;
            while (end < _namesRaw.Length && _namesRaw[end] != 0) end++;
            return Encoding.UTF8.GetString(_namesRaw, (int)off, end - (int)off);
        }

        public RpfEntry Find(params string[] path)
        {
            var cur = Root;
            foreach (var part in path)
            {
                cur = cur?.Children.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (cur == null) return null;
            }
            return cur;
        }

        public byte[] Extract(RpfEntry e)
        {
            if (e.Kind != EntryKind.Binary || e.StoredSize == 0) return null;
            using var fs = File.OpenRead(FilePath);
            fs.Position = (long)e.FileOffset * 512;
            var raw = new byte[e.StoredSize];
            int read = 0;
            while (read < raw.Length)
            {
                int n = fs.Read(raw, read, raw.Length - read);
                if (n <= 0) break;
                read += n;
            }
            var data = raw;
            if (e.IsEncrypted)
            {
                if (Encryption == ArchiveEnc.Ng) data = Crypto.DecryptNG(data, Crypto.GetNgKey(Ng, e.Name, e.Uncompressed), Ng.Decrypt);
                else return null;
            }
            if (e.FileSize > 0)
            {
                try { return Crypto.Inflate(data); } catch { return data; }
            }
            return data;
        }

        /// <summary>Schreibt Daten in einen Eintrag. Bei NG-Archiven wird der Index auf OPEN (Klartext) umgestellt.</summary>
        public static uint[][][] AEnc;
        public static NgLut[][] BLut;
        static byte[] _tocKey;

        public static void EnsureTables(Action<string> log = null)
        {
            if (AEnc != null && BLut != null) return;
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var fa = Path.Combine(dir, "ngenc_a.bin");
            var fb = Path.Combine(dir, "ngenc_b.bin");
            if (File.Exists(fa) && File.Exists(fb))
            {
                AEnc = NgEncrypt.Load(fa);
                BLut = NgLut.LoadAll(fb);
                return;
            }
            log?.Invoke("Baue NG-Verschlüsselungstabellen (einmalig, ca. 3 Minuten)...");
            if (Ng == null) throw new InvalidOperationException("NG-Keys nicht geladen.");
            AEnc = new uint[17][][];
            foreach (var r in new[] { 0, 1, 16 }) AEnc[r] = NgEncrypt.BuildRound(Ng.Decrypt[r], r);
            BLut = NgLut.BuildAll(Ng.Decrypt, log);
            NgEncrypt.Save(fa, AEnc);
            NgLut.SaveAll(fb, BLut);
        }

        public void Replace(RpfEntry e, byte[] plainData)
        {
            if (e.Kind != EntryKind.Binary) throw new InvalidOperationException("Nur Binärdateien.");

            if (Encryption == ArchiveEnc.Ng)
            {
                EnsureTables();
                var comp0 = Crypto.Deflate(plainData);
                long slot0 = (long)((e.StoredSize + 511) / 512) * 512;
                if (comp0.Length > slot0)
                    throw new InvalidOperationException("Neue Daten passen nicht in den vorhandenen Block (" + comp0.Length + " > " + slot0 + ").");
                var dataKey = Crypto.GetNgKey(Ng, e.Name, (uint)plainData.Length);
                var encData = NgLut.EncryptAll(comp0, dataKey, AEnc, BLut);
                using (var fsw = File.Open(FilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fsw.Position = (long)e.FileOffset * 512;
                    fsw.Write(encData, 0, comp0.Length);
                    var pad0 = new byte[slot0 - comp0.Length];
                    fsw.Write(pad0, 0, pad0.Length);
                }
                e.FileSize = (uint)comp0.Length;
                e.Uncompressed = (uint)plainData.Length;
                e.EncType = 1;
                WriteEntry(e);
                SaveTocEncrypted();
                return;
            }

            var comp = Crypto.Deflate(plainData);
            comp = comp.Length > 0xFFFFFF ? plainData : comp;
            uint newSize = (uint)comp.Length;
            if (comp.Length > 0xFFFFFF) newSize = 0;   // unkomprimiert

            long slot = (long)((e.StoredSize + 511) / 512) * 512;
            long alloc = Math.Max(slot, 512);

            using (var fs = File.Open(FilePath, FileMode.Open, FileAccess.ReadWrite))
            {
                long pos;
                if (comp.Length <= alloc)
                {
                    pos = (long)e.FileOffset * 512;
                }
                else
                {
                    pos = ((fs.Length + 511) / 512) * 512;
                    e.FileOffset = (uint)(pos / 512);
                    fs.SetLength(pos + ((comp.Length + 511) / 512) * 512);
                }
                fs.Position = pos;
                fs.Write(comp, 0, comp.Length);
                var pad = new byte[((comp.Length + 511) / 512) * 512 - comp.Length];
                fs.Write(pad, 0, pad.Length);
            }

            e.FileSize = newSize;
            e.Uncompressed = (uint)plainData.Length;
            e.EncType = 0;
            WriteEntry(e);
            SaveTocPlain();
        }

        void WriteEntry(RpfEntry e)
        {
            int o = e.Index * 16;
            if (e.Kind == EntryKind.Directory)
            {
                BitConverter.GetBytes(e.NameOffset).CopyTo(_entriesRaw, o);
                BitConverter.GetBytes(0x7FFFFF00u).CopyTo(_entriesRaw, o + 4);
                BitConverter.GetBytes(e.EntriesIndex).CopyTo(_entriesRaw, o + 8);
                BitConverter.GetBytes(e.EntriesCount).CopyTo(_entriesRaw, o + 12);
            }
            else
            {
                ulong buf = (ulong)(e.NameOffset & 0xFFFF)
                          | ((ulong)(e.FileSize & 0xFFFFFF) << 16)
                          | ((ulong)(e.FileOffset & 0xFFFFFF) << 40);
                BitConverter.GetBytes(buf).CopyTo(_entriesRaw, o);
                BitConverter.GetBytes(e.Uncompressed).CopyTo(_entriesRaw, o + 8);
                BitConverter.GetBytes(e.EncType).CopyTo(_entriesRaw, o + 12);
            }
        }

        /// <summary>Schreibt den (ggf. entschlüsselten) Index als Klartext und markiert das Archiv als OPEN.</summary>
        public void SaveTocEncrypted()
        {
            EnsureTables();
            var encE = NgLut.EncryptAll(_entriesRaw, _tocKey, AEnc, BLut);
            var encN = NgLut.EncryptAll(_namesRaw, _tocKey, AEnc, BLut);
            using var fs = File.Open(FilePath, FileMode.Open, FileAccess.ReadWrite);
            fs.Position = 16;
            fs.Write(encE, 0, encE.Length);
            fs.Write(encN, 0, encN.Length);
        }

        public void SaveTocPlain()
        {
            using var fs = File.Open(FilePath, FileMode.Open, FileAccess.ReadWrite);
            Encryption = ArchiveEnc.Open;
            using var bw = new BinaryWriter(fs);
            bw.Write(0x52504637u);
            bw.Write(EntryCount);
            bw.Write(NamesLength);
            bw.Write((uint)ArchiveEnc.Open);
            bw.Write(_entriesRaw);
            bw.Write(_namesRaw);
            bw.Flush();
        }

        public void Backup(string suffix = null)
        {
            var name = FilePath + ".bak_" + (suffix ?? DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (!File.Exists(name)) File.Copy(FilePath, name);
        }
    }
}
