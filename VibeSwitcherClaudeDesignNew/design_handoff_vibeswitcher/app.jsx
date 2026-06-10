// app.jsx — Splash, MainApp, ProfileDetailModal, root render
const { useState, useCallback } = React;
const { ProfileCard, ProfileIcon, VSSplashIcon, SettingsPanel, AboutPanel, FAQPanel } = window;

/* ── Mock Data ── */
const INIT_PROFILES = [
  { id:'1', name:'Speaker',     mode:'Both',      iconType:'speaker', playbackDevice:'SONOS',             recordingDevice:'Camera Mic',     hotkey:'PageUp', isPinned:true,  isActive:true,  silent:false, hasTrigger:false, autoSwitch:false, hasSchedule:false, hasSoundSwitch:false, notes:'' },
  { id:'2', name:'Headset',     mode:'Both',      iconType:'headset', playbackDevice:'Logitech Headset',  recordingDevice:'Logitech Mic',   hotkey:'Next',   isPinned:false, isActive:false, silent:false, hasTrigger:false, autoSwitch:false, hasSchedule:false, hasSoundSwitch:false, notes:'' },
  { id:'3', name:'Stream Setup',mode:'Recording', iconType:'stream',  playbackDevice:null,                recordingDevice:'Blue Yeti',      hotkey:null,     isPinned:false, isActive:false, silent:false, hasTrigger:true,  autoSwitch:false, hasSchedule:false, hasSoundSwitch:true,  notes:'Used for live streaming sessions' },
  { id:'4', name:'Gaming',      mode:'Playback',  iconType:'gaming',  playbackDevice:'SteelSeries Arctis',recordingDevice:null,             hotkey:'F9',     isPinned:false, isActive:false, silent:true,  hasTrigger:true,  autoSwitch:true,  hasSchedule:false, hasSoundSwitch:false, notes:'' },
  { id:'5', name:'Meeting',     mode:'Both',      iconType:'meeting', playbackDevice:'Dell Monitor Audio',recordingDevice:'Webcam Mic',     hotkey:'F10',    isPinned:false, isActive:false, silent:false, hasTrigger:false, autoSwitch:false, hasSchedule:true,  hasSoundSwitch:false, notes:'Mon–Fri 9 am – 5 pm' },
];

const PB_DEVS  = ['SONOS','Logitech Headset','SteelSeries Arctis','Dell Monitor Audio','Realtek HD Audio','AirPods Pro (Stereo)'];
const REC_DEVS = ['Camera Mic','Logitech Mic','Blue Yeti','Webcam Mic','Realtek Mic Array','AirPods Pro (Mono)'];

const FILTER_CHIPS = [
  {k:'playback',  lbl:'Playback only'},
  {k:'recording', lbl:'Recording only'},
  {k:'both',      lbl:'Both devices'},
  {k:'pinned',    lbl:'★ Pinned'},
  {k:'active',    lbl:'✓ Active'},
  {k:'silent',    lbl:'Silent'},
  {k:'hotkey',    lbl:'Has hotkey'},
  {k:'scheduled', lbl:'Scheduled'},
  {k:'trigger',   lbl:'Has trigger'},
  {k:'sound',     lbl:'Has sound'},
];

