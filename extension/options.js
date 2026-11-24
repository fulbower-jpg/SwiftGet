const $ = (id) => document.getElementById(id);

function parseList(text) {
  return text.split(/[\s,;]+/).map((x) => x.trim().toLowerCase().replace(/^\./, '')).filter(Boolean);
}

async function load() {
  const s = await getSettings();
  $('enabled').checked = s.enabled;
  $('interceptAll').checked = s.interceptAll;
  $('extensions').value = s.extensions.join(' ');
  $('minSizeMB').value = s.minSizeMB;
  $('excludedSites').value = s.excludedSites.join('\n');
}

$('save').addEventListener('click', async () => {
  await chrome.storage.sync.set({
    enabled: $('enabled').checked,
    interceptAll: $('interceptAll').checked,
    extensions: parseList($('extensions').value),
    minSizeMB: Math.max(0, Number($('minSizeMB').value) || 0),
    excludedSites: parseList($('excludedSites').value)
  });
  $('saved').textContent = 'Saved';
  setTimeout(() => { $('saved').textContent = ''; }, 1500);
});

$('reset').addEventListener('click', () => { $('extensions').value = DEFAULT_EXTENSIONS.join(' '); });

load();
