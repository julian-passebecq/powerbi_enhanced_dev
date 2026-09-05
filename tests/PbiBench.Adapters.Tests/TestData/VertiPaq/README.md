# Public VPAX compatibility fixture

`Contoso.vpax` is the public SQLBI VertiPaq Analyzer test fixture, pinned to source commit `f3a12773cbb386ac974a13083e451331e9f7ce3d` (Dax.Vpax 1.12.1).

Source: https://github.com/sql-bi/VertiPaq-Analyzer/blob/f3a12773cbb386ac974a13083e451331e9f7ce3d/tests/Dax.Vpax.Tests/_data/Contoso.vpax

SHA-256: `3F1C593DD9BC65E9F9A6B83172CAB5BCFD0C2FEAD48C9FC72A96C8CCA6C6645B`

The upstream MIT license is preserved in `LICENSE.SQLBI.md`. The fixture contains a real schema 1.2.0 snapshot with UTF-8 BOM, object references, partitions, columns, storage segments, and relationships. PbiBench reads only its `DaxModel.json` statistics part; the embedded `Model.bim` is never applied, written, or opened as a live model.