/* ── Splash animation CSS ── */
const SPLASH_ANIM_CSS = `
  /* V chevron: rises up then slams down into bars */
  @keyframes vsVPress {
    0%, 26% { transform: translateY(0px); }
    39%     { transform: translateY(-16px); }
    52%     { transform: translateY(11px); }
    64%     { transform: translateY(-5px); }
    76%     { transform: translateY(1.5px); }
    88%     { transform: translateY(-0.5px); }
    100%    { transform: translateY(0px); }
  }
  /* Center bar — biggest, fastest reaction */
  @keyframes vsBarC {
    0%, 48% { transform: scaleY(1); }
    55%     { transform: scaleY(0.28); }
    65%     { transform: scaleY(2.6); }
    74%     { transform: scaleY(0.62); }
    83%     { transform: scaleY(1.6); }
    92%     { transform: scaleY(0.88); }
    100%    { transform: scaleY(1); }
  }
  /* Inner bars (2 & 4) — slightly delayed, medium bounce */
  @keyframes vsBarI {
    0%, 51% { transform: scaleY(1); }
    58%     { transform: scaleY(0.38); }
    68%     { transform: scaleY(2.1); }
    77%     { transform: scaleY(0.7); }
    86%     { transform: scaleY(1.42); }
    94%     { transform: scaleY(0.92); }
    100%    { transform: scaleY(1); }
  }
  /* Outer bars (1 & 5) — most delayed, smallest reaction */
  @keyframes vsBarO {
    0%, 54% { transform: scaleY(1); }
    61%     { transform: scaleY(0.5); }
    71%     { transform: scaleY(1.7); }
    80%     { transform: scaleY(0.8); }
    89%     { transform: scaleY(1.25); }
    96%     { transform: scaleY(0.96); }
    100%    { transform: scaleY(1); }
  }
  .vs-v-anim {
    animation: vsVPress 1.6s cubic-bezier(0.4, 0, 0.2, 1) forwards;
    transform-box: fill-box;
    transform-origin: 50% 50%;
  }
  .vs-bar-c {
    animation: vsBarC 1.6s ease-in-out forwards;
    transform-box: fill-box;
    transform-origin: 50% 100%;
  }
  .vs-bar-i {
    animation: vsBarI 1.6s ease-in-out forwards;
    transform-box: fill-box;
    transform-origin: 50% 100%;
  }
  .vs-bar-o {
    animation: vsBarO 1.6s ease-in-out forwards;
    transform-box: fill-box;
    transform-origin: 50% 100%;
  }

  /* Looping equalizer — bars clipped at y=62, safely below V tip (y=54) */
  @keyframes vsEq1 {
    0%,100% { transform: scaleY(0.6); }
    50%     { transform: scaleY(2.4); }
  }
  @keyframes vsEq2 {
    0%,100% { transform: scaleY(0.7); }
    38%     { transform: scaleY(1.85); }
    72%     { transform: scaleY(0.55); }
  }
  @keyframes vsEq3 {
    0%,100% { transform: scaleY(0.5); }
    30%     { transform: scaleY(1.7); }
    65%     { transform: scaleY(0.75); }
  }
  @keyframes vsEq4 {
    0%,100% { transform: scaleY(0.65); }
    42%     { transform: scaleY(1.9); }
    78%     { transform: scaleY(0.6); }
  }
  @keyframes vsEq5 {
    0%,100% { transform: scaleY(0.55); }
    55%     { transform: scaleY(2.2); }
  }
  .vs-eq-1 { animation: vsEq1 0.52s ease-in-out infinite; transform-box:fill-box; transform-origin:50% 100%; }
  .vs-eq-2 { animation: vsEq2 0.41s ease-in-out infinite; transform-box:fill-box; transform-origin:50% 100%; }
  .vs-eq-3 { animation: vsEq3 0.36s ease-in-out infinite; transform-box:fill-box; transform-origin:50% 100%; }
  .vs-eq-4 { animation: vsEq4 0.45s ease-in-out infinite; transform-box:fill-box; transform-origin:50% 100%; }
  .vs-eq-5 { animation: vsEq5 0.48s ease-in-out infinite; transform-box:fill-box; transform-origin:50% 100%; }
`;

