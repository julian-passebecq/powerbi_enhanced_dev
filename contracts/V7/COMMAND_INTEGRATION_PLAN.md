# Command Integration Plan

## Principle

The fastest way to make TE2 feel native is not to rewrite the editor.
It is to make PbiBench own the **commands and context**.

## Command categories

### File / connection
- Open model
- Open PBIP
- Connect Desktop
- Connect endpoint
- Save

### Edit
- Undo
- Redo
- Copy
- Paste
- Delete

### Model
- Add measure
- Add calculated column/table
- Add relationship
- Calculation group
- Role
- Perspective
- Translation

### Engineering
- BPA
- Automate
- Dependencies
- Diagram
- DAX Studio

## Migration rule

For each legacy TE2 command:

1. locate the actual command/service path;
2. wrap it behind one PbiBench command;
3. bind toolbar/menu/context/shortcut to that command;
4. regression test;
5. only then hide/remove duplicate TE2 chrome.

## Legacy fallback

Until all meaningful commands are migrated:

`Tools > Advanced TE2 Commands`

This prevents feature regression while the visible UI becomes cleaner.
