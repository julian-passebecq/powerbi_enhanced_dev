# Power Query M Lab

## New module

PbiBench should not stop at semantic-model metadata.

Add an M-focused editor/analysis surface:

- M code editor
- snippets/functions
- query dependency graph
- source detection
- native query extraction
- parameter detection
- privacy/credential boundary hints
- folding analysis later
- test refresh on sample data
- Git diff.

## References

### User-supplied PowerQueryM library
The supplied repository uses the Unlicense/public-domain dedication.
It can be used as a safe optional snippet/function reference.

### Microsoft language services
Prefer Microsoft's Power Query language-services project for current syntax/intellisense behavior when integrating editor assistance.

## Action examples

- replace repeated path/server constants with parameters
- detect hard-coded environment values
- recommend staging queries
- detect duplicate transformation fragments
- mark likely fact/dimension outputs
- flag expensive row-wise operations
- generate dev/sample row limiter
- generate documented function wrapper.
