# EMF Ingestion Format Support

## Status

The initial EMF file-ingestion boundary is complete.

This document records formats intentionally supported by the current
discovery, inspection, extraction, OCR, and archive-processing layers.

## Text and Structured Text

| Extensions | Handling |
|---|---|
| `.txt`, `.log` | UTF-8 text extraction |
| `.md`, `.markdown` | UTF-8 Markdown extraction |
| `.yaml`, `.yml` | UTF-8 YAML extraction |
| `.csv`, `.tsv` | Delimited-text extraction |
| `.json` | JSON text extraction and inspection |
| `.xml` | XML text extraction and inspection |
| `.html`, `.htm` | HTML visible-text extraction and inspection |
| `.rtf` | RTF text extraction |

## Microsoft Office

| Extensions | Handling |
|---|---|
| `.docx` | Open XML Word text extraction |
| `.doc` | Legacy Word text extraction |
| `.xlsx` | Open XML Excel text extraction |
| `.xls` | Legacy Excel text extraction |
| `.pptx` | Open XML PowerPoint text extraction |
| `.ppt` | Legacy PowerPoint text extraction |

OOXML package signatures are distinguished from generic ZIP archives during
artifact inspection.

## OpenDocument

| Extensions | Handling |
|---|---|
| `.odt` | OpenDocument text extraction |
| `.ods` | OpenDocument spreadsheet extraction |
| `.odp` | OpenDocument presentation extraction |

OpenDocument package signatures are detected during artifact inspection.

## PDF

| Extension | Handling |
|---|---|
| `.pdf` | Native text extraction with image/OCR fallback |

PDF signature detection is supported during artifact inspection.

## Email

| Extensions | Handling |
|---|---|
| `.eml` | MIME email extraction and inspection |
| `.msg` | Outlook message extraction |

Email attachment processing is supported by the inventory workflow.

## Images and OCR

| Extensions | Handling |
|---|---|
| `.jpg`, `.jpeg` | OCR |
| `.png` | OCR |
| `.tif`, `.tiff` | OCR |
| `.bmp` | OCR |
| `.gif` | OCR |
| `.webp` | OCR |

Image decoding uses OpenCV and text recognition uses PaddleOCR.

## SQLite

| Extensions | Handling |
|---|---|
| `.db`, `.sqlite`, `.sqlite3` | SQLite discovery and inspection |

SQLite file signatures are detected independently of the filename extension.

## Archives

ZIP archives are recognized by signature and have dedicated entry extraction
and workflow processing.

Office Open XML and OpenDocument packages are detected before the generic ZIP
handler so they are not misclassified as ordinary archives.

## Inspection Architecture

Artifact inspection currently includes:

- Office/OpenDocument package signature detection
- PDF signature detection
- SQLite signature detection
- generic ZIP signature detection
- CSV inspection
- EML inspection
- HTML inspection
- JSON inspection
- plain-text inspection
- XML inspection

Inspection results may enrich persisted artifact metadata without changing
artifact identity, fingerprint, or provenance.

## Deferred Formats

The following formats are intentionally outside the initial ingestion boundary
and may be added when a demonstrated evidence requirement exists:

- TAR, GZIP, TGZ, and 7Z archives
- PST and OST mailbox stores
- HEIC/HEIF images
- Parquet and other analytical-storage formats
- proprietary database formats
- CAD and engineering formats
- audio and video media
- other specialized or proprietary document formats

Deferred status means the format is not currently promised as supported. It
does not prevent future format-specific providers from being added.

## Design Boundary

New formats should be added only when they provide clear evidence-ingestion
value. A new format should normally include:

1. content-type resolution;
2. signature detection when appropriate;
3. safe cross-platform extraction or inspection;
4. focused tests with representative files;
5. full regression verification.

This keeps ingestion capabilities explicit, testable, and bounded.