/* ── Animated Splash Icon (V presses down, bars wave, then loop) ── */
function AnimatedSplashIcon({ size = 96, phase = 'idle' }) {
  const isAnim = phase === 'anim';
  const isLoop = phase === 'loop' || phase === 'exit';
  return (
    <>
      {(isAnim || isLoop) && <style>{SPLASH_ANIM_CSS}</style>}
      <svg width={size} height={size} viewBox="0 0 80 80" fill="none">
        <rect width="80" height="80" rx="20" fill="#13131e"/>
        <rect x="1" y="1" width="78" height="78" rx="19"
              stroke="rgba(245,130,10,.22)" strokeWidth="1.5" fill="none"/>
        <defs>
          <radialGradient id="vsGlowS" cx="50%" cy="75%" r="55%">
            <stop offset="0%" stopColor="#f5820a" stopOpacity="0.09"/>
            <stop offset="100%" stopColor="#f5820a" stopOpacity="0"/>
          </radialGradient>
          <clipPath id="vsBarClip">
            <rect x="14" y="62" width="54" height="18"/>
          </clipPath>
        </defs>
        <rect width="80" height="80" rx="20" fill="url(#vsGlowS)"/>

        {/* V chevron — animates on press only */}
        <path
          className={isAnim ? 'vs-v-anim' : ''}
          d="M19 22L40 54L61 22"
          stroke="#f5820a" strokeWidth="4"
          strokeLinecap="round" strokeLinejoin="round" fill="none"
        />

        {/* Bars repositioned lower (bottom at y=76), clipped at y=62 */}
        <g clipPath="url(#vsBarClip)">
          {/* Bar 1 — outer left */}
          <rect
            className={isAnim ? 'vs-bar-o' : isLoop ? 'vs-eq-1' : ''}
            x="22" y="71" width="4.5" height="5" rx="2.25"
            fill="#f5820a" fillOpacity="0.38"/>
          {/* Bar 2 — inner left */}
          <rect
            className={isAnim ? 'vs-bar-i' : isLoop ? 'vs-eq-2' : ''}
            x="29.5" y="69" width="4.5" height="7" rx="2.25"
            fill="#f5820a" fillOpacity="0.62"/>
          {/* Bar 3 — center */}
          <rect
            className={isAnim ? 'vs-bar-c' : isLoop ? 'vs-eq-3' : ''}
            x="37.75" y="68" width="4.5" height="8" rx="2.25"
            fill="#f5820a"/>
          {/* Bar 4 — inner right */}
          <rect
            className={isAnim ? 'vs-bar-i' : isLoop ? 'vs-eq-4' : ''}
            x="46" y="69" width="4.5" height="7" rx="2.25"
            fill="#f5820a" fillOpacity="0.62"/>
          {/* Bar 5 — outer right */}
          <rect
            className={isAnim ? 'vs-bar-o' : isLoop ? 'vs-eq-5' : ''}
            x="53.5" y="71" width="4.5" height="5" rx="2.25"
            fill="#f5820a" fillOpacity="0.38"/>
        </g>
      </svg>
    </>
  );
}

/* ── Splash Screen ── */
function SplashScreen({ onEnter }) {
  const [phase, setPhase] = useState('idle'); // idle | anim | loop | exit

  React.useEffect(() => {
    const t1 = setTimeout(() => setPhase('anim'), 420);   // start V press
    const t2 = setTimeout(() => setPhase('loop'), 2060);  // bars settle → start looping eq
    const t3 = setTimeout(() => setPhase('exit'), 3400);  // fade out
    const t4 = setTimeout(onEnter, 3850);                  // load app
    return () => [t1, t2, t3, t4].forEach(clearTimeout);
  }, []);

  return (
    <div className={`vs-splash${phase === 'exit' ? ' vs-splash--exit' : ''}`}>
      <div className="vs-splash__icon">
        <AnimatedSplashIcon size={96} phase={phase} />
      </div>
      <div className="vs-splash__name">VibeSwitcher</div>
      <div className="vs-splash__tagline">Manage your device profiles and hotkeys</div>
    </div>
  );
}

