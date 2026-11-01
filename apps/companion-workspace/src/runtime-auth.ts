let token = "";

export function setRuntimeToken(value: string): void { token = value; }
export function runtimeHeaders(): Record<string, string> { return token ? { Authorization: `Bearer ${token}` } : {}; }
