export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.method && !['GET', 'HEAD'].includes(init.method)) headers.set('X-LazerRave', '1');
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json');
  const response = await fetch('/api' + path, { ...init, headers, credentials: 'same-origin' });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.error || (response.status === 401 ? 'Please sign in to continue.' : response.status === 429 ? 'Too many requests. Please wait and try again.' : 'Request failed.'));
  }
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return text ? JSON.parse(text) as T : null as T;
}
export const send = <T>(path: string, method: string, body: unknown) => api<T>(path, { method, body: JSON.stringify(body) });
export const size = (bytes: number) => bytes < 1048576 ? `${(bytes / 1024).toFixed(0)} KiB` : `${(bytes / 1048576).toFixed(1)} MiB`;
export const date = (value: string, locale = 'en') => new Date(value).toLocaleDateString(locale, { year: 'numeric', month: 'short', day: 'numeric' });
