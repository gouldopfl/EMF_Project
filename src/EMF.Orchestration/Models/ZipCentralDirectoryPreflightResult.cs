using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Models;

public readonly record struct ZipCentralDirectoryPreflightResult(
    string ProfileVersion, ZipRetainedBinding Parent, int EndRecordOffset, int DirectoryOffset,
    int DirectoryBytes, int CentralEntries, long FilenameBytes, long ExtraBytes, long CommentBytes,
    int ArchiveCommentBytes, long ExtraRecords, bool UsesZip64);
