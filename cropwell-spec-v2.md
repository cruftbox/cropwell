# Cropwell Specification

## 1. Overview

Cropwell is a Windows 11 desktop application for preparing images for social media. It opens an image, lets the user crop and resize it, generate alt text with an LLM, and export the result. The design follows the style of Microsoft Paint. The app works with one image at a time.

The build target is a single self-contained .exe that runs on Windows 11 with no Python or other runtime installed. The code should stay clean enough to share on GitHub.

The only LLM use in the app is alt text generation.

## 2. Repository and project requirements

- Private GitHub repository named `cropwell`.
- MIT license.
- Keep the README current: what the app does, how to build the .exe, how to configure API keys, which providers are supported, how to verify each provider (section 20), and the SmartScreen note (section 19).
- Never commit API keys, personal data, or local machine details.
- Code ported from waggle-dance keeps a header comment naming its source file and the waggle-dance license.
- Include a `THIRD_PARTY_NOTICES` file listing the licenses of every library and codec bundled into the .exe. Confirm before release that each bundled component, including any HEIC codec, permits redistribution inside an MIT-licensed binary.

## 3. Implementation platform

Cropwell is implemented in C# with WPF on .NET 10 LTS, targeting Windows 11 x64. Build tooling and helper libraries remain implementation choices, subject to:

- A self-contained, single-file `win-x64` .exe output.
- A Paint-like visual design on Windows 11.
- The provider porting requirements in section 13. The Python provider behavior is ported to C#; Python is not embedded or required at runtime.
- Everything else in this spec.

Image decoding uses Windows Imaging Component (WIC), including the WebP and HEIC codecs installed on the target desktop. Cropwell reports a normal decode error if a required system codec is unavailable.

## 4. Opening images

- An open dialog for image files.
- Drag and drop of a single image file onto the window opens it.
- A file path passed as the first command-line argument opens on launch, so "Open with" works.
- Supported formats: JPG, PNG, WebP, GIF, HEIC, TIFF, BMP, and generally anything a reasonable decoding stack handles.
- One image at a time. Opening a new image replaces the current one, subject to section 10.
- No in-app save. The working image lives in memory for the session.
- A file that fails to decode shows a short in-app error and leaves the current image unchanged.

## 5. Image normalization

Applied once, on open, before the image becomes the working image:

- Apply the EXIF orientation tag, so the pixels are upright, then discard the tag.
- Animated GIF and WebP: use the first frame only.
- Multi-page TIFF: use the first page only.
- Convert to 8-bit RGB, or 8-bit RGBA if the source has transparency. This covers CMYK, 16-bit, palette, and grayscale sources.
- Convert to sRGB using the embedded ICC profile when present (iPhone HEICs are usually Display P3). Images without a profile are treated as sRGB.

## 6. Canvas and zoom

- The image displays in a canvas area.
- Zoom to fit on open.
- Zoom in and out with toolbar buttons, keyboard shortcuts (section 18), and Ctrl+mouse wheel. Zoom range is 10% to 800%.
- A 100% (actual pixels) view.
- Pan the image when zoomed, by scrollbars, by the plain mouse wheel (vertical) and Shift+wheel (horizontal), and by dragging with the middle mouse button or with the Space key held.
- The canvas renders correctly at Windows display scaling of 100%, 125%, 150%, and 200%.
- A status bar shows the working image dimensions in pixels, the zoom level, and, while a crop selection exists, the selection dimensions in pixels.
- The app follows a light theme. Dark mode is optional.

## 7. Crop

Three crop modes:

- Fixed 1:1 (square).
- Fixed 9:16 (portrait). Landscape 16:9 is out of scope.
- Free: a freely sized rectangle with no ratio constraint.

Behavior:

- Choosing a crop mode creates an initial selection: the largest rectangle of that ratio centered in the image. In Free mode, the initial selection is the full image.
- The selection can also be drawn fresh by dragging outside it, moved by dragging inside it, and resized by dragging its edges or handles.
- The selection stays inside the image bounds.
- In fixed-ratio modes the selection keeps its ratio while drawn, moved, or resized. Dimensions snap so the ratio is exact in whole pixels: 1:1 uses equal sides, 9:16 uses 9k by 16k pixels.
- Minimum selection size is 16 pixels on the short side.
- Apply with the toolbar Apply button or Enter. Cancel with Esc, which removes the selection without changing the image.
- Applying a crop bakes the result in: the working image becomes the cropped region.
- Cropping is undoable (section 9).

## 8. Resize

- A dialog taking width and height in whole pixels.
- Aspect ratio is always preserved. The user enters one dimension and the other fills in automatically, rounded to the nearest pixel.
- Valid range is 1 to 16384 pixels per side. Upscaling is allowed within that range.
- Resampling uses Lanczos.
- The dialog rejects invalid input inline instead of closing.
- No presets, no percentage mode, no skew.
- Resizing is undoable (section 9).

## 9. Undo and redo

- Every edit (crop, resize) is undoable and redoable.
- Keep at least 20 steps. If keeping full image snapshots, also cap total history memory at 1.5 GB and drop the oldest steps past that cap, even if fewer than 20 remain. Storing edits as operations replayed from the normalized original is an acceptable alternative.
- Opening a new image clears history.

## 10. Discarding edits

Because there is no in-app save, the app warns before discarding work:

- If the working image has edits that have not been exported since the last edit, opening another image or closing the app shows a confirmation with Export, Discard, and Cancel.
- An image with no edits never prompts.

## 11. Export

- Export only. The app never overwrites the source file. The export dialog refuses the source file's path with a short message.
- The export dialog lets the user pick the destination, with an editable filename.
- Default folder: a valid export folder configured in Settings; otherwise the last export folder used in this session, or the source file's folder for the first export. An empty setting uses this fallback behavior.
- Default filename: `<source name>-cropwell.<ext>`.
- Formats: PNG (default), WebP, JPG, GIF. The extension follows the chosen format.
- If the destination file exists, confirm before overwriting.
- No quality controls. Fixed defaults:
  - PNG: lossless.
  - WebP: lossy, quality 90.
  - JPG: quality 90, 4:2:0 subsampling.
  - GIF: adaptive palette of 256 colors with dithering.
- Transparency: PNG, WebP, and GIF keep it. JPG flattens onto white.
- Strip all metadata on export, including EXIF and GPS. Embed an sRGB ICC profile in formats that support one.
- Export operates on the current working image.

## 12. Alt text

- A toolbar button labeled Generate alt text.
- The call runs against the current working image (post crop and resize), not the original file.
- The call does not block the UI. While it runs, the button is disabled and the alt text window shows a busy indicator and a Cancel button. Editing stays available.
- The result appears in a small, non-modal in-app window containing:
  - The text, editable.
  - A copy icon. Clicking it puts the current text on the clipboard.
  - A Regenerate button.
  - The provider label and model used, in small text.
- If the working image changes after generation, the window marks the text as possibly out of date. It does not regenerate automatically.
- No persistence of generated text.
- Errors appear in the same window (section 17).

## 13. Alt text providers

