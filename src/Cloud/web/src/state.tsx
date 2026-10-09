import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { api } from './api';
import type { ChatMessage, Room, User } from './types';

const AuthContext = createContext<{ user: User | null; loaded: boolean; setUser: (user: User | null) => void }>({ user: null, loaded: false, setUser: () => {} });
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => { api<User | null>('/me').then(setUser).catch(e => setError(e.message)).finally(() => setLoaded(true)); }, []);
  return <AuthContext.Provider value={{ user, loaded, setUser }}>{error && <div className="service-alert" role="alert">{error}</div>}{children}</AuthContext.Provider>;
}
export const useAuth = () => useContext(AuthContext);

const LiveContext = createContext<{ connection: HubConnection | null; connected: boolean; rooms: Room[]; room: Room | null; setRoom: (room: Room | null) => void; messages: ChatMessage[]; error: string }>({ connection: null, connected: false, rooms: [], room: null, setRoom: () => {}, messages: [], error: '' });
export function LiveProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [connection, setConnection] = useState<HubConnection | null>(null);
  const [connected, setConnected] = useState(false);
  const [rooms, setRooms] = useState<Room[]>([]);
  const [room, setRoom] = useState<Room | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [error, setError] = useState('');
  useEffect(() => {
    if (!user) { setRooms([]); setRoom(null); setMessages([]); setError(''); return; }
    let active = true;
    const hub = new HubConnectionBuilder().withUrl('/hubs/realtime', { withCredentials: true }).withAutomaticReconnect([0, 2000, 5000, 10000]).configureLogging(LogLevel.Warning).build();
    hub.on('RoomsChanged', (value: Room[]) => { if (active) setRooms(value); });
    hub.on('RoomUpdated', (value: Room) => { if (active && value.members.some(m => m.id === user.id)) setRoom(previous => !previous || value.id === previous.id && value.version >= previous.version ? value : previous); });
    hub.on('ChatMessage', (value: ChatMessage) => { if (active) setMessages(previous => [...previous, value].slice(-200)); });
    hub.onreconnecting(() => { if (active) { setConnected(false); setError('Reconnecting…'); } });
    hub.onreconnected(async () => { if (active) { setConnected(true); setRoom(null); setError('Connection restored. Join your room again.'); setRooms(await api<Room[]>('/rooms')); } });
    hub.onclose(() => { if (active) { setConnected(false); setRoom(null); setError('Realtime connection closed. Refresh to reconnect.'); } });
    setConnection(hub);
    hub.start().then(async () => {
      if (!active) return;
      setConnected(true); setError('');
      setRooms(await api<Room[]>('/rooms'));
      const history = await api<ChatMessage[]>('/chat/lobby');
      if (active) setMessages(history.reverse());
    }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; setConnected(false); setConnection(null); void hub.stop(); };
  }, [user?.id]);
  return <LiveContext.Provider value={{ connection, connected, rooms, room, setRoom, messages, error }}>{children}</LiveContext.Provider>;
}
export const useLive = () => useContext(LiveContext);
