// card.jsx — Profile icons, action buttons, ProfileCard
const { useState, useRef } = React;

/* ── VS Brand Icon ───────────────────────────────────────── */
function VSSplashIcon({ size = 80 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 80 80" fill="none">
      <rect width="80" height="80" rx="20" fill="#13131e"/>
      <rect x="1" y="1" width="78" height="78" rx="19" stroke="rgba(245,130,10,.22)" strokeWidth="1.5" fill="none"/>
      <defs>
        <radialGradient id="vsGlow" cx="50%" cy="75%" r="55%">
          <stop offset="0%" stopColor="#f5820a" stopOpacity="0.08"/>
          <stop offset="100%" stopColor="#f5820a" stopOpacity="0"/>
        </radialGradient>
      </defs>
      <rect width="80" height="80" rx="20" fill="url(#vsGlow)"/>
      <path d="M19 22L40 54L61 22" stroke="#f5820a" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" fill="none"/>
      <rect x="22" y="61" width="4.5" height="7" rx="2.25" fill="#f5820a" fillOpacity="0.38"/>
      <rect x="29.5" y="56" width="4.5" height="12" rx="2.25" fill="#f5820a" fillOpacity="0.62"/>
      <rect x="37.75" y="58.5" width="4.5" height="9.5" rx="2.25" fill="#f5820a"/>
      <rect x="46" y="56" width="4.5" height="12" rx="2.25" fill="#f5820a" fillOpacity="0.62"/>
      <rect x="53.5" y="61" width="4.5" height="7" rx="2.25" fill="#f5820a" fillOpacity="0.38"/>
    </svg>
  );
}

/* ── Profile SVG Icons ───────────────────────────────────── */
function SpeakerIcon() {
  return (
    <svg width="46" height="46" viewBox="0 0 46 46" fill="none">
      <rect width="46" height="46" rx="11" fill="rgba(245,130,10,.1)"/>
      <path d="M13 18.5h5l9-6.5v22l-9-6.5h-5v-9z" fill="#f5820a"/>
      <path d="M31 16c2.2 1.8 3.5 4.3 3.5 7s-1.3 5.2-3.5 7" stroke="#f5820a" strokeWidth="2" strokeLinecap="round" fill="none"/>
      <path d="M33.5 11.5c3.5 2.8 5.5 7 5.5 11.5s-2 8.7-5.5 11.5" stroke="#f5820a" strokeWidth="1.5" strokeLinecap="round" fill="none" opacity="0.35"/>
    </svg>
  );
}

function HeadsetIcon() {
  return (
    <svg width="46" height="46" viewBox="0 0 46 46" fill="none">
      <rect width="46" height="46" rx="11" fill="rgba(245,130,10,.1)"/>
      <path d="M11 27.5V22A12 12 0 0 1 35 22v5.5" stroke="#f5820a" strokeWidth="2.2" strokeLinecap="round" fill="none"/>
      <rect x="9" y="25" width="5.5" height="10" rx="2.75" fill="#f5820a"/>
      <rect x="31.5" y="25" width="5.5" height="10" rx="2.75" fill="#f5820a"/>
      <path d="M36.5 32v3a5 5 0 0 1-5 5h-4" stroke="#f5820a" strokeWidth="1.8" strokeLinecap="round" fill="none" opacity="0.5"/>
    </svg>
  );
}

function MicIcon() {
  return (
    <svg width="46" height="46" viewBox="0 0 46 46" fill="none">
      <rect width="46" height="46" rx="11" fill="rgba(48,210,120,.1)"/>
      <rect x="17.5" y="8" width="11" height="19" rx="5.5" fill="#30d278"/>
      <path d="M11 26c0 6.6 5.4 12 12 12s12-5.4 12-12" stroke="#30d278" strokeWidth="2.2" strokeLinecap="round" fill="none"/>
      <line x1="23" y1="38" x2="23" y2="42" stroke="#30d278" strokeWidth="2.2" strokeLinecap="round"/>
      <line x1="17" y1="42" x2="29" y2="42" stroke="#30d278" strokeWidth="2.2" strokeLinecap="round"/>
    </svg>
  );
}

