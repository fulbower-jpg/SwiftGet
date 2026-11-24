// Shared by the background worker, popup and options page.
const HOST = 'com.swiftget.host';

const DEFAULT_EXTENSIONS = [
  'zip','rar','7z','gz','tgz','tar','bz2','xz','iso','img','bin','cab',
  'exe','msi','apk','dmg','jar','deb','rpm','msix','appx',
  'pdf','doc','docx','xls','xlsx','ppt','pptx','epub','mobi',
  'mp3','flac','wav','m4a','aac','ogg','wma',
  'mp4','mkv','avi','mov','wmv','flv','webm','m4v','3gp','ts'
];

const DEFAULTS = {
  enabled: true,
  interceptAll: false,   // also catch file types that are not in the list
  minSizeMB: 0,          // ignore downloads smaller than this (0 = no limit)
  extensions: DEFAULT_EXTENSIONS,
  excludedSites: []      // host names that stay with the browser's own downloader
};

function getSettings() {
  return chrome.storage.sync.get(DEFAULTS);
}

// One-shot message to SwiftGet through its native host. Never rejects.
function sendNative(msg) {
  return new Promise((resolve) => {
    try {
      chrome.runtime.sendNativeMessage(HOST, msg, (resp) => {
        if (chrome.runtime.lastError) {
          resolve({ ok: false, error: chrome.runtime.lastError.message });
        } else {
          resolve(resp || { ok: false, error: 'No answer from SwiftGet' });
        }
      });
    } catch (e) {
      resolve({ ok: false, error: String(e) });
    }
  });
}
