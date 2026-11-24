importScripts('common.js');

const handled = new Set();

function extOf(s) {
  if (!s) return '';
  s = s.split('#')[0].split('?')[0];
  const name = s.split(/[\\/]/).pop();
  const i = name.lastIndexOf('.');
  return i > 0 ? name.slice(i + 1).toLowerCase() : '';
}

function hostOf(url) {
  try { return new URL(url).hostname.toLowerCase(); } catch (e) { return ''; }
}

async function cookieHeader(url) {
  try {
    const list = await chrome.cookies.getAll({ url });
    return list.map((c) => c.name + '=' + c.value).join('; ');
  } catch (e) {
    return '';
  }
}

function setBadge(ok, tip) {
  chrome.action.setBadgeText({ text: ok ? '' : '!' });
  chrome.action.setBadgeBackgroundColor({ color: '#d93025' });
  chrome.action.setTitle({ title: ok ? 'SwiftGet' : 'SwiftGet: ' + tip });
}

async function sendToSwiftGet(url, referrer) {
  const resp = await sendNative({
    cmd: 'add',
    url,
    referer: referrer || '',
    cookie: await cookieHeader(url),
    userAgent: navigator.userAgent
  });
  setBadge(!!resp.ok, resp.error || 'not reachable. Is SwiftGet installed and run once?');
  return resp;
}

function shouldIntercept(item, url, s) {
  if (!/^https?:/i.test(url)) return false;               // blob:, data:, file: stay in the browser
  const host = hostOf(url);
  if (s.excludedSites.some((d) => d && (host === d || host.endsWith('.' + d)))) return false;
  if (s.minSizeMB > 0 && item.fileSize > 0 && item.fileSize < s.minSizeMB * 1024 * 1024) return false;

  const ext = extOf(url) || extOf(item.filename);
  if (ext && s.extensions.includes(ext)) return true;
  if (s.interceptAll) {
    const mime = (item.mime || '').toLowerCase();
    return !mime.startsWith('text/html');
  }
  return false;
}

chrome.downloads.onCreated.addListener(async (item) => {
  try {
    const s = await getSettings();
    if (!s.enabled || handled.has(item.id)) return;
    const url = item.finalUrl || item.url;
    if (!shouldIntercept(item, url, s)) return;
    handled.add(item.id);

    const resp = await sendToSwiftGet(url, item.referrer);
    if (resp.ok) {
      // SwiftGet has it: remove the browser's own copy.
      chrome.downloads.cancel(item.id, () => {
        void chrome.runtime.lastError;
        chrome.downloads.erase({ id: item.id }, () => void chrome.runtime.lastError);
      });
    }
    // If SwiftGet could not be reached, the browser simply keeps downloading it.
  } catch (e) {
    console.warn('SwiftGet: download hook failed', e);
  }
});

function buildMenus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: 'sg-link', title: 'Download with SwiftGet', contexts: ['link'] });
    chrome.contextMenus.create({ id: 'sg-media', title: 'Download with SwiftGet', contexts: ['image', 'video', 'audio'] });
  });
}

chrome.runtime.onInstalled.addListener(buildMenus);
chrome.runtime.onStartup.addListener(buildMenus);

chrome.contextMenus.onClicked.addListener(async (info) => {
  const url = info.linkUrl || info.srcUrl;
  if (!url || !/^https?:/i.test(url)) return;
  await sendToSwiftGet(url, info.pageUrl);
});
