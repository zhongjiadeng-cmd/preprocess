# LaserPMT UTF-8 BOM Compatibility Fix

## Problem

The macOS UI writes the temporary LaserPMT request with `Encoding.UTF8`. In the
current runtime that file begins with a UTF-8 byte-order mark. The Python JSON
loader opens JSON as plain `utf-8`, so `json.loads` rejects the leading BOM
before LaserPMT generation starts.

## Approved approach

Apply defense in depth at the process boundary:

1. The C# UI writes future temporary request files using an explicit UTF-8
   encoding without a BOM.
2. The Python JSON loader reads JSON using `utf-8-sig`, which accepts both BOM
   and BOM-free UTF-8. This also permits an imported base `machine.json` that
   was produced by software using a BOM.

The JSON schema, duplicate-key rejection, generated package structure,
parameter expansion, patch contents, and machine-motion rules do not change.

## Error handling

Malformed UTF-8 and malformed JSON continue to raise the existing
`invalid JSON file` error. Only a leading UTF-8 BOM becomes accepted; other
unexpected leading content remains invalid.

## Verification

- Add a Python regression test for a BOM-prefixed request JSON.
- Add a Python regression test for a BOM-prefixed base `machine.json`.
- Add a source-level C# regression assertion that the request writer uses an
  explicit no-BOM encoding.
- Run the complete Python and .NET test suites.
- Rebuild and verify the macOS application bundle.

