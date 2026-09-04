# TE2 reuse decision

## Answer

**Reuse TE2 substantially, but do not simply fork the entire WinForms UX and call that the product.**

TE2 is unusually suitable because:
- it is C#,
- its main license is MIT,
- it already solves the hardest semantic-model object manipulation problems,
- it has a mature scripting model,
- it has BPA concepts,
- it has Power BI/Analysis Services connectivity,
- it is still maintained in 2026.

## What we gain immediately

- mature TOM object navigation patterns
- selection/model wrappers
- dependency traversal
- BPA engine concepts
- scripting conventions
- external-tool workflows
- semantic model save/deploy knowledge
- battle-tested behavior to regression-test against

## Why not use the TE2 UI directly

TE2's application project is .NET Framework 4.8 WinForms and uses older UI/editor/tree dependencies.

That UI is productive but it is not the visual/product direction required here.

A full UI fork creates:
- modernization debt,
- third-party control-license review,
- WinForms styling limitations,
- harder WebView2/D3 integration,
- less separation between upstream TE2 updates and our product.

## Recommended fork model

```text
vendor/TabularEditor2/           untouched upstream snapshot/submodule

src/PowerBIBench.TabularEngine/  selected/ported engine logic
src/PowerBIBench.BPA/            BPA integration/port
src/PowerBIBench.App/            new WPF UI
```

Maintain a `TE2_UPSTREAM_MAP.md` in code that records every file/class copied or adapted from TE2.

## Compatibility bridge

If porting TOMWrapper blocks early development:

1. keep TE2 engine on .NET Framework 4.8 temporarily,
2. expose a small local process/IPC bridge,
3. build WPF .NET 10 shell against the bridge,
4. port engine pieces gradually,
5. remove bridge when modern engine reaches parity.

Do not delay all product UI work waiting for a perfect big-bang port.
