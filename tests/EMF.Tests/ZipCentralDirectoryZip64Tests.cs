using System.IO.Compression;
using static EMF.Tests.ZipCentralDirectoryPreflightTests;

namespace EMF.Tests;

public sealed class ZipCentralDirectoryZip64Tests
{
    private static byte[] Outer(byte[] original)
    {
        var end=original.Length-22;var directory=U32(original,end+16);var size=U32(original,end+12);var count=U16(original,end+10);
        var b=new byte[original.Length+76];original.AsSpan(0,end).CopyTo(b);
        W32(b,end,0x06064b50);W64(b,end+4,44);W16(b,end+12,45);W16(b,end+14,45);
        W64(b,end+24,count);W64(b,end+32,count);W64(b,end+40,size);W64(b,end+48,directory);
        W32(b,end+56,0x07064b50);W64(b,end+64,(ulong)end);W32(b,end+72,1);
        original.AsSpan(end).CopyTo(b.AsSpan(end+76));
        W16(b,end+80,65535);W16(b,end+82,65535);W16(b,end+84,65535);W16(b,end+86,65535);
        W32(b,end+88,uint.MaxValue);W32(b,end+92,uint.MaxValue);return b;
    }
    private static byte[] Entry(int flags,bool full=false)
    {
        var original=Zip();var start=(int)U32(original,original.Length-6);var size=full?28:8*((flags&1)>0?1:0)+8*((flags&2)>0?1:0)+8*((flags&4)>0?1:0)+((flags&8)>0?4:0);
        var extra=new byte[size+4];W16(extra,0,1);W16(extra,2,size);var p=4;
        if(full){W64(extra,4,3);W64(extra,12,3);W64(extra,20,0);W32(extra,28,0);}
        else{if((flags&1)>0){W64(extra,p,3);p+=8;}if((flags&2)>0){W64(extra,p,3);p+=8;}if((flags&4)>0){W64(extra,p,0);p+=8;}if((flags&8)>0)W32(extra,p,0);}
        var b=AddExtra(original,extra);W16(b,start+6,45);
        if((flags&1)>0)W32(b,start+24,uint.MaxValue);if((flags&2)>0)W32(b,start+20,uint.MaxValue);
        if((flags&4)>0)W32(b,start+42,uint.MaxValue);if((flags&8)>0)W16(b,start+34,65535);return b;
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Compact_size_offset_subsets_match_runtime(int flags)
    {
        var b=Entry(flags);Assert.Equal(1,Validate(b).CentralEntries);
        using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);var entry=Assert.Single(z.Entries);
        Assert.Equal(3,entry.Length);Assert.Equal(3,entry.CompressedLength);using var s=entry.Open();Assert.Equal((int)'a',s.ReadByte());
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(8)] [InlineData(15)]
    public void Full_tuple_including_disk_replacement_matches_runtime(int flags)
    {
        var b=Entry(flags,true);Validate(b);using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);
        using var s=Assert.Single(z.Entries).Open();Assert.Equal((int)'a',s.ReadByte());
    }
    [Theory]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void Compact_disk_replacements_are_outside_closed_subset(int flags)=>Assert.Throws<InvalidDataException>(()=>Validate(Entry(flags)));
    [Fact]
    public void Canonical_outer_zip64_is_admitted_by_preflight_and_runtime()
    {
        var b=Outer(Zip());var r=Validate(b);Assert.True(r.UsesZip64);
        using var z=new ZipArchive(new MemoryStream(b),ZipArchiveMode.Read);Assert.Single(z.Entries);
    }
    [Theory]
    [InlineData(4)] [InlineData(24)] [InlineData(32)] [InlineData(40)] [InlineData(48)]
    public void Oversized_record_fields_fail_closed(int field)
    {
        var b=Outer(Zip());var z=b.Length-98;W64(b,z+field,ulong.MaxValue);
        Assert.Throws<InvalidDataException>(()=>Validate(b));
    }
    [Fact]
    public void Size_only_sentinel_and_missing_locator_reject()
    {
        var b=Zip();W32(b,b.Length-10,uint.MaxValue);Assert.Throws<InvalidDataException>(()=>Validate(b));
        b=Outer(Zip());W32(b,b.Length-42,0);Assert.Throws<InvalidDataException>(()=>Validate(b));
    }
    [Fact]
    public void Contradictory_classic_values_and_nonadjacent_end_region_reject()
    {
        var b=Outer(Zip());W32(b,b.Length-10,1);Assert.Throws<InvalidDataException>(()=>Validate(b));
        b=Outer(Zip());W64(b,b.Length-98+40,1);Assert.Throws<InvalidDataException>(()=>Validate(b));
        b=Outer(Zip());W64(b,b.Length-42+8,1);Assert.Throws<InvalidDataException>(()=>Validate(b));
    }
    [Fact]
    public void Redundant_entry_values_and_duplicate_zip64_tags_reject()
    {
        var b=Entry(0,true);var start=(int)U32(b,b.Length-6);var extra=start+46+U16(b,start+28);
        W64(b,extra+4,4);Assert.Throws<InvalidDataException>(()=>Validate(b));
        var tags=new byte[64];W16(tags,0,1);W16(tags,2,28);W16(tags,32,1);W16(tags,34,28);
        Assert.Throws<InvalidDataException>(()=>Validate(AddExtra(Zip(),tags)));
    }
    [Fact]
    public void Missing_zip64_entry_replacement_rejects()
    {
        var b=Zip();var start=(int)U32(b,b.Length-6);W32(b,start+24,uint.MaxValue);
        Assert.Throws<InvalidDataException>(()=>Validate(b));
    }
    [Fact]
    public void Zip64_extra_payload_cannot_extend_beyond_admitted_extra_extent()
    {
        var extra=new byte[12];W16(extra,0,1);W16(extra,2,28);
        Assert.Throws<InvalidDataException>(()=>Validate(AddExtra(Zip(),extra)));
    }
}
