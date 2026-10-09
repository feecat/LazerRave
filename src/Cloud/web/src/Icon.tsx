const paths = {
  ranking: ['M8 3h8v6a4 4 0 0 1-8 0V3Z', 'M8 5H4v2a4 4 0 0 0 4 4m8-6h4v2a4 4 0 0 1-4 4', 'M12 13v5m-4 3h8m-7-3h6v3'],
  pack: ['m12 3 9 5-9 5-9-5 9-5Z', 'M3 8v9l9 5 9-5V8M12 13v9M7.5 5.5l9 5'],
  table: ['m12 3 9 5-9 5-9-5 9-5Z', 'm3 12 9 5 9-5M3 16l9 5 9-5'],
  players: ['M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2', 'M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z', 'M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75'],
  admin: ['m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6l8-3Z', 'm9 12 2 2 4-4'],
  globe: ['M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z', 'M3 12h18M12 3a18 18 0 0 1 0 18 18 18 0 0 1 0-18Z'],
  chevron: ['m6 9 6 6 6-6'],
};

export type IconName = keyof typeof paths;
export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return <svg className="ui-icon" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
    {paths[name].map(path => <path key={path} d={path} />)}
  </svg>;
}
