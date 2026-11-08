window.rbacExport = {
  getElementHtml: function (el) {
    try {
      return el ? el.outerHTML : '';
    } catch (e) {
      return '';
    }
  },
  exportToPdf: function (html) {
    try {
      var w = window.open('', '_blank');
      if (!w) {
        alert('Pop-up blocked. Please allow pop-ups to export PDF.');
        return;
      }
      var style = "<style>body{font-family:Arial,Helvetica,sans-serif;margin:20px;} table{border-collapse:collapse;width:100%;} th, td{border:1px solid #ccc;padding:6px;text-align:left;} th{background:#f7f7f7}</style>";
      w.document.open();
      w.document.write("<!doctype html><html><head><meta charset='utf-8'><title>RBAC Matrix</title>" + style + "</head><body>" + html + "<script>window.onload = function(){ setTimeout(function(){ window.print(); }, 200); }<\/script></body></html>");
      w.document.close();
      w.focus();
    } catch (e) {
      console.error(e);
    }
  }
};