/* ── Profile Detail Modal ── */
function ProfileDetailModal({ profile, onSave, onClose }) {
  const [form, setForm] = useState({ ...profile });
  const set = (k, v) => setForm(f => ({ ...f, [k]: v }));

  const modeMap = { Both:'both', Playback:'pb', Recording:'rec' };

  const ICON_TYPES = ['speaker','headset','stream','gaming','meeting'];

  return (
    <div className="vs-overlay" onClick={onClose}>
      <div className="vs-modal" onClick={e => e.stopPropagation()}>

        {/* Header */}
        <div className="vs-modal__hdr">
          <div className="vs-modal__ico">
            <ProfileIcon iconType={form.iconType} name={form.name} />
          </div>
          <div className="vs-modal__title-area">
            <input
              className="vs-modal__name"
              value={form.name}
              onChange={e => set('name', e.target.value)}
              placeholder="Profile name"
            />
            <div className="vs-modal__modes">
              {['Both','Playback','Recording'].map(m => (
                <button
                  key={m}
                  className={`vs-mpill vs-mpill--${modeMap[m]}${form.mode === m ? ' on' : ''}`}
                  onClick={() => set('mode', m)}
                >{m === 'Both' ? 'Both Devices' : m}</button>
              ))}
            </div>
          </div>
          <button className="vs-modal__x" onClick={onClose} aria-label="Close">
            <svg width="14" height="14" viewBox="0 0 14 14" stroke="currentColor" strokeWidth="2" strokeLinecap="round" fill="none"><line x1="2" y1="2" x2="12" y2="12"/><line x1="12" y1="2" x2="2" y2="12"/></svg>
          </button>
        </div>

        {/* Body */}
        <div className="vs-modal__body">

          {/* Notes */}
          <div className="vs-frow">
            <label className="vs-flabel">Notes</label>
            <textarea
              className="vs-fi"
              value={form.notes || ''}
              onChange={e => set('notes', e.target.value)}
              placeholder="Add a note about this profile…"
              rows="2"
            />
          </div>

          {/* Playback */}
          {(form.mode === 'Both' || form.mode === 'Playback') && (
            <div className="vs-frow">
              <label className="vs-flabel"><span className="vs-dot vs-dot--pb" />Playback device</label>
              <div className="vs-devrow">
                <select className="vs-fi" value={form.playbackDevice || ''} onChange={e => set('playbackDevice', e.target.value)}>
                  <option value="">— None —</option>
                  {PB_DEVS.map(d => <option key={d} value={d}>{d}</option>)}
                </select>
                <button className="vs-btn vs-btn--sec vs-btn--sm">Test</button>
              </div>
            </div>
          )}

          {/* Recording */}
          {(form.mode === 'Both' || form.mode === 'Recording') && (
            <div className="vs-frow">
              <label className="vs-flabel"><span className="vs-dot vs-dot--rec" />Recording device</label>
              <div className="vs-devrow">
                <select className="vs-fi" value={form.recordingDevice || ''} onChange={e => set('recordingDevice', e.target.value)}>
                  <option value="">— None —</option>
                  {REC_DEVS.map(d => <option key={d} value={d}>{d}</option>)}
                </select>
                <button className="vs-btn vs-btn--sec vs-btn--sm">Test</button>
              </div>
            </div>
          )}

          {/* Hotkey */}
          <div className="vs-frow">
            <label className="vs-flabel">Hotkey</label>
            <div className="vs-devrow">
              <div className="vs-fi" style={{ display:'flex', alignItems:'center', minHeight:38 }}>
                {form.hotkey
                  ? <code style={{ fontFamily:"'Courier New',monospace", fontSize:13, color:'var(--text)' }}>{form.hotkey}</code>
                  : <span style={{ color:'var(--text-muted)', fontStyle:'italic' }}>None assigned</span>
                }
              </div>
              <button className="vs-btn vs-btn--sec vs-btn--sm">Set Hotkey</button>
            </div>
          </div>

          {/* Icon */}
          <div className="vs-frow">
            <label className="vs-flabel">Icon</label>
            <div className="vs-devrow" style={{ alignItems:'flex-start' }}>
              <div className="vs-icrow">
                {ICON_TYPES.map(t => (
                  <button key={t} className={`vs-ipick${form.iconType === t ? ' on' : ''}`} onClick={() => set('iconType', t)} title={t}>
                    <ProfileIcon iconType={t} name={t} />
                  </button>
                ))}
              </div>
              <button className="vs-btn vs-btn--sec vs-btn--sm" style={{ marginTop:6 }}>Pick File</button>
            </div>
          </div>

        </div>

        {/* Footer */}
        <div className="vs-modal__ftr">
          <button className="vs-btn vs-btn--sec" onClick={onClose}>Cancel</button>
          <button className="vs-btn vs-btn--pri" onClick={() => { onSave(form); onClose(); }}>Save Changes</button>
        </div>

      </div>
    </div>
  );
}

/* ── Title Bar ── */
function TitleBar({ onClose }) {
  return (
    <div className="vs-titlebar">
      <VSSplashIcon size={14} />
      <span className="vs-titlebar__text">VibeSwitcher</span>
      <div className="vs-titlebar__controls">
        <button className="vs-titlebar__btn">─</button>
        <button className="vs-titlebar__btn">□</button>
        <button className="vs-titlebar__btn vs-titlebar__btn--close" onClick={onClose}>✕</button>
      </div>
    </div>
  );
}