function GamingIcon() {
  return (
    <svg width="46" height="46" viewBox="0 0 46 46" fill="none">
      <rect width="46" height="46" rx="11" fill="rgba(74,168,255,.1)"/>
      <path d="M9 23c0-7 3.5-11.5 9.5-11.5H27.5c6 0 9.5 4.5 9.5 11.5s-2.5 11-7 11H16c-4.5 0-7-4-7-11z" stroke="#4aa8ff" strokeWidth="1.8" fill="none"/>
      <path d="M17 21v4M15 23h4" stroke="#4aa8ff" strokeWidth="2" strokeLinecap="round"/>
      <circle cx="31" cy="21.5" r="1.8" fill="#4aa8ff"/>
      <circle cx="27.5" cy="24.5" r="1.8" fill="#4aa8ff"/>
      <circle cx="34.5" cy="24.5" r="1.8" fill="#4aa8ff"/>
    </svg>
  );
}

function MeetingIcon() {
  return (
    <svg width="46" height="46" viewBox="0 0 46 46" fill="none">
      <rect width="46" height="46" rx="11" fill="rgba(155,105,255,.1)"/>
      <rect x="8" y="15.5" width="22" height="15" rx="3.5" stroke="#9b69ff" strokeWidth="1.8" fill="none"/>
      <path d="M30 21.5l9-4.5v13l-9-4.5V21.5z" stroke="#9b69ff" strokeWidth="1.8" strokeLinejoin="round" fill="none"/>
    </svg>
  );
}

function DefaultIcon({ name }) {
  const pal = ['#f5820a','#4aa8ff','#30d278','#9b69ff','#ff6b8a','#ffc340'];
  const c = pal[name.charCodeAt(0) % pal.length];
  return (
    <svg width="46" height="46" viewBox="0 0 46 46" fill="none">
      <rect width="46" height="46" rx="11" fill={c} fillOpacity="0.12"/>
      <text x="23" y="31" textAnchor="middle" fill={c} fontSize="19" fontWeight="700" fontFamily="Inter,sans-serif">{name[0]?.toUpperCase()}</text>
    </svg>
  );
}

const ICON_MAP = { speaker: SpeakerIcon, headset: HeadsetIcon, mic: MicIcon, stream: MicIcon, gaming: GamingIcon, meeting: MeetingIcon };

function ProfileIcon({ iconType, name }) {
  const C = ICON_MAP[iconType];
  return C ? <C /> : <DefaultIcon name={name || '?'} />;
}

/* ── Action Button Icons ─────────────────────────────────── */
const AI = {
  bell:     <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round"><path d="M8 2a5 5 0 0 0-5 5v3l-1 2h12l-1-2V7a5 5 0 0 0-5-5z"/><path d="M6.5 13.5a1.5 1.5 0 0 0 3 0"/><line x1="1" y1="1" x2="15" y2="15" strokeWidth="1.9"/></svg>,
  launch:   <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round"><path d="M7 3H4a1 1 0 0 0-1 1v8a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1v-3"/><path d="M10 2h4v4"/><line x1="9.5" y1="6.5" x2="14" y2="2"/></svg>,
  auto:     <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round"><path d="M14 8A6 6 0 0 1 3 11.2"/><path d="M2 8A6 6 0 0 1 13 4.8"/><polyline points="2,11.5 2,8 5,8"/><polyline points="14,4.5 14,8 11,8"/></svg>,
  star:     <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><polygon points="8,1.5 10.2,5.8 15,6.4 11.5,9.8 12.4,14.5 8,12.1 3.6,14.5 4.5,9.8 1,6.4 5.8,5.8"/></svg>,
  activate: <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round"><circle cx="8" cy="8" r="5.5"/><polyline points="5.5,8 7.5,10.2 11,6"/></svg>,
  clone:    <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round"><rect x="5.5" y="5.5" width="8" height="8" rx="1.5"/><path d="M3.5 10.5V3.5a1 1 0 0 1 1-1h7"/></svg>,
  clock:    <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round"><circle cx="8" cy="8" r="5.5"/><polyline points="8,5 8,8.5 10.5,10.2"/></svg>,
  sound:    <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round"><path d="M5 5.5h2l4-3.5v12l-4-3.5H5v-5z"/><path d="M11 6.8c.7.6 1.1 1.4 1.1 2.2s-.4 1.6-1.1 2.2" opacity="0.75"/></svg>,
  trash:    <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round"><polyline points="2,4 14,4"/><path d="M5.5 4V2.5h5V4"/><path d="M3.5 4l.8 9.5h7.4L12.5 4"/></svg>,
};

