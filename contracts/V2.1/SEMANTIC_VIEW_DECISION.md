# Do we need a Semantic Viewer?

## Decision

No separate semantic-viewer application.

PbiBench already has:
- the TE2-backed model explorer/tree;
- inspectors/properties;
- the PbiBench WPF relationship diagram;
- diagram search/zoom/pan/fit;
- key/all/no-column display;
- hidden/type labels;
- active vs inactive relationship rendering;
- cardinality;
- relationship arrows;
- relationship selection/editing;
- one-hop related/filtering views;
- table groups/star-layout behavior.

Therefore V2 should rename/evolve this experience into:

`Semantic View`

instead of creating a duplicate viewer.

## V2 Semantic View modes

### Model
Existing relationship diagram, improved presentation.

### Dependencies
Selected measure/column/function dependency graph.

### Report Usage
When PBIP/PBIR context exists:
- measure -> report/page/visual usage;
- column -> report/page/visual usage;
- selected visual -> semantic objects.

### Issues
Optional overlay:
- BPA finding counts;
- relationship warnings;
- unused objects;
- broken report references.

## DAX.do-inspired improvements

Use the user's DAX.do screenshots only as conceptual UX reference:
- cleaner table cards;
- clear fact/dimension markers;
- cardinality at line ends;
- active solid / inactive dashed;
- clearer arrows;
- compact toolbar;
- reset / fit / zoom;
- collapse fields;
- focus selected table neighborhood.

Do not copy DAX.do branding, logo, exact icons, CSS or layout pixel-for-pixel.

## Important

The existing DiagramView is the implementation base.
Do not create a second graph engine unless a measured technical limitation makes replacement necessary.