Port the provider code from the waggle-dance project (https://github.com/cruftbox/waggle-dance) instead of writing new clients. Port:

- `waggle_dance/providers/base.py`: shared request logic, key handling, and `with_retries`.
- `waggle_dance/providers/anthropic.py`: Claude.
- `waggle_dance/providers/openai.py`: the OpenAI Responses provider. It serves both ChatGPT and Muse Spark.
- `waggle_dance/providers/gemini.py`: Gemini.
- `waggle_dance/providers/mock.py`: the mock provider, used for testing (section 20).

The waggle-dance providers send text only. Extending them to send an image part is new code and part of this work. Encode images per provider:

- Claude (Anthropic Messages API): a base64 image part in the message content.
- OpenAI Responses (ChatGPT and Muse Spark): an `input_image` part with a `data:` URL.
- Gemini: an inline image part with `data` and `mime_type`.

Cropwell does not read waggle-dance YAML config. The Muse Spark values from the `muse` block in `config/models.example.yaml` are carried over as the defaults in the table below. All configuration comes from the settings dialog (section 14).

Muse Spark specifics, from waggle-dance `docs/PROVIDERS.md`:

- Muse Spark runs through the OpenAI Responses provider with `base_url: https://api.meta.ai/v1` and the key `META_API_KEY`. There is no separate Meta client, and none should be written.
- Muse Spark always reasons. A `reasoning.effort` value of `none` returns a 400 on the Meta endpoint. Omit `reasoning.effort` or use the model default.
- Reasoning tokens count against `max_output_tokens`.
- Confirm during development that the Muse Spark endpoint accepts `input_image` parts.

Provider interface. Wrap the ported provider classes behind a single interface:

```python
class AltTextProvider(Protocol):
    display_name: str

    async def alt_text(
        self,
        *,
        media_type: str,
        image_b64: str,
        instruction: str,
    ) -> str:
        ...
```

`instruction` is the system instruction from section 16. Each provider maps it to its API's system field: `system` for Anthropic Messages, `instructions` for OpenAI Responses, `system_instruction` for Gemini. The user message text is fixed and built inside the provider.

The UI runs the async call off the UI thread (a worker thread with its own event loop, or an async bridge for the chosen framework). Cancel abandons the call and discards any late result.

Provider defaults. Every model ID and base URL is editable in settings, because vendors change them. Verify each default model ID against the vendor's current documentation before each release.

| Provider label | Provider type | Default model | API key environment variable | Base URL | Max output tokens | Reasoning |
|---|---|---|---|---|---|---|
| Claude | `anthropic` | `claude-opus-5-5` | `ANTHROPIC_API_KEY` | `https://api.anthropic.com` | 1024 | Extended thinking off |
| ChatGPT | `openai_responses` | `gpt-6-sol` | `OPENAI_API_KEY` | `https://api.openai.com` | 4000 | `reasoning.effort` low |
| Gemini | `gemini` | `gemini-3.8-flash` | `GEMINI_API_KEY` | `https://generativelanguage.googleapis.com` | 4000 | `thinking_level` low |
| Muse Spark | `openai_responses` | `muse-spark-1.3` | `META_API_KEY` | `https://api.meta.ai/v1` | 16000 | Omit `reasoning.effort` |

Mock provider. When the environment variable `CROPWELL_MOCK=1` is set at launch, a fifth entry, Mock, appears in the provider dropdown. It needs no key and returns a fixed sentence after a short delay. It never appears otherwise.

## 14. Settings and API keys

- Settings includes an optional export folder. Saving rejects a non-empty path that is not an existing folder.

A settings dialog with:

- A provider dropdown: Claude, ChatGPT, Gemini, Muse Spark (plus Mock, per section 13).
- A model name field.
- A base URL field, pre-filled with the provider default and editable for compatible alternate endpoints.
- An API key field, with a Validate key button.
- A line showing the key source for the selected provider: stored key, environment variable, or none.
- Save and cancel actions.

Behavior:

- Each provider has its own stored model, base URL, and key. Changing the dropdown loads that provider's values into the fields.
- The provider selected when the user saves is the active provider for alt text.
- Cancel discards all changes made in the dialog, for every provider.

Persistence:

- Non-secret settings (active provider, per-provider model and base URL, window size and position) are stored in `%APPDATA%\Cropwell\settings.json`.
- API keys are stored in Windows Credential Manager, one entry per provider. Keys are never written to `settings.json`, logs, or any other file.

API key handling:

- Use the stored key first.
- Fall back to the environment variable for the selected provider.
- Environment variable names: `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`, `META_API_KEY`.
- After a key is saved, the field shows it masked. Typing in the field replaces the stored key on save.
- Validate key sends a real request containing a generated 1×1 image through the selected provider's configured model and base URL. It tests a pending typed key without saving it, reports success or a short provider error in the dialog, and may incur a small provider charge.
- If the selected provider has no key, show a short in-app error in the alt text window when the user requests alt text.

## 15. Image encoding for API calls

The API copy is built from the working image and used only for the call. Export always uses the full working image.

1. Flatten any transparency onto white.
2. If the long edge exceeds 2048 pixels, scale it down to 2048, preserving aspect ratio.
3. Encode as PNG. If the base64-encoded payload is 5 MB or less, send it with media type `image/png`.
4. Otherwise encode as JPEG quality 85. If the base64 payload is 5 MB or less, send it with media type `image/jpeg`.
5. Otherwise scale the image to 0.75 of its current dimensions and repeat step 4.
6. If the long edge falls below 512 pixels and the payload is still over 5 MB, stop and show a short error.

The 5 MB limit is measured on the base64 string, not the raw bytes.

## 16. Alt text prompt

System instruction, exactly:

```text
You write accessible alt text. Return one plain sentence that describes what a person needs to see in the image.
```

User message, exactly one, containing:

- The image part.
- The text `Write alt text for this image.`

Trim whitespace from the returned text before showing it. If the trimmed text is empty, treat it as an error.

## 17. Error handling and logging

Errors:

- Provider failures surface as a short, UI-safe `ProviderError` message.
- Retry with the ported `with_retries` helper: up to 3 attempts with backoff, only for HTTP 429, HTTP 5xx, timeouts, and connection errors. HTTP 400, 401, and 403 fail immediately.
- Request timeout is 120 seconds per attempt.
- On final failure, show the short message in the alt text window.
- Missing key, network errors, and API errors all display the same way. No crash dialogs.
- An unexpected exception anywhere in the app is logged and shown as a short in-app message. The app does not exit.

Logging:

- Write a rotating log to `%LOCALAPPDATA%\Cropwell\logs\cropwell.log`, capped at 1 MB with 3 backups.
- Log app version, file open and export events (file names only), provider name, model, HTTP status, and error details.
- Never log API keys, request headers, image data, or generated alt text.

## 18. Keyboard shortcuts

| Action | Shortcut |
|---|---|
| Open | Ctrl+O |
| Export | Ctrl+E |
| Undo | Ctrl+Z |
| Redo | Ctrl+Y and Ctrl+Shift+Z |
| Resize | Ctrl+R |
| Generate alt text | Ctrl+G |
| Zoom in | Ctrl+= |
| Zoom out | Ctrl+- |
| Zoom to fit | Ctrl+0 |
| Actual pixels (100%) | Ctrl+1 |
| Apply crop | Enter |
| Cancel crop | Esc |
| Settings | Ctrl+, |

Every toolbar control is reachable by keyboard and has an accessible name for screen readers. Tooltips show the shortcut.

## 19. Build and distribution

- Pin the .NET SDK version in `global.json` and pin every external package version.
- The .exe has an app icon and Windows version info (product name, version, MIT license note).
- The build is a single command documented in the README.
- The .exe is unsigned for now. The README notes that Windows SmartScreen will warn on first launch and how to proceed.
- No console window appears when the .exe runs.

## 20. Testing

- Unit tests, run without network access:
  - Crop math: ratio snapping, bounds clamping, minimum size.
  - Resize: aspect fill and rounding.
  - Normalization: EXIF orientation, mode conversion, first frame of animated input.
  - API encoding: the reduction loop in section 15, including the 512 pixel stop.
  - Export: format defaults, JPG flattening, metadata stripping, refusal of the source path.
  - Settings: per-provider load and save, key source precedence.
- Use the mock provider to exercise the alt text flow, including cancel, regenerate, and error display, with zero API cost.
- Each real provider needs a live key to verify end to end. Verify all four during development with keys supplied locally, and document the verification steps in the README without committing any keys.

## 21. Out of scope

- Multiple images, tabs, or batch processing.
- Landscape 16:9 and other crop ratios beyond those in section 7.
- Resize presets, percentage resize, and skew.
- Rotation, flipping, and any other edits beyond crop and resize.
- Export quality controls.
- Pasting images from the clipboard.
- Alt text persistence.
- Any LLM use beyond alt text.
- In-app save or overwriting the source file.
- Code signing.