/* ── ProfileCard ─────────────────────────────────────────── */
function ProfileCard({ profile, onExpand, onToggle }) {
  const ref = useRef(null);

  function handleClick() {
    const el = ref.current;
    if (el) {
      el.classList.add('vs-card--clicking');
      setTimeout(() => el.classList.remove('vs-card--clicking'), 230);
    }
    setTimeout(() => onExpand(profile.id), 190);
  }

  const modeKey = profile.mode === 'Both' ? 'both' : profile.mode === 'Playback' ? 'pb' : 'rec';
  const modeLabel = { Both: 'Both Devices', Playback: 'Playback', Recording: 'Recording' }[profile.mode];

  const acts = [
    { k:'bell',     icon:AI.bell,     tip:'No Notification', on:profile.silent },
    { k:'launch',   icon:AI.launch,   tip:'App Trigger',     on:profile.hasTrigger },
    { k:'auto',     icon:AI.auto,     tip:'Auto-Switch',     on:profile.autoSwitch },
    { k:'star',     icon:AI.star,     tip:'Favorite',        on:profile.isPinned },
    { k:'activate', icon:AI.activate, tip:'Activate',        on:profile.isActive, isAct:true },
    { k:'clone',    icon:AI.clone,    tip:'Clone',           on:false },
    { k:'clock',    icon:AI.clock,    tip:'Scheduler',       on:profile.hasSchedule },
    { k:'sound',    icon:AI.sound,    tip:'Sound Switch',    on:profile.hasSoundSwitch },
    { k:'trash',    icon:AI.trash,    tip:'Delete',          on:false, danger:true },
  ];

  return (
    <div
      ref={ref}
      className={`vs-card${profile.isActive ? ' vs-card--active' : ''}`}
      onClick={handleClick}
    >
      {profile.isActive && <div className="vs-card__bar" />}

      <div className="vs-card__ico">
        <ProfileIcon iconType={profile.iconType} name={profile.name} />
      </div>

      <p className="vs-card__name">{profile.name}</p>

      <span className={`vs-badge vs-badge--${modeKey}`}>{modeLabel}</span>

      <div className="vs-card__devs">
        {profile.playbackDevice && (
          <div className="vs-card__dev">
            <span className="vs-card__devdot vs-card__devdot--pb" />
            <span>{profile.playbackDevice}</span>
          </div>
        )}
        {profile.recordingDevice && (
          <div className="vs-card__dev">
            <span className="vs-card__devdot vs-card__devdot--rec" />
            <span>{profile.recordingDevice}</span>
          </div>
        )}
      </div>

      <div className="vs-card__foot">
        {profile.isActive    && <span className="vsdot vsdot--g" title="Active" />}
        {profile.isPinned    && <span className="vsdot vsdot--o" title="Pinned" />}
        {profile.hasSchedule && <span className="vsdot vsdot--b" title="Scheduled" />}
        {profile.hotkey      && <code className="vs-hk">{profile.hotkey}</code>}
      </div>

      <div className="vs-card__acts" onClick={e => e.stopPropagation()}>
        {acts.map(a => (
          <button
            key={a.k}
            className={`vs-act${a.on ? (a.isAct && a.on ? ' vs-act--activated' : ' vs-act--on') : ''}${a.danger ? ' vs-act--del' : ''}`}
            title={a.tip}
            onClick={e => { e.stopPropagation(); onToggle(profile.id, a.k); }}
          >{a.icon}</button>
        ))}
      </div>
    </div>
  );
}

Object.assign(window, { ProfileCard, ProfileIcon, VSSplashIcon });
