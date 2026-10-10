using System.Buffers.Binary;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

// Narrow structural admission only. ZipArchive remains the semantic parser.
// No names, per-entry arrays, decoded strings, or proportional scratch buffers.
public sealed class ZipCentralDirectoryPreflight
{
    private static ushort U16(ReadOnlySpan<byte> b,int p)=>BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(p,2));
    private static uint U32(ReadOnlySpan<byte> b,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(p,4));
    private static long U64(ReadOnlySpan<byte> b,int p)
    {
        var value=BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(p,8));
        if(value>long.MaxValue)throw Invalid();return (long)value;
    }
    private static InvalidDataException Invalid()=>new("ZIP central-directory preflight rejected the structure.");
    private static void Require(bool condition){if(!condition)throw Invalid();}
    internal static int SelectEndRecord(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<22)throw Invalid();
        // Mirror .NET 10's helper on the same-array MemoryStream used below.
        // In its short-origin branch ReadAtLeast's minimum is Position, but its
        // destination is the WHOLE block: MemoryStream fills that destination.
        var position=bytes.Length-18;var total=0;var blockLength=4096;var outOfBytes=false;
        while(!outOfBytes && total<ZipCentralDirectoryLimits.EndSearchBytes)
        {
            var overlap=total==0?0:4;
            if(ZipCentralDirectoryLimits.EndSearchBytes-total+overlap<blockLength)
                blockLength=ZipCentralDirectoryLimits.EndSearchBytes-total+overlap;
            var full=position>=blockLength;
            var start=full?position-(blockLength-overlap):0;
            var read=full?blockLength:position==0?0:Math.Min(blockLength,bytes.Length);
            for(var p=start+read-4;p>=start;p--)
                if(U32(bytes,p)==0x06054b50)return p;
            outOfBytes=read<blockLength;total+=read-overlap;position=start;
        }
        throw Invalid();
    }
    public ZipCentralDirectoryPreflightResult Validate(ReadOnlySpan<byte> bytes,ZipRetainedBinding parent,
        CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        Require(bytes.Length<=ZipCentralDirectoryLimits.MaximumParentBytes && parent.Length==bytes.Length);
        var end=SelectEndRecord(bytes);Require(end<=bytes.Length-22);var comment=U16(bytes,end+20);
        Require(checked(end+22+comment)==bytes.Length);
        var disk=U16(bytes,end+4);var directoryDisk=U16(bytes,end+6);
        var diskCount=U16(bytes,end+8);var totalCount=U16(bytes,end+10);
        Require(disk==directoryDisk && diskCount==totalCount);
        long count=totalCount,size=U32(bytes,end+12),offset=U32(bytes,end+16);
        var zip64=disk==ushort.MaxValue || offset==uint.MaxValue || totalCount==ushort.MaxValue;
        var region=end;
        if(zip64)
        {
            Require(end>=76);var locator=end-20;
            Require(U32(bytes,locator)==0x07064b50 && U32(bytes,locator+4)==0 && U32(bytes,locator+16)==1);
            var z=U64(bytes,locator+8);Require(z>=0 && z<=locator-56 && checked(z+56)==locator);
            region=checked((int)z);
            Require(U32(bytes,region)==0x06064b50 && U64(bytes,region+4)==44 &&
                U32(bytes,region+16)==0 && U32(bytes,region+20)==0);
            var zCount=U64(bytes,region+32);var zSize=U64(bytes,region+40);var zOffset=U64(bytes,region+48);
            Require(U64(bytes,region+24)==zCount && (disk==0 || disk==ushort.MaxValue));
            Require((totalCount==ushort.MaxValue || totalCount==zCount) &&
                (size==uint.MaxValue || size==zSize) && (offset==uint.MaxValue || offset==zOffset));
            count=zCount;size=zSize;offset=zOffset;
        }
        else Require(disk==0 && size!=uint.MaxValue);
        Require(count<=ZipCentralDirectoryLimits.MaximumEntries && offset<=region && size<=region &&
            checked(offset+size)==region && size<=ZipCentralDirectoryLimits.MaximumDirectoryBytes);
        var pos=checked((int)offset);var directoryEnd=region;
        long names=0,extras=0,comments=0,extraRecords=0;var entries=0;
        while(pos<directoryEnd)
        {
            ct.ThrowIfCancellationRequested();
            Require(entries<ZipCentralDirectoryLimits.MaximumEntries && directoryEnd-pos>=46 && U32(bytes,pos)==0x02014b50);
            var name=U16(bytes,pos+28);var extra=U16(bytes,pos+30);var entryComment=U16(bytes,pos+32);
            var next=checked(pos+46+name+extra+entryComment);Require(next<=directoryEnd);
            names=checked(names+name);extras=checked(extras+extra);comments=checked(comments+entryComment);
            long u=U32(bytes,pos+24),c=U32(bytes,pos+20),local=U32(bytes,pos+42);var entryDisk=(long)U16(bytes,pos+34);
            var needU=u==uint.MaxValue;var needC=c==uint.MaxValue;var needO=local==uint.MaxValue;var needD=entryDisk==ushort.MaxValue;
            var extraPos=checked(pos+46+name);var extraEnd=checked(extraPos+extra);var seenZip64=false;
            while(extraPos<extraEnd)
            {
                ct.ThrowIfCancellationRequested();
                Require(extraEnd-extraPos>=4);var tag=U16(bytes,extraPos);var length=U16(bytes,extraPos+2);
                var extraNext=checked(extraPos+4+length);Require(extraNext<=extraEnd);extraRecords=checked(extraRecords+1);
                if(tag==1)
                {
                    Require(!seenZip64);seenZip64=true;
                    var data=bytes.Slice(extraPos+4,length);
                    var compact=8*((needU?1:0)+(needC?1:0)+(needO?1:0));
                    Require(length==28 || (!needD && compact>0 && length==compact));
                    if(length==28)
                    {
                        var zu=U64(data,0);var zc=U64(data,8);var zo=U64(data,16);var zd=(long)U32(data,24);
                        Require((needU || u==zu) && (needC || c==zc) && (needO || local==zo) && (needD || entryDisk==zd));
                        u=zu;c=zc;local=zo;entryDisk=zd;
                    }
                    else
                    {
                        var p=0;
                        if(needU){u=U64(data,p);p+=8;}
                        if(needC){c=U64(data,p);p+=8;}
                        if(needO)local=U64(data,p);
                    }
                }
                extraPos=extraNext;
            }
            Require((!(needU||needC||needO||needD) || seenZip64) && entryDisk==0 &&
                u<=ZipNumericLimits.Child && c<=ZipNumericLimits.Parent && local<offset && checked(local+30)<=offset);
            entries++;pos=next;
        }
        Require(entries==count && checked(names+extras+comments)==checked(size-46L*entries));
        return new(ZipCentralDirectoryLimits.ProfileVersion,parent,end,checked((int)offset),checked((int)size),entries,
            names,extras,comments,comment,extraRecords,zip64);
    }
}
