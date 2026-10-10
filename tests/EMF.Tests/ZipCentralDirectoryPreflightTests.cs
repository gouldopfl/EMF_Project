using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class ZipCentralDirectoryPreflightTests
{
    internal static byte[] Zip(int count=1,string name="file.txt")
    {
        using var m=new MemoryStream();
        using(var z=new ZipArchive(m,ZipArchiveMode.Create,true))
            for(var i=0;i<count;i++){using var s=z.CreateEntry(name,CompressionLevel.NoCompression).Open();s.Write("abc"u8);}
        return m.ToArray();
    }
    internal static ushort U16(byte[] b,int p)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
    internal static uint U32(byte[] b,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
    internal static void W16(byte[] b,int p,int v)=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p),checked((ushort)v));
    internal static void W32(byte[] b,int p,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p),v);
    internal static void W64(byte[] b,int p,ulong v)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(p),v);
    internal static ZipRetainedBinding Binding(byte[] b)=>new("parent","revision",new string('A',64),b.Length);
    internal static EMF.Orchestration.Models.ZipCentralDirectoryPreflightResult Validate(byte[] b)=>new ZipCentralDirectoryPreflight().Validate(b,Binding(b));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(999)] [InlineData(1000)]
    public void Valid_entry_counts_match_normal_runtime_parser(int count)
    {
        var b=Zip(count);var r=Validate(b);using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);
        Assert.Equal(count,r.CentralEntries);Assert.Equal(count,z.Entries.Count);
        Assert.Equal(z.GetType().GetField("_centralDirectoryStart",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(z),(long)r.DirectoryOffset);
    }
    [Fact] public void Count_1001_rejects(){Assert.Throws<InvalidDataException>(()=>Validate(Zip(1001)));}
    [Theory]
    [InlineData(1)] [InlineData(4092)] [InlineData(4093)] [InlineData(4094)] [InlineData(4095)] [InlineData(4096)] [InlineData(65535)]
    public void Comments_and_block_boundaries_use_same_runtime_candidate(int comment)
    {
        var original=Zip();var b=new byte[original.Length+comment];original.CopyTo(b,0);W16(b,original.Length-2,comment);
        Array.Fill(b,(byte)'x',original.Length,comment);var r=Validate(b);
        using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);
        Assert.Equal(original.Length-22,r.EndRecordOffset);
        Assert.Equal(z.GetType().GetField("_centralDirectoryStart",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(z),(long)r.DirectoryOffset);
    }
    [Fact]
    public void Nearest_candidate_wins_in_both_runtime_and_preflight()
    {
        var older=Zip();var b=new byte[older.Length+22];older.CopyTo(b,0);W16(b,older.Length-2,22);
        W32(b,older.Length,0x06054b50);W32(b,older.Length+16,(uint)older.Length);
        var r=Validate(b);using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);
        Assert.Empty(z.Entries);Assert.Equal(older.Length,r.EndRecordOffset);Assert.Equal(older.Length,r.DirectoryOffset);
        Assert.Equal((long)older.Length,z.GetType().GetField("_centralDirectoryStart",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(z));
    }
    [Fact]
    public void Invalid_nearer_candidate_does_not_fall_back_to_older_valid_record()
    {
        var older=Zip();var b=new byte[older.Length+22];older.CopyTo(b,0);W16(b,older.Length-2,22);
        W32(b,older.Length,0x06054b50);W16(b,older.Length+4,1);
        Assert.Throws<InvalidDataException>(()=>Validate(b));
        Assert.Throws<InvalidDataException>(()=>new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read));
    }
    [Fact]
    public void Short_origin_read_selects_truncated_signature_in_final_eighteen_like_runtime()
    {
        var b=Zip();W32(b,b.Length-10,0x06054b50);
        Assert.Equal(b.Length-10,ZipCentralDirectoryPreflight.SelectEndRecord(b));
        Assert.Throws<InvalidDataException>(()=>Validate(b));
        Assert.Throws<InvalidDataException>(()=>new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read));
    }
    [Fact]
    public void Candidate_one_byte_outside_runtime_window_is_not_selected()
    {
        var b=new byte[65561];W32(b,0,0x06054b50);
        Assert.Throws<InvalidDataException>(()=>ZipCentralDirectoryPreflight.SelectEndRecord(b));
        Assert.Throws<InvalidDataException>(()=>new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read));
    }
    [Theory]
    [InlineData(65558)] [InlineData(65559)] [InlineData(65560)]
    public void Runtime_short_origin_block_overscan_selects_same_candidate(int length)
    {
        var b=new byte[length];W32(b,0,0x06054b50);
        Assert.Equal(0,ZipCentralDirectoryPreflight.SelectEndRecord(b));
        using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);Assert.Empty(z.Entries);
        // Exact end/comment validation still rejects this trailing-junk layout.
        Assert.Throws<InvalidDataException>(()=>Validate(b));
    }
    [Theory]
    [InlineData(5000,883)] [InlineData(5000,884)] [InlineData(5000,885)]
    [InlineData(65558,61)] [InlineData(65558,62)] [InlineData(65558,63)]
    public void Short_origin_read_fills_destination_and_finds_crossing_signature(int length,int offset)
    {
        var b=new byte[length];W32(b,offset,0x06054b50);W32(b,offset+16,(uint)offset);
        Assert.Equal(offset,ZipCentralDirectoryPreflight.SelectEndRecord(b));
        using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);
        Assert.Equal((long)offset,z.GetType().GetField("_centralDirectoryStart",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(z));
    }
    [Fact]
    public void Directory_cannot_stop_before_additional_header_that_runtime_would_read()
    {
        var b=Zip(2);var end=b.Length-22;var start=(int)U32(b,end+16);var first=46+U16(b,start+28)+U16(b,start+30)+U16(b,start+32);
        W32(b,end+12,(uint)first);W16(b,end+8,1);W16(b,end+10,1);
        Assert.Throws<InvalidDataException>(()=>Validate(b));
        using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(()=>z.Entries);
    }
    [Theory]
    [InlineData(28)] [InlineData(30)] [InlineData(32)]
    public void Truncated_variable_fields_reject(int field)
    {
        var b=Zip();var start=(int)U32(b,b.Length-6);W16(b,start+field,65535);
        Assert.Throws<InvalidDataException>(()=>Validate(b));
    }
    internal static byte[] AddExtra(byte[] original,byte[] extra,int comment=0)
    {
        var end=original.Length-22;var start=(int)U32(original,end+16);var name=U16(original,start+28);var oldExtra=U16(original,start+30);
        var oldComment=U16(original,start+32);var insert=start+46+name;
        var b=new byte[original.Length-oldExtra-oldComment+extra.Length+comment];
        original.AsSpan(0,insert).CopyTo(b);extra.CopyTo(b,insert);
        original.AsSpan(end,22).CopyTo(b.AsSpan(b.Length-22));
        W16(b,start+30,extra.Length);W16(b,start+32,comment);W32(b,b.Length-10,(uint)(b.Length-22-start));return b;
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Malformed_extra_tlv_tail_rejects_even_when_runtime_tolerates_it(int length)
    {
        var b=AddExtra(Zip(),new byte[length]);Assert.Throws<InvalidDataException>(()=>Validate(b));
    }
    [Fact]
    public void Truncated_extra_payload_rejects()
    {
        var extra=new byte[4];W16(extra,0,123);W16(extra,2,1);
        Assert.Throws<InvalidDataException>(()=>Validate(AddExtra(Zip(),extra)));
    }
    [Fact]
    public void Maximum_field_widths_and_metadata_totals_are_admitted()
    {
        var extra=new byte[65535];W16(extra,0,123);W16(extra,2,65531);
        var b=AddExtra(Zip(name:new string('a',65535)),extra,65535);var r=Validate(b);
        Assert.Equal(65535,r.FilenameBytes);Assert.Equal(65535,r.ExtraBytes);Assert.Equal(65535,r.CommentBytes);
        Assert.Equal(46+3L*65535,r.DirectoryBytes);
    }
    [Fact]
    public void Preflight_success_has_no_proportional_name_or_metadata_allocation()
    {
        var small=Zip();var large=Zip(1000,new string('a',1024));var p=new ZipCentralDirectoryPreflight();
        var s=Binding(small);var l=Binding(large);p.Validate(small,s);p.Validate(large,l);
        var before=GC.GetAllocatedBytesForCurrentThread();p.Validate(small,s);var smallAllocated=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();p.Validate(large,l);var largeAllocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,smallAllocated);Assert.Equal(0,largeAllocated);
    }
    [Fact]
    public void Parent_limit_and_binding_length_reject_before_walk()
    {
        var b=new byte[33554433];Assert.Throws<InvalidDataException>(()=>Validate(b));
        var valid=Zip();Assert.Throws<InvalidDataException>(()=>new ZipCentralDirectoryPreflight().Validate(valid,Binding(valid) with{Length=valid.Length+1}));
    }
}
