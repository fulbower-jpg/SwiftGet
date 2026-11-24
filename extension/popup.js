const statusEl = document.getElementById('status');
const enabledEl = document.getElementById('enabled');

async function refresh() {
  const resp = await sendNative({ cmd: 'ping' });
  if (resp.ok) {
    statusEl.textContent = resp.running ? 'Connected to SwiftGet' : 'Ready (SwiftGet will start when needed)';
    statusEl.className = 'status ok';
  } else {
    statusEl.textContent = 'SwiftGet not found. Install the app and run it once.';
    statusEl.className = 'status bad';
  }
}

(async () => {
  const s = await getSettings();
  enabledEl.checked = s.enabled;
  refresh();
})();

enabledEl.addEventListener('change', () => chrome.storage.sync.set({ enabled: enabledEl.checked }));
document.getElementById('open').addEventListener('click', async () => { await sendNative({ cmd: 'show' }); window.close(); });
document.getElementById('opts').addEventListener('click', () => chrome.runtime.openOptionsPage());
