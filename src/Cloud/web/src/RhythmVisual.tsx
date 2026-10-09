import type { CSSProperties } from 'react';

export function RhythmVisual() {
  return <div className="rhythm-art" aria-hidden="true">
    <div className="rhythm-glow" />
    <div className="rhythm-stage">
      <div className="rhythm-orbit" /><div className="rhythm-orbit outer" />
      <div className="rhythm-ripple" /><div className="rhythm-ripple delayed" />
      <div className="rhythm-logo"><img src="/logo.svg" alt="" /></div>
      <div className="rhythm-lanes">{Array.from({ length: 7 }, (_, lane) =>
        <div className="rhythm-lane" key={lane} style={{ '--lane': lane } as CSSProperties}>
          <i className="falling-note" /><i className="falling-note second" /><span className="rhythm-key" />
        </div>)}<div className="rhythm-judgement" /></div>
    </div>
    <span className="art-caption">BMS / 5K · 7K · 9K · DP</span>
  </div>;
}
