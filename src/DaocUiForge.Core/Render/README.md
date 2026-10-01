# The drawing layer

The port of the drawing layer from the HTML version. It is complete.

- `RenderMath.cs` — `num` / `pt` / `rgba`. `Num` mirrors `parseFloat`, and
  `JsRound` mirrors JavaScript's `Math.round` (which C# does not).
- `TextureCache.cs` — `slice` / `loadTex`: decoded textures with a cache, slices
  drawn straight onto the target surface. (It was called `Slicer` while planning;
  loading and slicing belong together, because the cache sits between them.)
- `NineSlice.cs` — `drawHResize` / `drawVResize` / `drawFullResize` / `drawArea`
  (stretchable areas are tiled, not stretched).
- `FontProvider.cs` — typeface and size. `<Height>` is the **font size in
  pixels**; the line advance follows as `Height × TtfMetrics.LineHeightEm`.
- `TextDrawer.cs` — labels with a 1 px shadow. The first baseline sits one whole
  ascender below the top of the element, the ascender rounded UP to whole pixels.
  No half-leading, no centring in `<Height>` (skill §4).
- `SampleData.cs` — `adapterOf` / `dummyFor`: sample content for fields that only
  the engine fills in the game. It never modifies the XML.
- `RenderContext.cs`, `RenderOptions.cs` — they replace the global variables of the
  original; `RenderedElement` replaces its DOM nodes.
- `Placeholders.cs` — the stand-in shapes that were CSS in the original: stripes
  for "the content only arrives in the game", dashed borders for missing templates,
  gradients in place of missing button images, the title zone and the guide around
  the declared window size.
- `ElementRenderer.cs` + `.Images` / `.Text` / `.Controls` — `renderEl`, all 24
  element types. Split by family, because one file of 1400 lines stops being
  readable. The header comment in `ElementRenderer.cs` explains why the content is
  recorded and only shifted afterwards — please read that before rebuilding
  anything.
- `WindowRenderer.cs` — a whole window with tabs, alignment and frame data. The
  result is a `RenderedWindow` (original lines 1991–2186). Its header comment
  explains the mandatory order of work and why the window is recorded as well.

Drawing is synchronous: in the original this was `async` and needed a `renderSeq`
abort counter. Here the `TextureCache` loads on first access, so the concurrency
falls away (skill §5).

`daoc-ui-editor.html` is always authoritative. Read the geometry and texture rules
in skill §4 before drawing.