/* ── Top Nav ── */
function TopNav({ screen, onScreen, filterOpen, onFilter, filterCount }) {
  const [q, setQ] = useState('');
  return (
    <nav className="vs-nav">
      <div className="vs-nav__logo" onClick={() => onScreen('main')}>
        <VSSplashIcon size={26} />
        <span className="vs-nav__logo-text">Vibe<span>Switcher</span></span>
      </div>

      <div className="vs-nav__div" />

      {screen === 'main' && (
        <>
          <div className="vs-nav__search">
            <svg width="13" height="13" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="7" cy="7" r="5"/><path d="M11 11l3 3"/></svg>
            <input placeholder="Search profiles…" value={q} onChange={e => setQ(e.target.value)} />
          </div>
          <button className={`vs-nav__filter${filterOpen ? ' on' : ''}`} onClick={onFilter}>
            <svg width="13" height="13" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round"><path d="M1.5 3.5h13M4 8h8M7 12.5h2"/></svg>
            Filters
            {filterCount > 0 && <span className="vs-nav__badge">{filterCount}</span>}
          </button>
        </>
      )}

      <div className="vs-nav__spacer" />

      <button className={`vs-nav__btn${screen === 'faq' ? ' on' : ''}`} onClick={() => onScreen(screen === 'faq' ? 'main' : 'faq')} title="FAQ">
        <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round"><circle cx="8" cy="8" r="6.5"/><path d="M6 6.5a2 2 0 1 1 2 1.5v1.2"/><circle cx="8" cy="11.5" r=".75" fill="currentColor" stroke="none"/></svg>
      </button>

      <button className={`vs-nav__btn${screen === 'about' ? ' on' : ''}`} onClick={() => onScreen(screen === 'about' ? 'main' : 'about')} title="About">
        <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round"><circle cx="8" cy="8" r="6.5"/><line x1="8" y1="7.5" x2="8" y2="11.2"/><circle cx="8" cy="5.5" r=".75" fill="currentColor" stroke="none"/></svg>
      </button>

      <button className={`vs-nav__btn${screen === 'settings' ? ' on' : ''}`} onClick={() => onScreen(screen === 'settings' ? 'main' : 'settings')} title="Settings">
        <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round"><circle cx="8" cy="8" r="2.4"/><path d="M8 1.5v1.3M8 13.2v1.3M1.5 8h1.3M13.2 8h1.3M3.2 3.2l.9.9M11.9 11.9l.9.9M3.2 12.8l.9-.9M11.9 4.1l.9-.9"/></svg>
      </button>

      <div className="vs-nav__div" />

      <button className="vs-nav__add" onClick={() => alert('➕  Add New Profile — opens the profile type wizard')}>
        <svg width="13" height="13" viewBox="0 0 13 13" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><line x1="6.5" y1="1.5" x2="6.5" y2="11.5"/><line x1="1.5" y1="6.5" x2="11.5" y2="6.5"/></svg>
        New Profile
      </button>
    </nav>
  );
}

/* ── Filter Bar ── */
function FilterBar({ active, onToggle, open }) {
  return (
    <div className={`vs-filters${open ? ' on' : ''}`}>
      {FILTER_CHIPS.map(c => (
        <button key={c.k} className={`vs-fchip${active.includes(c.k) ? ' on' : ''}`} onClick={() => onToggle(c.k)}>{c.lbl}</button>
      ))}
      {active.length > 0 && (
        <button className="vs-fchip vs-fchip--clear" onClick={() => active.forEach(k => onToggle(k))}>✕ Clear</button>
      )}
    </div>
  );
}

