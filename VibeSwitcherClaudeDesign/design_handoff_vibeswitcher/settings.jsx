// settings.jsx — Settings, About, FAQ panels
const { useState } = React;

/* ── VS icon reused in About (inline to avoid cross-file dep) ── */
function AboutVSIcon() {
  return (
    <svg width="72" height="72" viewBox="0 0 80 80" fill="none">
      <rect width="80" height="80" rx="20" fill="#13131e"/>
      <rect x="1" y="1" width="78" height="78" rx="19" stroke="rgba(245,130,10,.22)" strokeWidth="1.5" fill="none"/>
      <path d="M19 22L40 54L61 22" stroke="#f5820a" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" fill="none"/>
      <rect x="22" y="61" width="4.5" height="7" rx="2.25" fill="#f5820a" fillOpacity="0.38"/>
      <rect x="29.5" y="56" width="4.5" height="12" rx="2.25" fill="#f5820a" fillOpacity="0.62"/>
      <rect x="37.75" y="58.5" width="4.5" height="9.5" rx="2.25" fill="#f5820a"/>
      <rect x="46" y="56" width="4.5" height="12" rx="2.25" fill="#f5820a" fillOpacity="0.62"/>
      <rect x="53.5" y="61" width="4.5" height="7" rx="2.25" fill="#f5820a" fillOpacity="0.38"/>
    </svg>
  );
}

/* ── Primitives ── */
function Toggle({ on, onChange }) {
  return (
    <button className={`vs-toggle${on ? ' on' : ''}`} onClick={() => onChange(!on)} role="switch" aria-checked={on}>
      <span className="vs-toggle__th" />
    </button>
  );
}

function Row({ label, info, children }) {
  return (
    <div className="vs-row">
      <div className="vs-row__lbl">
        {label}
        {info && <span className="vs-row__info" title={info}>ⓘ</span>}
      </div>
      {children}
    </div>
  );
}

function Section({ title, children }) {
  return (
    <div className="vs-sec">
      <div className="vs-sec__title">{title}</div>
      <div className="vs-sec__body">{children}</div>
    </div>
  );
}

function HKRow({ label, value }) {
  return (
    <div className="vs-hkrow">
      <span className="vs-hkrow__lbl">{label}</span>
      <span className="vs-hkval">{value || 'None'}</span>
      <button className="vs-btn vs-btn--sec vs-btn--sm">Set hotkey</button>
    </div>
  );
}

/* ── Settings Panel ── */
function SettingsPanel({ settings, onChange, onClose }) {
  const s = settings;
  const set = (k, v) => onChange({ ...s, [k]: v });

  return (
    <div className="vs-main" style={{ overflowY: 'auto' }}>
      <div className="vs-panel">
        <div className="vs-panel__hdr">
          <button className="vs-panel__back" onClick={onClose}>
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M10 3L5 8l5 5"/></svg>
          </button>
          <h2 className="vs-panel__title">Settings</h2>
        </div>

        <Section title="Appearance">
          <Row label="Theme">
            <div className="vs-theme-picker">
              {[['Auto','Follow Windows'],['Light','Light'],['Dark','Dark']].map(([k, lbl]) => (
                <button key={k} className={`vs-theme-btn${s.theme === k ? ' on' : ''}`} onClick={() => set('theme', k)}>{lbl}</button>
              ))}
            </div>
          </Row>
        </Section>

        <Section title="Startup">
          <Row label="Start with Windows" info="Launch VibeSwitcher automatically when Windows starts">
            <Toggle on={s.startWithWindows} onChange={v => set('startWithWindows', v)} />
          </Row>
          <Row label="Start minimized to tray" info="Hides the window on launch — only the tray icon is shown">
            <Toggle on={s.startMinimized} onChange={v => set('startMinimized', v)} />
          </Row>
          <Row label="Close to tray" info="Closing the window keeps VibeSwitcher running in the background">
            <Toggle on={s.closeToTray} onChange={v => set('closeToTray', v)} />
          </Row>
        </Section>

        <Section title="Notifications">
          <Row label="Show device switch alerts" info="Display a notification banner when a profile is activated">
            <Toggle on={s.showNotifications} onChange={v => set('showNotifications', v)} />
          </Row>
        </Section>

        <Section title="Schedules">
          <Row label="Use 24-hour clock">
            <Toggle on={s.use24h} onChange={v => set('use24h', v)} />
          </Row>
        </Section>

        <Section title="Tray">
          <Row label="Left-click tray icon to cycle profiles" info="Each left-click steps to the next profile in order">
            <Toggle on={s.leftClickCycles} onChange={v => set('leftClickCycles', v)} />
          </Row>
        </Section>

        <Section title="Devices">
          <Row label="Use legacy sound panel" info="Opens the old Windows Sound control panel instead of modern Settings">
            <Toggle on={s.useLegacySound} onChange={v => set('useLegacySound', v)} />
          </Row>
          <Row label="Show disabled devices in dropdowns">
            <Toggle on={s.showDisabled} onChange={v => set('showDisabled', v)} />
          </Row>
          <Row label="Show disconnected devices in dropdowns">
            <Toggle on={s.showDisconnected} onChange={v => set('showDisconnected', v)} />
          </Row>
        </Section>

        <Section title="Shortcuts">
          <HKRow label="Open / Close VibeSwitcher" value={null} />
          <HKRow label="Mute Microphone" value={null} />
          <HKRow label="Mute Speakers" value={null} />
          <HKRow label="Mute All" value={null} />
        </Section>

        <div className="vs-panel__ftr">
          <button className="vs-btn vs-btn--sec">Open Sound Settings</button>
          <button className="vs-btn vs-btn--sec">View Session Log</button>
          <button className="vs-btn vs-btn--sec">Device Aliases…</button>
        </div>
      </div>
    </div>
  );
}

