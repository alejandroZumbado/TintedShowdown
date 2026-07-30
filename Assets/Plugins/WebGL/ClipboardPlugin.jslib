// GUIUtility.systemCopyBuffer does not bridge to the real browser clipboard on WebGL —
// it's a plain C# field there, not backed by any native API. The browser's Clipboard API
// (navigator.clipboard.writeText) is the only way to actually copy text, and it requires
// a secure context (HTTPS) plus a user-gesture call stack, which a Button onClick satisfies.
mergeInto(LibraryManager.library, {
  CopyToClipboard: function (textPtr) {
    var text = UTF8ToString(textPtr);
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).catch(function (err) {
        console.warn('[ClipboardPlugin] navigator.clipboard.writeText failed:', err);
      });
    } else {
      console.warn('[ClipboardPlugin] navigator.clipboard unavailable (insecure context or unsupported browser).');
    }
  }
});
