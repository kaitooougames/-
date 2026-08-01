mergeInto(LibraryManager.library, {
  KaitouCopyText: function (textPtr) {
    var text = UTF8ToString(textPtr);
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).catch(function () {});
      return;
    }
    var area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.focus();
    area.select();
    document.execCommand('copy');
    document.body.removeChild(area);
  },

  KaitouPasteText: function (objectPtr, methodPtr) {
    var objectName = UTF8ToString(objectPtr);
    var methodName = UTF8ToString(methodPtr);
    if (navigator.clipboard && navigator.clipboard.readText) {
      navigator.clipboard.readText().then(function (text) {
        SendMessage(objectName, methodName, text || '');
      }).catch(function () {
        SendMessage(objectName, methodName, '');
      });
      return;
    }
    SendMessage(objectName, methodName, '');
  }
});
