# Clean-room / License Boundaries

## Allowed foundation
Tabular Editor 2:
- MIT
- fork/modify with license preservation.

## Public requirements references
TE3 public docs, comparison pages, release notes:
- use to understand what problem a feature solves;
- implement the feature independently.

## Do not do
- decompile TE3 binaries
- copy TE3 proprietary source
- copy non-open UI assets/icons/themes
- reproduce proprietary internal algorithms from reverse engineering
- present PbiBench as Tabular Editor 3
- bypass TE3 licensing.

## Paid license
A paid TE3 license allows use of the licensed product under its terms.
It does not turn proprietary product code into open source.

## Open components
Individual third-party/open-source components may be reusable if:
- license permits
- attribution/notice retained
- copyleft implications understood.

## Public formats/protocols
Use public Microsoft:
- TOM
- TMSL
- TMDL
- XMLA
- DAX
- Fabric REST
- Power BI REST
- PBIP/PBIR schemas

as PbiBench's interoperability layer.

## Rule of thumb

Implement the **capability**, not their implementation.
