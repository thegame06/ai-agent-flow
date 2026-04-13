const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5183/api/v1';

export async function fetchWithAuth(url: string, options: RequestInit = {}): Promise<Response> {
  const token = localStorage.getItem('auth_token');

  const headers = {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...options.headers
  };

  const response = await fetch(`${API_BASE_URL}${url}`, {
    ...options,
    headers
  });

  if (!response.ok) {
    if (response.status === 401) {
      window.location.href = '/login';
    }

    const maybeJson = await response.text();
    throw new Error(maybeJson || `API Error: ${response.status} ${response.statusText}`);
  }

  return response;
}
