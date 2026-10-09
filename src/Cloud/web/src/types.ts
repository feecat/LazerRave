export interface User { id: string; username: string; displayName: string; signature: string; bio: string; avatarUrl: string | null; role: 'player' | 'admin'; createdAt: string }
export interface Pack { id: string; title: string; description: string; sha256: string; sizeBytes: number; published?: boolean; createdAt: string }
export interface Chart { id: string; title: string; artist: string; difficulty: string; keys: number; level: number; sha256: string; md5: string; packId: string; path?: string }
export interface Score { id: string; rank: number; username: string; displayName: string; exScore: number; misses: number; maxCombo: number; clear: string; verified: boolean; createdAt: string; chartId?: string; title?: string }
export interface Member { id: string; username: string; displayName: string; avatarUrl: string | null; ready: boolean; exScore: number; combo: number; misses: number; progress: number; finished: boolean }
export interface Room { id: string; name: string; hostId: string; state: string; version: number; chart: { id: string; title: string; sha256: string; keys: number; packId: string } | null; matchId: string | null; startAt: string | null; members: Member[] }
export interface ChatMessage { id: number; channel: string; text: string; userId: string; username: string; displayName: string; createdAt: string }