/* ── Main App ── */
function MainApp() {
  const [profiles, setProfiles]     = useState(INIT_PROFILES);
  const [screen, setScreen]         = useState('main');
  const [expandedId, setExpandedId] = useState(null);
  const [filterOpen, setFilterOpen] = useState(false);
  const [filters, setFilters]       = useState([]);
  const [settings, setSettings]     = useState({
    theme:'Dark', startWithWindows:true, startMinimized:false,
    closeToTray:true, showNotifications:true, use24h:false,
    leftClickCycles:false, useLegacySound:false,
    showDisabled:true, showDisconnected:true,
  });

  const handleScreen = useCallback(s => { setScreen(s); if (s !== 'main') setExpandedId(null); }, []);

  const handleToggleFilter = useCallback(k => {
    setFilters(prev => prev.includes(k) ? prev.filter(x => x !== k) : [...prev, k]);
  }, []);

  const handleToggle = useCallback((id, key) => {
    if (key === 'activate') {
      setProfiles(prev => {
        const cur = prev.find(p => p.id === id)?.isActive;
        return prev.map(p => ({ ...p, isActive: cur ? false : p.id === id }));
      });
      return;
    }
    if (key === 'trash') {
      setProfiles(prev => {
        const name = prev.find(p => p.id === id)?.name;
        if (window.confirm(`Delete profile "${name}"?`)) return prev.filter(p => p.id !== id);
        return prev;
      });
      return;
    }
    const toggleMap = { bell:'silent', launch:'hasTrigger', auto:'autoSwitch', star:'isPinned', clock:'hasSchedule', sound:'hasSoundSwitch' };
    const field = toggleMap[key];
    if (field) setProfiles(prev => prev.map(p => p.id === id ? { ...p, [field]: !p[field] } : p));
  }, []);

  const handleSave = useCallback(updated => {
    setProfiles(prev => prev.map(p => p.id === updated.id ? updated : p));
  }, []);

  const filtered = profiles.filter(p => {
    if (!filters.length) return true;
    if (filters.includes('playback')  && p.mode !== 'Playback')  return false;
    if (filters.includes('recording') && p.mode !== 'Recording') return false;
    if (filters.includes('both')      && p.mode !== 'Both')      return false;
    if (filters.includes('pinned')    && !p.isPinned)            return false;
    if (filters.includes('active')    && !p.isActive)            return false;
    if (filters.includes('silent')    && !p.silent)              return false;
    if (filters.includes('hotkey')    && !p.hotkey)              return false;
    if (filters.includes('scheduled') && !p.hasSchedule)         return false;
    if (filters.includes('trigger')   && !p.hasTrigger)          return false;
    if (filters.includes('sound')     && !p.hasSoundSwitch)      return false;
    return true;
  });

  const expandedProfile = profiles.find(p => p.id === expandedId);

  function renderContent() {
    if (screen === 'settings') return <SettingsPanel settings={settings} onChange={setSettings} onClose={() => setScreen('main')} />;
    if (screen === 'about')    return <AboutPanel onClose={() => setScreen('main')} />;
    if (screen === 'faq')      return <FAQPanel onClose={() => setScreen('main')} />;

    return (
      <div className="vs-main" style={{ overflowY:'auto' }}>
        <div className="vs-grid-wrap">
          <div className="vs-grid-hdr">
            <span className="vs-grid-lbl">Profiles</span>
            <span className="vs-grid-cnt">{filtered.length}</span>
          </div>
          <div className="vs-grid">
            {filtered.map((p, i) => (
              <ProfileCard
                key={p.id}
                profile={p}
                onExpand={setExpandedId}
                onToggle={handleToggle}
                cardIndex={i}
              />
            ))}
            <div className="vs-card vs-card--add" style={{'--card-i': filtered.length}} onClick={() => alert('➕  Add New Profile — opens the profile type wizard')}>
              <svg width="28" height="28" viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><line x1="14" y1="5" x2="14" y2="23"/><line x1="5" y1="14" x2="23" y2="14"/></svg>
              <span>New Profile</span>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="vs-app">
      <TitleBar onClose={() => setScreen('main')} />
      <TopNav
        screen={screen}
        onScreen={handleScreen}
        filterOpen={filterOpen}
        onFilter={() => setFilterOpen(f => !f)}
        filterCount={filters.length}
      />
      <FilterBar active={filters} onToggle={handleToggleFilter} open={filterOpen} />
      {renderContent()}

      {expandedProfile && (
        <ProfileDetailModal
          profile={expandedProfile}
          onSave={handleSave}
          onClose={() => setExpandedId(null)}
        />
      )}
    </div>
  );
}

/* ── Root ── */
function App() {
  const [ready, setReady] = useState(false);
  return ready ? <MainApp /> : <SplashScreen onEnter={() => setReady(true)} />;
}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);