/* ── About Panel ── */
function AboutPanel({ onClose }) {
  return (
    <div className="vs-main" style={{ overflowY: 'auto' }}>
      <div className="vs-panel">
        <div className="vs-panel__hdr">
          <button className="vs-panel__back" onClick={onClose}>
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M10 3L5 8l5 5"/></svg>
          </button>
          <h2 className="vs-panel__title">About</h2>
        </div>
        <div className="vs-about">
          <AboutVSIcon />
          <div className="vs-about__name">VibeSwitcher</div>
          <div className="vs-about__ver">Version 2.0.0 — Windows 10/11</div>
          <div className="vs-about__desc">
            Manage your Windows audio device profiles with global hotkeys, smart schedules, app triggers, and headset auto-detection.
          </div>
          <div className="vs-about__links">
            <a href="#" className="vs-alink">GitHub</a>
            <a href="#" className="vs-alink">Changelog</a>
            <a href="#" className="vs-alink">License (MIT)</a>
            <a href="#" className="vs-alink">Report a bug</a>
          </div>
        </div>
        <div style={{ marginTop: 28 }}>
          <div className="vs-sec">
            <div className="vs-sec__title">Built with</div>
            <div className="vs-sec__body">
              {[['WPF / .NET 8','Windows Presentation Foundation'],['NAudio','Audio playback and device management'],['CoreAudio','Windows audio COM APIs'],['HID API','Wireless headset detection']].map(([lib, desc]) => (
                <div key={lib} className="vs-row">
                  <div className="vs-row__lbl" style={{ flexDirection:'column', alignItems:'flex-start', gap:2 }}>
                    <span style={{ color:'var(--text)', fontWeight:600 }}>{lib}</span>
                    <span style={{ fontSize:11.5, color:'var(--text-muted)' }}>{desc}</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ── FAQ Panel ── */
const FAQ_ITEMS = [
  { q: 'How do I set a hotkey for a profile?', a: 'Open a profile card and press "Set Hotkey" in the Hotkey field. Press your desired key combination, then confirm. Hotkeys are registered system-wide and work even when VibeSwitcher is minimized.' },
  { q: 'Can profiles switch automatically?', a: 'Yes! Use the App Trigger (↗) button on a card to link an executable — the profile switches when that app launches. Use the Scheduler (⏰) to set time-based rules.' },
  { q: 'What is Auto-Switch?', a: 'Auto-Switch detects when a profile\'s linked audio device physically connects to your computer and automatically activates that profile. Great for USB headsets.' },
  { q: 'How do I create a Playback-only or Recording-only profile?', a: 'Click the + button to add a profile. In the mode selector, choose "Playback" or "Recording". Only the relevant device field will be shown.' },
  { q: 'What does "No Notification" do?', a: 'By default, VibeSwitcher shows a banner notification when a profile is activated. Enabling "No Notification" on a profile suppresses that banner for silent, seamless switching.' },
  { q: 'Why does my profile show a warning?', a: 'A warning appears when a profile\'s assigned device is disconnected or unavailable. Connect the device and the warning clears automatically.' },
  { q: 'Where are my profiles saved?', a: 'Profiles are stored at %APPDATA%\\VibeSwitcher\\config.json. VibeSwitcher writes atomically (temp → rename) with an automatic backup at config.json.bak.' },
  { q: 'How do I set a custom switch sound?', a: 'Press the Sound Switch (♪) button on a profile card to open the sound override dialog. You can choose from built-in tones or pick a custom audio file.' },
];

function FAQPanel({ onClose }) {
  const [open, setOpen] = useState(null);
  return (
    <div className="vs-main" style={{ overflowY: 'auto' }}>
      <div className="vs-panel">
        <div className="vs-panel__hdr">
          <button className="vs-panel__back" onClick={onClose}>
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M10 3L5 8l5 5"/></svg>
          </button>
          <h2 className="vs-panel__title">FAQ</h2>
        </div>
        <div className="vs-sec">
          <div className="vs-sec__body vs-faq">
            {FAQ_ITEMS.map((item, i) => (
              <div key={i} className={`vs-faq__item${open === i ? ' open' : ''}`}>
                <button className="vs-faq__q" onClick={() => setOpen(open === i ? null : i)}>
                  <span>{item.q}</span>
                  <svg width="13" height="13" viewBox="0 0 14 14" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <path d={open === i ? 'M2 9L7 4l5 5' : 'M2 5l5 5 5-5'}/>
                  </svg>
                </button>
                {open === i && <div className="vs-faq__a">{item.a}</div>}
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { SettingsPanel, AboutPanel, FAQPanel });
